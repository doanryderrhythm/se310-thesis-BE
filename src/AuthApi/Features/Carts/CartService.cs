using AuthApi.Data;
using AuthApi.Features.Products;
using MongoDB.Driver;

namespace AuthApi.Features.Carts;

internal sealed class CartService(IMongoDatabase db, CounterService counters, ProductCatalogService catalog)
{
    public const int MaxQuantityPerAdd = 99;
    private readonly IMongoCollection<CartDocument> carts = db.GetCollection<CartDocument>("carts");
    private readonly IMongoCollection<CartItemDocument> items = db.GetCollection<CartItemDocument>("cart_items");

    public static async Task SetupAsync(IMongoDatabase db, CounterService counters)
    {
        var carts = db.GetCollection<CartDocument>("carts");
        var items = db.GetCollection<CartItemDocument>("cart_items");
        await carts.Indexes.CreateOneAsync(new CreateIndexModel<CartDocument>(
            Builders<CartDocument>.IndexKeys.Ascending(c => c.UserId),
            new CreateIndexOptions<CartDocument>
            {
                Unique = true,
                Name = "active_cart_per_user",
                PartialFilterExpression = new MongoDB.Bson.BsonDocument("status", "active")
            }));
        await items.Indexes.CreateOneAsync(new CreateIndexModel<CartItemDocument>(
            Builders<CartItemDocument>.IndexKeys.Ascending(i => i.CartId).Ascending(i => i.VariantId),
            new CreateIndexOptions { Unique = true, Name = "cart_variant_unique" }));
        await counters.InitializeAsync("carts", await carts.Find(Builders<CartDocument>.Filter.Empty).SortByDescending(c => c.CartId).Project(c => c.CartId).FirstOrDefaultAsync());
        await counters.InitializeAsync("cart_items", await items.Find(Builders<CartItemDocument>.Filter.Empty).SortByDescending(i => i.CartItemId).Project(i => i.CartItemId).FirstOrDefaultAsync());
    }

    public async Task<CartResponse> GetAsync(long userId)
    {
        var cart = await FindActiveCartAsync(userId);
        return cart is null ? new CartResponse(null, [], 0, 0, 0, 0) : await BuildResponseAsync(cart);
    }

    public async Task<(CartResponse? Cart, IResult? Error)> AddItemAsync(long userId, long variantId, int quantity)
    {
        var bundle = await catalog.LoadForVariantsAsync([variantId]);
        if (!bundle.VariantsById.TryGetValue(variantId, out var variant)
            || variant.Status != "active"
            || bundle.Products.GetValueOrDefault(variant.ProductId)?.Status != "active")
        {
            return (null, Results.NotFound(new { message = "Product variant not found." }));
        }

        var available = bundle.AvailableFor(variantId);
        if (quantity > available)
        {
            return (null, OutOfStock(available));
        }

        var cart = await GetOrCreateCartAsync(userId);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var existing = await items.Find(i => i.CartId == cart.CartId && i.VariantId == variantId)
                .FirstOrDefaultAsync();
            var newQuantity = (existing?.Quantity ?? 0) + quantity;
            if (newQuantity > available)
            {
                return (null, OutOfStock(available));
            }

            if (existing is not null)
            {
                var result = await items.UpdateOneAsync(
                    i => i.CartItemId == existing.CartItemId && i.Quantity == existing.Quantity,
                    Builders<CartItemDocument>.Update.Set(i => i.Quantity, newQuantity)
                        .Set(i => i.PriceAtAdd, bundle.PriceFor(bundle.VariantsById[variantId])));
                if (result.ModifiedCount == 0) continue;
            }
            else
            {
                try
                {
                    await items.InsertOneAsync(new CartItemDocument
                    {
                        CartItemId = await counters.NextAsync("cart_items"),
                        CartId = cart.CartId,
                        VariantId = variantId,
                        Quantity = newQuantity,
                        AddedAt = DateTime.UtcNow,
                        Selected = true,
                        PriceAtAdd = bundle.PriceFor(bundle.VariantsById[variantId])
                    });
                }
                catch (MongoWriteException exception) when (exception.WriteError?.Code == 11000)
                {
                    continue;
                }
            }

            await Touch(cart);
            return (await BuildResponseAsync(cart), null);
        }

