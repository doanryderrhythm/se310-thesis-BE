using System.Text.RegularExpressions;
using AuthApi.Common;
using AuthApi.Features.Catalog;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AuthApi.Features.Products;

/// <summary>
/// Public product listing: only active products of active categories. Product-level filters (category,
/// material, occasion, text) run in Mongo; size, price and "in stock" must hold on the same active variant,
/// so they are checked in memory over the candidates (fine for the catalog size of this shop).
/// </summary>
internal sealed class ProductListService(IMongoDatabase db)
{
    public static readonly string[] Sorts = ["newest", "price_asc", "price_desc", "name_asc"];

    private readonly IMongoCollection<ProductDocument> products = db.GetCollection<ProductDocument>("products");
    private readonly IMongoCollection<CategoryDocument> categories = db.GetCollection<CategoryDocument>("categories");
    private readonly IMongoCollection<SizeDocument> sizes = db.GetCollection<SizeDocument>("sizes");
    private readonly IMongoCollection<BsonDocument> materials = db.GetCollection<BsonDocument>("materials");
    private readonly IMongoCollection<BsonDocument> occasions = db.GetCollection<BsonDocument>("occasions");
    private readonly ProductCatalogService catalog = new(db);

    // ---- Lookups -------------------------------------------------------------------------------

    public async Task<PublicCategory[]> CategoriesAsync()
    {
        var list = await categories.Find(c => c.Status == null || c.Status == "active").ToListAsync();
        var counts = (await products.Aggregate().Match(p => p.Status == "active")
            .Group<BsonDocument>(new BsonDocument { { "_id", "$category_id" }, { "n", new BsonDocument("$sum", 1) } }).ToListAsync())
            .ToDictionary(r => r["_id"].ToInt64(), r => r["n"].ToInt64());
        return list.OrderBy(c => c.DisplayOrder ?? int.MaxValue).ThenBy(c => c.CategoryId)
            .Select(c => new PublicCategory(c.CategoryId, c.Name, c.Slug, c.Description,
                c.DisplayOrder ?? 0, counts.GetValueOrDefault(c.CategoryId))).ToArray();
    }

    public async Task<PublicAttribute[]> AttributesAsync(AttributeKind kind)
    {
        var rows = await db.GetCollection<BsonDocument>(kind.Collection)
            .Find(Builders<BsonDocument>.Filter.Eq("status", "active")).ToListAsync();
        var activeProducts = products.Aggregate().Match(p => p.Status == "active");
        var grouped = kind.ProductField == "occasion_ids"
            ? await activeProducts.Unwind(kind.ProductField)
                .Group(new BsonDocument { { "_id", $"${kind.ProductField}" }, { "n", new BsonDocument("$sum", 1) } }).ToListAsync()
            : await activeProducts
                .Group<BsonDocument>(new BsonDocument { { "_id", $"${kind.ProductField}" }, { "n", new BsonDocument("$sum", 1) } }).ToListAsync();
        var counts = grouped.Where(r => !r["_id"].IsBsonNull).ToDictionary(r => r["_id"].ToInt64(), r => r["n"].ToInt64());
        return rows.OrderBy(d => d.GetValue("display_order", int.MaxValue).ToInt32()).ThenBy(d => d[kind.IdField].ToInt64())
            .Select(d => new PublicAttribute(d[kind.IdField].ToInt64(), d["name"].AsString, d.GetValue("slug", "").AsString,
                d.GetValue("description", BsonNull.Value).IsBsonNull ? null : d["description"].AsString,
                d.GetValue("display_order", 0).ToInt32(), counts.GetValueOrDefault(d[kind.IdField].ToInt64()))).ToArray();
    }

    /// <summary>Turns ids or slugs from the query string into ids; unknown values are dropped.</summary>
    private static List<long> ResolveIds(IEnumerable<string> tokens, IEnumerable<BsonDocument> rows, string idField)
    {
        var list = rows.ToList();
        var result = new List<long>();
        foreach (var raw in tokens.Select(t => t.Trim()).Where(t => t.Length > 0))
        {
            if (long.TryParse(raw, out var id)) { result.Add(id); continue; }
            var match = list.FirstOrDefault(d => string.Equals(d.GetValue("slug", "").AsString, raw, StringComparison.OrdinalIgnoreCase));
            if (match is not null) result.Add(match[idField].ToInt64());
        }
        return result;
    }

    // ---- Listing -------------------------------------------------------------------------------

