using AuthApi.Data;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AuthApi.Features.Products;

[BsonIgnoreExtraElements]
internal sealed class SizeChartDocument
{
    [BsonElement("size_chart_id")] public long SizeChartId { get; set; }
    [BsonElement("name")] public string? Name { get; set; }
    [BsonElement("description")] public string? Description { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class SizeChartItemDocument
{
    [BsonElement("size_chart_item_id")] public long SizeChartItemId { get; set; }
    [BsonElement("size_chart_id")] public long SizeChartId { get; set; }
    [BsonElement("size_label")] public string? SizeLabel { get; set; }
    [BsonElement("chest_cm")] public double? ChestCm { get; set; }
    [BsonElement("waist_cm")] public double? WaistCm { get; set; }
    [BsonElement("hip_cm")] public double? HipCm { get; set; }
    [BsonElement("length_cm")] public double? LengthCm { get; set; }
    [BsonElement("notes")] public string? Notes { get; set; }
}

internal sealed class CatalogBundle
{
    public Dictionary<long, ProductDocument> Products { get; init; } = [];
    public Dictionary<long, string> CategoryNames { get; init; } = [];
    public Dictionary<long, ProductImageDocument[]> ImagesByProduct { get; init; } = [];
    public Dictionary<long, ProductVariantDocument[]> VariantsByProduct { get; init; } = [];
    public Dictionary<long, ProductVariantDocument> VariantsById { get; init; } = [];
    public Dictionary<long, string> SizeNames { get; init; } = [];
    public Dictionary<long, ColorDocument> Colors { get; init; } = [];
    public Dictionary<long, InventoryDocument> Inventory { get; init; } = [];

    public int AvailableFor(long variantId) =>
        InventoryMath.Available(Inventory.GetValueOrDefault(variantId));

    public double? PriceFor(ProductVariantDocument variant) =>
        variant.Price ?? Products.GetValueOrDefault(variant.ProductId)?.BasePrice;
}

internal sealed class ProductCatalogService(IMongoDatabase db)
{
    private readonly IMongoCollection<ProductDocument> products = db.GetCollection<ProductDocument>("products");
    private readonly IMongoCollection<CategoryDocument> categories = db.GetCollection<CategoryDocument>("categories");
    private readonly IMongoCollection<ProductImageDocument> images = db.GetCollection<ProductImageDocument>("product_images");
    private readonly IMongoCollection<ProductVariantDocument> variants = db.GetCollection<ProductVariantDocument>("product_variants");
    private readonly IMongoCollection<SizeDocument> sizes = db.GetCollection<SizeDocument>("sizes");
    private readonly IMongoCollection<ColorDocument> colors = db.GetCollection<ColorDocument>("colors");
    private readonly IMongoCollection<InventoryDocument> inventory = db.GetCollection<InventoryDocument>("inventory");
    private readonly IMongoCollection<SizeChartDocument> sizeCharts = db.GetCollection<SizeChartDocument>("size_charts");
    private readonly IMongoCollection<SizeChartItemDocument> sizeChartItems = db.GetCollection<SizeChartItemDocument>("size_chart_items");

    public async Task<CatalogBundle> LoadForProductsAsync(IReadOnlyList<ProductDocument> productList)
    {
        var productIds = productList.Select(p => p.ProductId).ToArray();
        var productVariants = await variants
            .Find(Builders<ProductVariantDocument>.Filter.In(v => v.ProductId, productIds))
            .SortBy(v => v.VariantId).ToListAsync();
        return await BuildAsync(productList, productVariants);
    }

    public async Task<CatalogBundle> LoadForVariantsAsync(IReadOnlyList<long> variantIds)
    {
        if (variantIds.Count == 0) return new CatalogBundle();
        var found = await variants
            .Find(Builders<ProductVariantDocument>.Filter.In(v => v.VariantId, variantIds))
            .ToListAsync();
        var productIds = found.Select(v => v.ProductId).Distinct().ToArray();
        var productList = await products
            .Find(Builders<ProductDocument>.Filter.In(p => p.ProductId, productIds)).ToListAsync();
        return await BuildAsync(productList, found);
    }

    public async Task<ProductDocument?> FindProductAsync(long productId) =>
        await products.Find(p => p.ProductId == productId).FirstOrDefaultAsync();

    public async Task<(SizeChartDocument Chart, SizeChartItemDocument[] Items)?> LoadSizeChartAsync(long? sizeChartId)
    {
        if (sizeChartId is null) return null;
        var chart = await sizeCharts.Find(c => c.SizeChartId == sizeChartId).FirstOrDefaultAsync();
        if (chart is null) return null;
        var items = await sizeChartItems.Find(i => i.SizeChartId == sizeChartId)
            .SortBy(i => i.SizeChartItemId).ToListAsync();
        return (chart, items.ToArray());
    }

    private async Task<CatalogBundle> BuildAsync(
        IReadOnlyList<ProductDocument> productList, IReadOnlyList<ProductVariantDocument> variantList)
    {
        var productIds = productList.Select(p => p.ProductId).ToArray();
        var categoryIds = productList.Select(p => p.CategoryId).Distinct().ToArray();
        var variantIds = variantList.Select(v => v.VariantId).ToArray();
        var sizeIds = variantList.Select(v => v.SizeId).Distinct().ToArray();
        var colorIds = variantList.Select(v => v.ColorId).Distinct().ToArray();

        var imageList = await images
            .Find(Builders<ProductImageDocument>.Filter.In(i => i.ProductId, productIds))
            .SortBy(i => i.ImageId).ToListAsync();
        var categoryList = await categories
            .Find(Builders<CategoryDocument>.Filter.In(c => c.CategoryId, categoryIds)).ToListAsync();
        var sizeList = await sizes
            .Find(Builders<SizeDocument>.Filter.In(s => s.SizeId, sizeIds)).ToListAsync();
        var colorList = await colors
            .Find(Builders<ColorDocument>.Filter.In(c => c.ColorId, colorIds)).ToListAsync();
        var stockList = await inventory
            .Find(Builders<InventoryDocument>.Filter.In(i => i.VariantId, variantIds)).ToListAsync();

        return new CatalogBundle
        {
            Products = productList.ToDictionary(p => p.ProductId),
            CategoryNames = categoryList.ToDictionary(c => c.CategoryId, c => c.Name),
            ImagesByProduct = imageList.GroupBy(i => i.ProductId).ToDictionary(g => g.Key, g => g.ToArray()),
            VariantsByProduct = variantList.GroupBy(v => v.ProductId).ToDictionary(g => g.Key, g => g.ToArray()),
            VariantsById = variantList.ToDictionary(v => v.VariantId),
            SizeNames = sizeList.ToDictionary(s => s.SizeId, s => s.Name),
            Colors = colorList.ToDictionary(c => c.ColorId),
            Inventory = stockList.GroupBy(i => i.VariantId).ToDictionary(g => g.Key, g => g.First())
        };
    }
}