        return (null, Results.Conflict(new { message = "The cart was modified concurrently. Please retry." }));
    }

    /// <summary>Changes the quantity of a line and/or ticks it for checkout.</summary>
    public async Task<(CartResponse? Cart, IResult? Error)> UpdateItemAsync(
        long userId, long variantId, int? quantity, bool? selected)
    {
        var cart = await FindActiveCartAsync(userId);
        var line = cart is null ? null : await items.Find(i => i.CartId == cart.CartId && i.VariantId == variantId).FirstOrDefaultAsync();
        if (cart is null || line is null) return (null, Results.NotFound(new { message = "Cart item not found." }));

        var update = Builders<CartItemDocument>.Update;
        var changes = new List<UpdateDefinition<CartItemDocument>>();
        if (quantity is { } wanted)
        {
            var bundle = await catalog.LoadForVariantsAsync([variantId]);
            if (!bundle.VariantsById.TryGetValue(variantId, out var variant)
                || variant.Status != "active"
                || bundle.Products.GetValueOrDefault(variant.ProductId)?.Status != "active")
            {
                return (null, Results.Conflict(new { message = "This product is no longer available; remove it from the cart." }));
            }

            var available = bundle.AvailableFor(variantId);
            if (wanted > available) return (null, OutOfStock(available));
            changes.Add(update.Set(i => i.Quantity, wanted));
        }
        if (selected is { } tick) changes.Add(update.Set(i => i.Selected, tick));

        await items.UpdateOneAsync(i => i.CartItemId == line.CartItemId, update.Combine(changes));
        await Touch(cart);
        return (await BuildResponseAsync(cart), null);
    }

    /// <summary>
    /// Merges the cart a visitor built before logging in: quantities are added to the account cart and
    /// capped at the available stock; lines that cannot be bought are reported and skipped.
    /// </summary>
    public async Task<MergeCartResponse> MergeAsync(long userId, IReadOnlyList<(long VariantId, int Quantity)> lines)
    {
        var bundle = await catalog.LoadForVariantsAsync(lines.Select(l => l.VariantId).ToArray());
        var cart = await GetOrCreateCartAsync(userId);
        var results = new List<MergeLineResult>();
        foreach (var (variantId, requested) in lines)
        {
            if (!bundle.VariantsById.TryGetValue(variantId, out var variant)
                || variant.Status != "active"
                || bundle.Products.GetValueOrDefault(variant.ProductId)?.Status != "active")
            {
                results.Add(new MergeLineResult(variantId, requested, 0, 0, "unavailable"));
                continue;
            }

            var cap = Math.Min(bundle.AvailableFor(variantId), MaxQuantityPerAdd);
            var done = false;
            for (var attempt = 0; attempt < 3 && !done; attempt++)
            {
                var existing = await items.Find(i => i.CartId == cart.CartId && i.VariantId == variantId).FirstOrDefaultAsync();
                var current = existing?.Quantity ?? 0;
                var target = Math.Min(cap, current + requested);
                if (target <= current)
                {
                    results.Add(new MergeLineResult(variantId, requested, 0, current, cap <= 0 ? "out_of_stock" : "capped"));
                    done = true;
                    break;
                }

                if (existing is not null)
                {
                    var changed = await items.UpdateOneAsync(
                        i => i.CartItemId == existing.CartItemId && i.Quantity == existing.Quantity,
                        Builders<CartItemDocument>.Update.Set(i => i.Quantity, target));
                    if (changed.ModifiedCount == 0) continue;
                }
                else
                {
                    try
                    {
                        await items.InsertOneAsync(new CartItemDocument
                        {
                            CartItemId = await counters.NextAsync("cart_items"), CartId = cart.CartId, VariantId = variantId,
                            Quantity = target, AddedAt = DateTime.UtcNow, Selected = true, PriceAtAdd = bundle.PriceFor(variant)
                        });
                    }
                    catch (MongoWriteException exception) when (exception.WriteError?.Code == 11000)
                    {
                        continue;
                    }
                }

                results.Add(new MergeLineResult(variantId, requested, target - current, target,
                    target < current + requested ? "capped" : "added"));
                done = true;
            }

            if (!done) results.Add(new MergeLineResult(variantId, requested, 0, 0, "conflict"));
        }

        await Touch(cart);
        return new MergeCartResponse(await BuildResponseAsync(cart), [.. results]);
    }

    private static IResult OutOfStock(int available) => Results.Conflict(new
    {
        message = $"Only {available} item(s) available for this variant.",
        availableQuantity = available
    });

    public async Task<CartResponse?> RemoveItemAsync(long userId, long variantId)
    {
        var cart = await FindActiveCartAsync(userId);
        if (cart is null) return null;
        var result = await items.DeleteOneAsync(i => i.CartId == cart.CartId && i.VariantId == variantId);
        if (result.DeletedCount == 0) return null;
        await Touch(cart);
        return await BuildResponseAsync(cart);
    }

    private Task Touch(CartDocument cart) =>
        carts.UpdateOneAsync(c => c.CartId == cart.CartId,
            Builders<CartDocument>.Update.Set(c => c.UpdatedAt, DateTime.UtcNow));

    private Task<CartDocument?> FindActiveCartAsync(long userId) =>
        carts.Find(c => c.UserId == userId && c.Status == "active").FirstOrDefaultAsync()!;

    private async Task<CartDocument> GetOrCreateCartAsync(long userId)
    {
        var cart = await FindActiveCartAsync(userId);
        if (cart is not null) return cart;
        var now = DateTime.UtcNow;
        try
        {
            cart = new CartDocument
            {
                CartId = await counters.NextAsync("carts"),
                UserId = userId,
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now
            };
            await carts.InsertOneAsync(cart);
            return cart;
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Code == 11000)
        {
            return (await FindActiveCartAsync(userId))!;
        }
    }

    private async Task<CartResponse> BuildResponseAsync(CartDocument cart)
    {
        var lines = await items.Find(i => i.CartId == cart.CartId).SortBy(i => i.CartItemId).ToListAsync();
        var bundle = await catalog.LoadForVariantsAsync(lines.Select(l => l.VariantId).ToArray());
        var responses = new List<CartItemResponse>();
        foreach (var line in lines)
        {
            if (!bundle.VariantsById.TryGetValue(line.VariantId, out var variant)) continue;
            var product = bundle.Products.GetValueOrDefault(variant.ProductId);
            if (product is null) continue;
            bundle.Colors.TryGetValue(variant.ColorId, out var color);
            var available = bundle.AvailableFor(variant.VariantId);
            var price = bundle.PriceFor(variant);
            var purchasable = variant.Status == "active" && product.Status == "active"
                && price is not null && line.Quantity <= available;
            responses.Add(new CartItemResponse(
                variant.VariantId, product.ProductId, product.Name,
                bundle.ImagesByProduct.GetValueOrDefault(product.ProductId)?.FirstOrDefault()?.ImageUrl,
                variant.Sku, bundle.SizeNames.GetValueOrDefault(variant.SizeId),
                color?.Name, color?.HexCode, price, line.Quantity, available,
                (price ?? 0) * line.Quantity, purchasable,
                line.Selected ?? true, line.PriceAtAdd, line.PriceAtAdd is { } added && price is { } now && added != now));
        }

        var payable = responses.Where(r => r.IsPurchasable).ToArray();
        var chosen = payable.Where(r => r.Selected).ToArray();
        return new CartResponse(cart.CartId, responses.ToArray(),
            responses.Sum(r => r.Quantity), payable.Sum(r => r.LineTotal),
            chosen.Sum(r => r.Quantity), chosen.Sum(r => r.LineTotal));
    }
}