    public async Task<PageResponse<ProductListItem>> ListAsync(ProductListQuery q)
    {
        var activeCategories = (await categories.Find(c => c.Status == null || c.Status == "active").ToListAsync());
        var allowedCategories = activeCategories.Select(c => c.CategoryId).ToList();
        if (!string.IsNullOrWhiteSpace(q.Category))
        {
            var token = q.Category.Trim();
            var match = long.TryParse(token, out var id)
                ? activeCategories.FirstOrDefault(c => c.CategoryId == id)
                : activeCategories.FirstOrDefault(c => string.Equals(c.Slug, token, StringComparison.OrdinalIgnoreCase));
            allowedCategories = match is null ? [] : [match.CategoryId];
        }

        var b = Builders<ProductDocument>.Filter;
        var filter = b.Eq(p => p.Status, "active") & b.In(p => p.CategoryId, allowedCategories);

        if (q.Materials.Length > 0)
        {
            var ids = ResolveIds(q.Materials, await materials.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync(), "material_id");
            filter &= b.In(p => p.MaterialId, ids.Select(i => (long?)i));
        }
        if (q.Occasions.Length > 0)
        {
            var ids = ResolveIds(q.Occasions, await occasions.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync(), "occasion_id");
            filter &= b.AnyIn(p => p.OccasionIds, ids);
        }
        foreach (var word in Text.Fold(q.Q ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            filter &= b.Regex(p => p.SearchText, new BsonRegularExpression(Regex.Escape(word)));

        var candidates = await products.Find(filter).ToListAsync();
        var bundle = await catalog.LoadForProductsAsync(candidates);

        var wantedSizes = q.Sizes.Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        var sizeIdSet = new HashSet<long>(wantedSizes.Where(s => long.TryParse(s, out _)).Select(long.Parse));
        var sizeNameSet = wantedSizes.Where(s => !long.TryParse(s, out _)).Select(s => s.ToUpperInvariant()).ToHashSet();

        var matches = new List<(ProductDocument Product, List<ProductVariantDocument> Active)>();
        foreach (var product in candidates)
        {
            var active = bundle.VariantsByProduct.GetValueOrDefault(product.ProductId, []).Where(v => v.Status == "active").ToList();
            var eligible = active.Any(v =>
            {
                if (wantedSizes.Count > 0 && !sizeIdSet.Contains(v.SizeId)
                    && !sizeNameSet.Contains(bundle.SizeNames.GetValueOrDefault(v.SizeId, "").ToUpperInvariant())) return false;
                var price = bundle.PriceFor(v) ?? 0;
                if (q.MinPrice is { } min && price < min) return false;
                if (q.MaxPrice is { } max && price > max) return false;
                return !q.InStock || bundle.AvailableFor(v.VariantId) > 0;
            });
            if (eligible) matches.Add((product, active));
        }

        double Lowest((ProductDocument Product, List<ProductVariantDocument> Active) m) =>
            m.Active.Select(v => bundle.PriceFor(v) ?? double.MaxValue).DefaultIfEmpty(m.Product.BasePrice ?? double.MaxValue).Min();

        IEnumerable<(ProductDocument Product, List<ProductVariantDocument> Active)> sorted = q.Sort switch
        {
            "price_asc" => matches.OrderBy(Lowest).ThenByDescending(m => m.Product.ProductId),
            "price_desc" => matches.OrderByDescending(Lowest).ThenByDescending(m => m.Product.ProductId),
            "name_asc" => matches.OrderBy(m => Text.Fold(m.Product.Name), StringComparer.Ordinal).ThenBy(m => m.Product.ProductId),
            _ => matches.OrderByDescending(m => m.Product.CreatedAt).ThenByDescending(m => m.Product.ProductId)
        };

        var total = matches.Count;
        var pageItems = sorted.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToList();

        var materialDocs = (await materials.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync())
            .ToDictionary(d => d["material_id"].ToInt64());
        var occasionDocs = (await occasions.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync())
            .ToDictionary(d => d["occasion_id"].ToInt64());
        var sizeOrder = (await sizes.Find(Builders<SizeDocument>.Filter.Empty).ToListAsync())
            .ToDictionary(s => s.SizeId, s => s.DisplayOrder ?? (int)s.SizeId);

        var items = pageItems.Select(m =>
        {
            var p = m.Product;
            var prices = m.Active.Select(v => bundle.PriceFor(v)).Where(x => x is not null).Select(x => x!.Value).ToArray();
            var images = bundle.ImagesByProduct.GetValueOrDefault(p.ProductId, []);
            return new ProductListItem(
                p.ProductId, p.Slug, p.Name, p.CategoryId, bundle.CategoryNames.GetValueOrDefault(p.CategoryId),
                prices.Length > 0 ? prices.Min() : p.BasePrice, prices.Length > 0 ? prices.Max() : p.BasePrice,
                images.Length > 0 ? PublicImages.From(images[0], 0) : null,
                images.Length > 1 ? PublicImages.From(images[1], 1) : null,
                m.Active.Select(v => v.ColorId).Distinct().Where(bundle.Colors.ContainsKey)
                    .Select(id => new PublicColor(id, bundle.Colors[id].Name, bundle.Colors[id].HexCode)).ToArray(),
                m.Active.Select(v => v.SizeId).Distinct().OrderBy(id => sizeOrder.GetValueOrDefault(id, int.MaxValue))
                    .Select(id => bundle.SizeNames.GetValueOrDefault(id, "")).Where(n => n.Length > 0).ToArray(),
                m.Active.Any(v => bundle.AvailableFor(v.VariantId) > 0), p.Material,
                p.MaterialId is { } mid && materialDocs.TryGetValue(mid, out var material) ? Ref(material, "material_id") : null,
                (p.OccasionIds ?? []).Where(occasionDocs.ContainsKey).Select(id => Ref(occasionDocs[id], "occasion_id")).ToArray(),
                p.CreatedAt);
        }).ToArray();

        return new PageResponse<ProductListItem>(items, q.Page, q.PageSize, total);
    }

    public static PublicRef Ref(BsonDocument d, string idField) =>
        new(d[idField].ToInt64(), d["name"].AsString, d.GetValue("slug", "").AsString);

    // ---- Size suggestion ---------------------------------------------------------------------------

    public async Task<SizeSuggestionResponse> SuggestSizeAsync(ProductDocument product, double? heightCm, double? weightKg)
    {
        var chart = await catalog.LoadSizeChartForProductAsync(product);
        var bundle = await catalog.LoadForProductsAsync([product]);
        var active = bundle.VariantsByProduct.GetValueOrDefault(product.ProductId, []).Where(v => v.Status == "active").ToList();
        var sold = active.Select(v => bundle.SizeNames.GetValueOrDefault(v.SizeId, "").ToUpperInvariant()).ToHashSet();
        var inStock = active.Where(v => bundle.AvailableFor(v.VariantId) > 0)
            .Select(v => bundle.SizeNames.GetValueOrDefault(v.SizeId, "").ToUpperInvariant()).ToHashSet();

        static double Gap(double value, double? min, double? max)
        {
            if (min is not null && value < min) return min.Value - value;
            if (max is not null && value > max) return value - max.Value;
            return 0;
        }

        var rows = (chart?.Items ?? []).Where(i => i.SizeLabel is not null).ToList();
        var usable = rows.Where(i => sold.Contains(i.SizeLabel!.ToUpperInvariant())).ToList();
        if (usable.Count == 0) usable = rows;

        var scored = new List<(SizeChartItemDocument Row, double Distance)>();
        foreach (var row in usable)
        {
            var hasHeight = row.HeightMinCm is not null || row.HeightMaxCm is not null;
            var hasWeight = row.WeightMinKg is not null || row.WeightMaxKg is not null;
            var asked = (heightCm is not null && hasHeight) || (weightKg is not null && hasWeight);
            if (!asked) continue; // this row has no range for what was asked
            var distance = 0.0;
            if (heightCm is { } h && hasHeight) distance += Gap(h, row.HeightMinCm, row.HeightMaxCm);
            if (weightKg is { } w && hasWeight) distance += Gap(w, row.WeightMinKg, row.WeightMaxKg);
            scored.Add((row, distance));
        }

        if (scored.Count == 0) return new SizeSuggestionResponse(null, "none", false, []);
        var ordered = scored.OrderBy(x => x.Distance).ThenByDescending(x => x.Row.DisplayOrder ?? 0).ThenByDescending(x => x.Row.SizeChartItemId).ToList();
        var best = ordered[0];
        return new SizeSuggestionResponse(best.Row.SizeLabel, best.Distance == 0 ? "exact" : "closest",
            inStock.Contains(best.Row.SizeLabel!.ToUpperInvariant()),
            ordered.Take(3).Select(x => new SizeSuggestionCandidate(x.Row.SizeLabel!, Math.Round(x.Distance, 1))).ToArray());
    }
}

internal static class PublicImages
{
    public static PublicImage From(ProductImageDocument i, int index) => new(
        i.ImageId, i.ImageUrl, i.AltText, i.ColorId, i.SortOrder ?? index, i.IsPrimary ?? index == 0,
        i.Width, i.Height, i.FocalX, i.FocalY, MediaEndpoints.Srcset(i.MediaFile, i.Widths));
}
