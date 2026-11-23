using AuthApi.Common;
using AuthApi.Features.Catalog;
using AuthApi.Features.Promotions;
using AuthApi.Features.Returns;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AuthApi.Features.Products;

internal sealed record ProductDetailVariantResponse(
    long VariantId, string? Sku, string? Size, string? Color, string? HexCode,
    double? Price, string? Status, int QuantityOnHand, int QuantityReserved,
    int AvailableQuantity, bool InStock, long SizeId = 0, long ColorId = 0);

internal sealed record SizeChartItemResponse(
    string? SizeLabel, double? ChestCm, double? WaistCm, double? HipCm, double? LengthCm, string? Notes,
    double? HeightMinCm = null, double? HeightMaxCm = null, double? WeightMinKg = null, double? WeightMaxKg = null);

internal sealed record SizeChartResponse(
    long SizeChartId, string? Name, string? Description, SizeChartItemResponse[] Items);

internal sealed record ProductDetailResponse(
    long ProductId, long CategoryId, string? CategoryName, string Name, string? Description,
    string? Material, double? BasePrice, string? Status,
    PublicImage[] Images, ProductDetailVariantResponse[] Variants,
    int TotalAvailableQuantity, SizeChartResponse? SizeChart,
    string? Slug = null, PublicRef? MaterialGroup = null, PublicRef[]? Occasions = null, string? CareInstructions = null);

internal static class ProductEndpoints
{
    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    public static void MapProductEndpoints(this IEndpointRouteBuilder app, IMongoDatabase db, IConfiguration configuration)
    {
        var catalog = new ProductCatalogService(db);
        var listing = new ProductListService(db);
        var materials = db.GetCollection<BsonDocument>("materials");
        var occasions = db.GetCollection<BsonDocument>("occasions");

        async Task<ProductDetailResponse?> BuildDetailAsync(ProductDocument product)
        {
            var bundle = await catalog.LoadForProductsAsync([product]);
            var variants = bundle.VariantsByProduct.GetValueOrDefault(product.ProductId, [])
                .Where(v => v.Status == "active")
                .Select(v =>
                {
                    bundle.Colors.TryGetValue(v.ColorId, out var color);
                    bundle.Inventory.TryGetValue(v.VariantId, out var stock);
                    var available = bundle.AvailableFor(v.VariantId);
                    return new ProductDetailVariantResponse(
                        v.VariantId, v.Sku, bundle.SizeNames.GetValueOrDefault(v.SizeId),
                        color?.Name, color?.HexCode, v.Price ?? product.BasePrice, v.Status,
                        stock?.QuantityOnHand ?? 0, stock?.QuantityReserved ?? 0,
                        available, available > 0, v.SizeId, v.ColorId);
                }).ToArray();

            SizeChartResponse? chartResponse = null;
            var chart = await catalog.LoadSizeChartForProductAsync(product);
            if (chart is { } found)
            {
                chartResponse = new SizeChartResponse(
                    found.Chart.SizeChartId, found.Chart.Name, found.Chart.Description,
                    found.Items.Select(i => new SizeChartItemResponse(
                        i.SizeLabel, i.ChestCm, i.WaistCm, i.HipCm, i.LengthCm, i.Notes,
                        i.HeightMinCm, i.HeightMaxCm, i.WeightMinKg, i.WeightMaxKg)).ToArray());
            }

            PublicRef? materialGroup = null;
            if (product.MaterialId is { } mid)
            {
                var doc = await materials.Find(Builders<BsonDocument>.Filter.Eq("material_id", mid)).FirstOrDefaultAsync();
                if (doc is not null) materialGroup = ProductListService.Ref(doc, "material_id");
            }
            var occasionRefs = Array.Empty<PublicRef>();
            if (product.OccasionIds is { Count: > 0 } ids)
            {
                var docs = await occasions.Find(Builders<BsonDocument>.Filter.In("occasion_id", ids)).ToListAsync();
                occasionRefs = [.. docs.OrderBy(d => d.GetValue("display_order", int.MaxValue).ToInt32())
                    .Select(d => ProductListService.Ref(d, "occasion_id"))];
            }

            return new ProductDetailResponse(
                product.ProductId, product.CategoryId, bundle.CategoryNames.GetValueOrDefault(product.CategoryId),
                product.Name, product.Description, product.Material, product.BasePrice, product.Status,
                bundle.ImagesByProduct.GetValueOrDefault(product.ProductId, [])
                    .Select((i, index) => PublicImages.From(i, index)).ToArray(),
                variants, variants.Sum(v => v.AvailableQuantity), chartResponse,
                product.Slug, materialGroup, occasionRefs, product.CareInstructions);
        }

        // ---- Product list and lookups -------------------------------------------------------------

        app.MapGet("/api/categories", async () => Results.Ok(await listing.CategoriesAsync()));
        app.MapGet("/api/materials", async () => Results.Ok(await listing.AttributesAsync(AttributeKind.Material)));
        app.MapGet("/api/occasions", async () => Results.Ok(await listing.AttributesAsync(AttributeKind.Occasion)));

        app.MapGet("/api/products", async (HttpContext context, string? q, string? category, double? minPrice,
            double? maxPrice, bool? inStock, string? sort, int? page, int? pageSize) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (q is { Length: > 100 }) errors["q"] = ["Search text must be at most 100 characters."];
            if (minPrice is < 0) errors["minPrice"] = ["Minimum price must be 0 or greater."];
            if (maxPrice is < 0) errors["maxPrice"] = ["Maximum price must be 0 or greater."];
            if (minPrice is not null && maxPrice is not null && minPrice > maxPrice)
                errors["maxPrice"] = ["Maximum price must not be lower than the minimum price."];
            var sortKey = string.IsNullOrWhiteSpace(sort) ? "newest" : sort.Trim();
            if (!ProductListService.Sorts.Contains(sortKey))
                errors["sort"] = [$"Sort must be one of: {string.Join(", ", ProductListService.Sorts)}."];
            var badPaging = Paging.TryGet(page, pageSize, out var p, out var ps, defaultSize: 12, maxSize: 48);
            if (badPaging is not null) return badPaging;
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            string[] Many(string key) => context.Request.Query[key].SelectMany(v => (v ?? "").Split(',')).ToArray();
            var query = new ProductListQuery(q, category, Many("size"), Many("material"), Many("occasion"),
                minPrice, maxPrice, inStock == true, sortKey, p, ps);
            return Results.Ok(await listing.ListAsync(query));
        });

