using AuthApi.Common;
using AuthApi.Data;
using AuthApi.Features.Products;
using MongoDB.Driver;

namespace AuthApi.Features.Catalog;

internal static class CatalogSetup
{
    public static async Task SetupAsync(IMongoDatabase db, CounterService counters)
    {
        var products = db.GetCollection<ProductDocument>("products");
        var categories = db.GetCollection<CategoryDocument>("categories");
        var variants = db.GetCollection<ProductVariantDocument>("product_variants");

        await BackfillProductSlugsAsync(products);
        await BackfillSearchTextAsync(products);
        // Categories created before display_order existed keep their id order.
        // Only rows that have no display_order at all; an admin-chosen 0 is kept.
        var missingOrder = Builders<CategoryDocument>.Filter.Exists("display_order", false);
        foreach (var category in await categories.Find(missingOrder).ToListAsync())
            await categories.UpdateOneAsync(c => c.CategoryId == category.CategoryId,
                Builders<CategoryDocument>.Update.Set(c => c.DisplayOrder, (int)category.CategoryId));

        // Slug uniqueness only applies to documents that have a slug (older rows may not).
        var hasSlug = new MongoDB.Bson.BsonDocument("slug", new MongoDB.Bson.BsonDocument("$type", "string"));
        await products.Indexes.CreateOneAsync(new CreateIndexModel<ProductDocument>(
            Builders<ProductDocument>.IndexKeys.Ascending(p => p.Slug),
            new CreateIndexOptions<ProductDocument>
            {
                Unique = true, Name = "slug_partial_unique",
                PartialFilterExpression = hasSlug
            }));
        await categories.Indexes.CreateOneAsync(new CreateIndexModel<CategoryDocument>(
            Builders<CategoryDocument>.IndexKeys.Ascending(c => c.Slug),
            new CreateIndexOptions<CategoryDocument>
            {
                Unique = true, Name = "slug_partial_unique",
                PartialFilterExpression = hasSlug
            }));
        // Same names as mongo-init/01-schema.js (`uniqueIndexes`) so both creators agree.
        await variants.Indexes.CreateOneAsync(new CreateIndexModel<ProductVariantDocument>(
            Builders<ProductVariantDocument>.IndexKeys.Ascending(v => v.Sku),
            new CreateIndexOptions { Unique = true, Name = "sku_unique" }));
        await variants.Indexes.CreateOneAsync(new CreateIndexModel<ProductVariantDocument>(
            Builders<ProductVariantDocument>.IndexKeys.Ascending(v => v.ProductId).Ascending(v => v.SizeId).Ascending(v => v.ColorId),
            new CreateIndexOptions { Unique = true, Name = "product_size_color_unique" }));

        foreach (var (counter, collection, field) in new[]
        {
            ("products", "products", "product_id"), ("categories", "categories", "category_id"),
            ("sizes", "sizes", "size_id"), ("colors", "colors", "color_id"),
            ("product_images", "product_images", "image_id"), ("product_variants", "product_variants", "variant_id"),
            ("size_charts", "size_charts", "size_chart_id"), ("size_chart_items", "size_chart_items", "size_chart_item_id")
        })
        {
            await IdCounters.SyncAsync(db, counters, counter, collection, field);
        }
    }

    /// <summary>Products created before the public search existed get their accent-free search text.</summary>
    private static async Task BackfillSearchTextAsync(IMongoCollection<ProductDocument> products)
    {
        var missing = await products.Find(Builders<ProductDocument>.Filter.Exists("search_text", false)).ToListAsync();
        foreach (var product in missing)
            await products.UpdateOneAsync(p => p.ProductId == product.ProductId,
                Builders<ProductDocument>.Update.Set(p => p.SearchText, AdminProductService.SearchTextOf(product.Name, product.Description)));
    }

    private static async Task BackfillProductSlugsAsync(IMongoCollection<ProductDocument> products)
    {
        var all = await products.Find(Builders<ProductDocument>.Filter.Empty).ToListAsync();
        var used = all.Where(p => !string.IsNullOrEmpty(p.Slug)).Select(p => p.Slug!).ToHashSet();
        foreach (var product in all.Where(p => string.IsNullOrEmpty(p.Slug)))
        {
            var slug = Slugs.From(product.Name);
            if (slug.Length == 0 || used.Contains(slug)) slug = $"{(slug.Length == 0 ? "san-pham" : slug)}-{product.ProductId}";
            used.Add(slug);
            await products.UpdateOneAsync(p => p.ProductId == product.ProductId,
                Builders<ProductDocument>.Update.Set(p => p.Slug, slug));
        }
    }
}
