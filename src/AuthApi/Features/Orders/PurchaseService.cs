using AuthApi.Data;
using AuthApi.Features.Carts;
using AuthApi.Features.Customers;
using AuthApi.Features.Inventory;
using AuthApi.Features.Products;
using AuthApi.Features.Promotions;
using MongoDB.Driver;

namespace AuthApi.Features.Orders;

internal sealed class PurchaseService(
    IMongoDatabase db,
    CounterService counters,
    InventoryService stock,
    CouponService coupons)
{
    private readonly IMongoCollection<OrderDocument> orders = db.GetCollection<OrderDocument>("orders");
    private readonly IMongoCollection<OrderItemDocument> orderItems = db.GetCollection<OrderItemDocument>("order_items");
    private readonly IMongoCollection<OrderStatusHistoryDocument> history =
        db.GetCollection<OrderStatusHistoryDocument>("order_status_history");
    private readonly IMongoCollection<CartDocument> carts = db.GetCollection<CartDocument>("carts");
    private readonly IMongoCollection<CartItemDocument> cartItems = db.GetCollection<CartItemDocument>("cart_items");
    private readonly IMongoCollection<DeliveryAddressDocument> addresses =
        db.GetCollection<DeliveryAddressDocument>("addresses");
    private readonly ProductCatalogService catalog = new(db);

    public async Task<(PurchaseResponse? Response, IResult? Error)> PurchaseAsync(
        long userId, PurchaseRequest request, string? idempotencyKey)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await orders.Find(o =>
                o.UserId == userId && o.IdempotencyKey == idempotencyKey).FirstOrDefaultAsync();
            if (existing is not null)
                return (await BuildResponseAsync(existing), null);
        }

        var errors = new Dictionary<string, string[]>();
        var paymentMethod = (request.PaymentMethod ?? PaymentMethods.Cod).Trim().ToLowerInvariant();
        if (paymentMethod is not (PaymentMethods.Cod or PaymentMethods.BankTransfer))
            errors["paymentMethod"] = ["paymentMethod must be cod or bank_transfer."];

        var note = string.IsNullOrWhiteSpace(request.CustomerNote) ? null : request.CustomerNote.Trim();
        if (note?.Length > 1000) errors["customerNote"] = ["Customer note must be at most 1000 characters."];

        CouponItemRequest[] items;
        if (request.Items is { Length: > 0 })
        {
            items = request.Items;
        }
        else
        {
            var cart = await carts.Find(c => c.UserId == userId && c.Status == "active").FirstOrDefaultAsync();
            if (cart is null)
                items = [];
            else
                items = (await cartItems.Find(i => i.CartId == cart.CartId).ToListAsync())
                    .Select(i => new CouponItemRequest(i.VariantId, i.Quantity)).ToArray();
        }

        if (items.Length is < 1 or > CheckoutPricing.MaxCartLines)
            errors["items"] = ["The cart must contain between 1 and 50 purchasable items."];
        var normalizedItems = new List<CouponItemRequest>();
        var requested = new Dictionary<long, int>();
        foreach (var item in items)
        {
            if (item.VariantId is null or < 1)
            {
                errors["items"] = ["Each item requires a valid variantId."];
                continue;
            }
            if (item.Quantity is null or < 1 or > CartService.MaxQuantityPerAdd)
            {
                errors["items"] = ["Each item quantity must be between 1 and 99."];
                continue;
            }
            requested[item.VariantId.Value] = requested.GetValueOrDefault(item.VariantId.Value) + item.Quantity.Value;
        }
        foreach (var (variantId, quantity) in requested)
            normalizedItems.Add(new CouponItemRequest(variantId, quantity));
        if (normalizedItems.Count > CheckoutPricing.MaxCartLines)
            errors["items"] = ["The cart must contain no more than 50 distinct variants."];

        DeliveryAddressDocument? address = null;
        if (request.AddressId is { } addressId)
            address = await addresses.Find(a => a.AddressId == addressId && a.UserId == userId).FirstOrDefaultAsync();
        else
            address = await addresses.Find(a => a.UserId == userId)
                .SortByDescending(a => a.IsDefault).ThenBy(a => a.AddressId).FirstOrDefaultAsync();
        if (address is null)
            errors["addressId"] = ["A valid delivery address is required."];
        if (errors.Count > 0) return (null, Results.ValidationProblem(errors));

        var bundle = await catalog.LoadForVariantsAsync(requested.Keys.ToArray());
        var snapshots = new List<(long VariantId, long ProductId, string? ProductName, string? Sku,
            string? Size, string? Color, int Quantity, double UnitPrice, double LineTotal)>();
        foreach (var (variantId, quantity) in requested)
        {
            if (!bundle.VariantsById.TryGetValue(variantId, out var variant)
                || variant.Status != "active"
                || bundle.Products.GetValueOrDefault(variant.ProductId) is not { Status: "active" } product)
            {
                return (null, Results.Conflict(new { message = $"Variant {variantId} is not available." }));
            }
            var available = bundle.AvailableFor(variantId);
            if (quantity > available)
            {
                return (null, Results.Conflict(new
                {
                    message = $"Only {available} item(s) available for variant {variantId}.",
                    variantId,
                    availableQuantity = available
                }));
            }
            var price = bundle.PriceFor(variant);
            if (price is null)
                return (null, Results.Conflict(new { message = $"Variant {variantId} has no valid price." }));
            bundle.Colors.TryGetValue(variant.ColorId, out var color);
            snapshots.Add((variantId, variant.ProductId, product.Name, variant.Sku,
                bundle.SizeNames.GetValueOrDefault(variant.SizeId), color?.Name,
                quantity, price.Value, price.Value * quantity));
        }

        var code = request.CouponCode ?? request.Code;
        var quote = string.IsNullOrWhiteSpace(code) && request.UserCouponId is null
            ? new CouponService.QuoteOutcome(null, null, null)
            : await coupons.QuoteAsync(userId, code, request.UserCouponId, normalizedItems);
        if (quote.ItemsError is not null)
            return (null, Results.ValidationProblem(new Dictionary<string, string[]> { ["items"] = [quote.ItemsError] }));
        if (quote.CouponError is not null)
            return (null, Results.ValidationProblem(new Dictionary<string, string[]> { ["couponCode"] = [quote.CouponError] }));
        var subtotal = snapshots.Sum(s => s.LineTotal);
        var discount = quote.Quote?.DiscountAmount ?? 0;
        var shippingFee = quote.Quote?.ShippingFee ?? (subtotal >= CheckoutPricing.FreeShippingThreshold ? 0 : CheckoutPricing.ShippingFee);
        var shippingDiscount = quote.Quote?.ShippingDiscount ?? 0;
        var total = subtotal - discount + shippingFee - shippingDiscount;

        var now = DateTime.UtcNow;
        var orderId = await counters.NextAsync("orders");
        var orderCode = $"THM-{1000 + orderId}";
        var order = new OrderDocument
        {
            OrderId = orderId,
            OrderCode = orderCode,
            UserId = userId,
            CouponId = quote.Quote?.Coupon.CouponId,
            Status = OrderStatuses.Pending,
            PaymentMethod = paymentMethod,
            PaymentStatus = paymentMethod == PaymentMethods.Cod ? PaymentStatuses.Unpaid : PaymentStatuses.Pending,
            RecipientName = address!.RecipientName,
            RecipientPhone = address!.RecipientPhone,
            ShippingAddressLine = address!.AddressLine,
            ShippingWard = address!.Ward,
            ShippingDistrict = address!.District,
            ShippingProvince = address!.Province,
            ShippingPostalCode = null,
            ShippingProvinceCode = address!.ProvinceCode,
            ShippingDistrictCode = address!.DistrictCode,
            ShippingWardCode = address!.WardCode,
            Subtotal = subtotal,
            DiscountAmount = discount,
            ShippingFee = shippingFee,
            TotalAmount = total,
            CustomerNote = note,
            PlacedAt = now,
            UpdatedAt = now,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey
        };

        var reserved = new List<(long VariantId, int Quantity)>();
        foreach (var line in snapshots)
        {
            if (!await stock.ReserveAsync(line.VariantId, line.Quantity, "order", orderId, userId))
            {
                foreach (var undo in reserved)
                    await stock.CompensateAsync(undo.VariantId, 0, -undo.Quantity, "order", orderId, userId);
                return (null, Results.Conflict(new { message = $"Stock changed for variant {line.VariantId}. Please retry." }));
            }
            reserved.Add((line.VariantId, line.Quantity));
        }

        if (quote.Quote is not null &&
            !await coupons.RedeemAsync(userId, quote.Quote.Coupon.CouponId,
                quote.Quote.UserCouponId, orderId, discount))
        {
            foreach (var undo in reserved)
                await stock.CompensateAsync(undo.VariantId, 0, -undo.Quantity, "order", orderId, userId);
            return (null, Results.Conflict(new { message = "The coupon was changed or has reached its limit. Please retry." }));
        }

        try
        {
            await orders.InsertOneAsync(order);
            var itemDocuments = new List<OrderItemDocument>();
            foreach (var line in snapshots)
            {
                itemDocuments.Add(new OrderItemDocument
                {
                    OrderItemId = await counters.NextAsync("order_items"),
                    OrderId = orderId,
                    VariantId = line.VariantId,
                    ProductId = line.ProductId,
                    ProductNameSnapshot = line.ProductName,
                    SkuSnapshot = line.Sku,
                    SizeSnapshot = line.Size,
                    ColorSnapshot = line.Color,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    DiscountAmount = discount == 0 ? 0 :
                        Math.Round(line.LineTotal / subtotal * discount, MidpointRounding.AwayFromZero),
                    LineTotal = line.LineTotal
                });
            }
            await orderItems.InsertManyAsync(itemDocuments);
            await history.InsertOneAsync(new OrderStatusHistoryDocument
            {
                HistoryId = await counters.NextAsync("order_status_history"),
                OrderId = orderId,
                OldStatus = null,
                NewStatus = OrderStatuses.Pending,
                ChangedBy = userId,
                Note = "Customer placed order",
                ChangedAt = now
            });

            var activeCart = await carts.Find(c => c.UserId == userId && c.Status == "active").FirstOrDefaultAsync();
            if (activeCart is not null)
            {
                await cartItems.DeleteManyAsync(i => i.CartId == activeCart.CartId);
                await carts.UpdateOneAsync(c => c.CartId == activeCart.CartId,
                    MongoDB.Driver.Builders<CartDocument>.Update.Set(c => c.Status, "converted").Set(c => c.UpdatedAt, now));
            }
        }
        catch
        {
            if (quote.Quote is not null) await coupons.ReleaseAsync(orderId);
            foreach (var undo in reserved)
                await stock.CompensateAsync(undo.VariantId, 0, -undo.Quantity, "order", orderId, userId);
            throw;
        }

        return (await BuildResponseAsync(order), null);
    }

    private async Task<PurchaseResponse> BuildResponseAsync(OrderDocument order)
    {
        var lines = await orderItems.Find(i => i.OrderId == order.OrderId).SortBy(i => i.OrderItemId).ToListAsync();
        return new PurchaseResponse(
            order.OrderId, order.OrderCode, order.Status, order.PlacedAt,
            order.Subtotal, order.DiscountAmount, order.ShippingFee,
            Math.Max(0, order.ShippingFee - (order.TotalAmount - order.Subtotal + order.DiscountAmount)),
            order.TotalAmount, order.PaymentMethod ?? PaymentMethods.Cod,
            order.PaymentStatus ?? (order.PaymentMethod == PaymentMethods.Cod ? PaymentStatuses.Unpaid : PaymentStatuses.Pending),
            order.RecipientName ?? string.Empty, order.RecipientPhone ?? string.Empty,
            order.ShippingAddressLine ?? string.Empty, order.ShippingWard, order.ShippingDistrict,
            order.ShippingProvince, order.ShippingPostalCode, order.ShippingProvinceCode,
            order.ShippingDistrictCode, order.ShippingWardCode,
            lines.Select(i => new PurchaseItemResponse(
                i.VariantId, i.ProductId, i.ProductNameSnapshot, i.SkuSnapshot, i.SizeSnapshot,
                i.ColorSnapshot, i.Quantity, i.UnitPrice, i.DiscountAmount, i.LineTotal)).ToArray());
    }
}