        // ---- Detail ----------------------------------------------------------------------------------

        app.MapGet("/api/products/{productId:long}", async (long productId) =>
        {
            var product = await catalog.FindProductAsync(productId);
            if (product is null || product.Status != "active")
                return Results.NotFound(new { message = "Product not found." });
            return Results.Ok(await BuildDetailAsync(product));
        });

        app.MapGet("/api/products/slug/{slug}", async (string slug) =>
        {
            var product = await db.GetCollection<ProductDocument>("products")
                .Find(x => x.Slug == slug && x.Status == "active").FirstOrDefaultAsync();
            return product is null
                ? Results.NotFound(new { message = "Product not found." })
                : Results.Ok(await BuildDetailAsync(product));
        });

        app.MapGet("/api/products/{productId:long}/size-suggestion", async (long productId, double? heightCm, double? weightKg) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (heightCm is null && weightKg is null) errors["heightCm"] = ["Send heightCm, weightKg or both."];
            if (heightCm is < 50 or > 250) errors["heightCm"] = ["Height must be between 50 and 250 cm."];
            if (weightKg is < 10 or > 250) errors["weightKg"] = ["Weight must be between 10 and 250 kg."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var product = await catalog.FindProductAsync(productId);
            if (product is null || product.Status != "active")
                return Results.NotFound(new { message = "Product not found." });
            return Results.Ok(await listing.SuggestSizeAsync(product, heightCm, weightKg));
        });

        app.MapGet("/api/size-charts/{id:long}", async (long id) =>
        {
            var chart = await catalog.LoadSizeChartAsync(id);
            if (chart is not { } found) return Results.NotFound(new { message = "Size chart not found." });
            return Results.Ok(new SizeChartResponse(found.Chart.SizeChartId, found.Chart.Name, found.Chart.Description,
                found.Items.Select(i => new SizeChartItemResponse(i.SizeLabel, i.ChestCm, i.WaistCm, i.HipCm, i.LengthCm,
                    i.Notes, i.HeightMinCm, i.HeightMaxCm, i.WeightMinKg, i.WeightMaxKg)).ToArray()));
        });

        // ---- Store policies ----------------------------------------------------------------------------

        app.MapGet("/api/storefront/policies", () =>
        {
            string? Setting(string key) => string.IsNullOrWhiteSpace(configuration[$"Store:{key}"]) ? null : configuration[$"Store:{key}"];
            var hasBank = Setting("BankAccountNumber") is not null;
            return Results.Ok(new
            {
                currency = "VND",
                shippingFee = CheckoutPricing.ShippingFee,
                freeShippingThreshold = CheckoutPricing.FreeShippingThreshold,
                returnWindowDays = ReturnService.ReturnWindowDays,
                maxDeliveryAttempts = Orders.OrderStateMachine.MaxDeliveryAttempts,
                customerCancelStatuses = new[] { "pending", "confirmed" },
                paymentMethods = new object[]
                {
                    new { code = "cod", label = "Thanh toán khi nhận hàng" },
                    new { code = "bank_transfer", label = "Chuyển khoản ngân hàng" }
                },
                bankTransfer = hasBank
                    ? new
                    {
                        bankName = Setting("BankName"), accountNumber = Setting("BankAccountNumber"),
                        accountName = Setting("BankAccountName"),
                        note = "Ghi mã đơn hàng vào nội dung chuyển khoản. Shop xác nhận thanh toán thủ công."
                    }
                    : null,
                store = new { name = Setting("Name") ?? "Themis Fashion Studio", address = Setting("Address"), phone = Setting("Phone") },
                imageStandard = ImageStandard.Describe()
            });
        });
    }
}
