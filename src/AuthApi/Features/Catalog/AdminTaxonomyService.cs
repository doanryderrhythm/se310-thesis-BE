using System.Text.RegularExpressions;
using AuthApi.Common;
using AuthApi.Data;
using AuthApi.Features.Products;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AuthApi.Features.Catalog;

/// <summary>Categories, sizes, colors and size charts.</summary>
internal sealed class AdminTaxonomyService(IMongoDatabase db, CounterService counters)
{
    private readonly IMongoCollection<CategoryDocument> categories = db.GetCollection<CategoryDocument>("categories");
    private readonly IMongoCollection<ProductDocument> products = db.GetCollection<ProductDocument>("products");
    private readonly IMongoCollection<SizeDocument> sizes = db.GetCollection<SizeDocument>("sizes");
    private readonly IMongoCollection<ColorDocument> colors = db.GetCollection<ColorDocument>("colors");
    private readonly IMongoCollection<ProductVariantDocument> variants = db.GetCollection<ProductVariantDocument>("product_variants");
    private readonly IMongoCollection<SizeChartDocument> charts = db.GetCollection<SizeChartDocument>("size_charts");
    private readonly IMongoCollection<SizeChartItemDocument> chartItems = db.GetCollection<SizeChartItemDocument>("size_chart_items");

    private static readonly Regex HexColor = new(@"\A#[0-9A-Fa-f]{6}\z", RegexOptions.Compiled);

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    // ---- Categories ------------------------------------------------------------------

    public async Task<AdminCategoryResponse[]> ListCategoriesAsync(string? q, string? status)
    {
        var b = Builders<CategoryDocument>.Filter;
        var filter = b.Empty;
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = new BsonRegularExpression(Regex.Escape(q.Trim()), "i");
            filter &= b.Regex(c => c.Name, pattern) | b.Regex(c => c.Slug, pattern);
        }
        if (!string.IsNullOrWhiteSpace(status)) filter &= b.Eq(c => c.Status, status);
        var list = await categories.Find(filter).ToListAsync();
        var counts = await ProductCountsAsync();
        return list.OrderBy(c => c.DisplayOrder ?? int.MaxValue).ThenBy(c => c.CategoryId)
            .Select(c => ToResponse(c, counts.GetValueOrDefault(c.CategoryId))).ToArray();
    }

    public async Task<AdminCategoryResponse?> GetCategoryAsync(long id)
    {
        var category = await categories.Find(c => c.CategoryId == id).FirstOrDefaultAsync();
        if (category is null) return null;
        return ToResponse(category, await products.CountDocumentsAsync(p => p.CategoryId == id));
    }

    public async Task<(AdminCategoryResponse? Category, IResult? Error)> CreateCategoryAsync(CategoryRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        var name = r.Name?.Trim();
        if (name is null || name.Length is < 2 or > 150) errors["name"] = ["Name must be between 2 and 150 characters."];
        var slug = string.IsNullOrWhiteSpace(r.Slug) ? (name is null ? "" : Slugs.From(name)) : r.Slug.Trim();
        if (!Slugs.IsValid(slug)) errors["slug"] = ["Slug must be lowercase letters, digits and hyphens (at most 180 characters)."];
        if (r.Description is { Length: > 1000 }) errors["description"] = ["Description must be at most 1000 characters."];
        if (r.DisplayOrder is < 0) errors["displayOrder"] = ["Display order must be 0 or greater."];
        var status = r.Status ?? "active";
        if (status is not ("active" or "hidden")) errors["status"] = ["Status must be 'active' or 'hidden'."];
        if (errors.Count > 0) return (null, Results.ValidationProblem(errors));
        if (await categories.Find(c => c.Slug == slug).AnyAsync()) return (null, Invalid("slug", "Slug already exists."));

        var highest = (await categories.Find(Builders<CategoryDocument>.Filter.Empty).ToListAsync())
            .Select(c => c.DisplayOrder ?? 0).DefaultIfEmpty(0).Max();
        var now = DateTime.UtcNow;
        var category = new CategoryDocument
        {
            CategoryId = await counters.NextAsync("categories"), Name = name!, Slug = slug,
            Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim(),
            DisplayOrder = r.DisplayOrder ?? highest + 1, Status = status, CreatedAt = now, UpdatedAt = now
        };
        try
        {
            await categories.InsertOneAsync(category);
        }
        catch (MongoWriteException e) when (e.WriteError?.Code == 11000)
        {
            return (null, Invalid("slug", "Slug already exists."));
        }

        return (ToResponse(category, 0), null);
    }

    public async Task<(AdminCategoryResponse? Category, IResult? Error)> UpdateCategoryAsync(long id, CategoryRequest r)
    {
        var existing = await categories.Find(c => c.CategoryId == id).FirstOrDefaultAsync();
        if (existing is null) return (null, Results.NotFound());

        var errors = new Dictionary<string, string[]>();
        var u = Builders<CategoryDocument>.Update.Set(c => c.UpdatedAt, DateTime.UtcNow);
        if (r.Name is not null)
        {
            var name = r.Name.Trim();
            if (name.Length is < 2 or > 150) errors["name"] = ["Name must be between 2 and 150 characters."];
            else u = u.Set(c => c.Name, name);
        }
        if (r.Slug is not null)
        {
            var slug = r.Slug.Trim();
            if (!Slugs.IsValid(slug)) errors["slug"] = ["Slug must be lowercase letters, digits and hyphens (at most 180 characters)."];
            else if (await categories.Find(c => c.Slug == slug && c.CategoryId != id).AnyAsync()) errors["slug"] = ["Slug already exists."];
            else u = u.Set(c => c.Slug, slug);
        }
        if (r.Description is not null)
        {
            if (r.Description.Length > 1000) errors["description"] = ["Description must be at most 1000 characters."];
            else u = u.Set(c => c.Description, string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim());
        }
        if (r.DisplayOrder is { } order)
        {
            if (order < 0) errors["displayOrder"] = ["Display order must be 0 or greater."];
            else u = u.Set(c => c.DisplayOrder, order);
        }
        if (r.Status is not null)
        {
            if (r.Status is not ("active" or "hidden")) errors["status"] = ["Status must be 'active' or 'hidden'."];
            else u = u.Set(c => c.Status, r.Status);
        }
        if (errors.Count > 0) return (null, Results.ValidationProblem(errors));

        try
        {
            await categories.UpdateOneAsync(c => c.CategoryId == id, u);
        }
        catch (MongoWriteException e) when (e.WriteError?.Code == 11000)
        {
            return (null, Invalid("slug", "Slug already exists."));
        }

        return (await GetCategoryAsync(id), null);
    }

    public async Task<IResult> DeleteCategoryAsync(long id)
    {
        if (!await categories.Find(c => c.CategoryId == id).AnyAsync()) return Results.NotFound();
        if (await products.Find(p => p.CategoryId == id).AnyAsync() || await charts.Find(c => c.CategoryId == id).AnyAsync())
            return Results.Conflict(new { message = "The category still has products or size charts; hide it instead." });
        await categories.DeleteOneAsync(c => c.CategoryId == id);
        return Results.NoContent();
    }

    private async Task<Dictionary<long, long>> ProductCountsAsync()
    {
        var rows = await products.Aggregate()
            .Group<BsonDocument>(new BsonDocument { { "_id", "$category_id" }, { "n", new BsonDocument("$sum", 1) } })
            .ToListAsync();
        return rows.ToDictionary(r => r["_id"].ToInt64(), r => r["n"].ToInt64());
    }

    private static AdminCategoryResponse ToResponse(CategoryDocument c, long productCount) =>
        new(c.CategoryId, c.Name, c.Slug, c.Description, c.DisplayOrder ?? 0, c.Status ?? "active",
            productCount, c.CreatedAt, c.UpdatedAt);

    // ---- Sizes ----------------------------------------------------------------------------

    public async Task<AdminSizeResponse[]> ListSizesAsync()
    {
        var list = await sizes.Find(Builders<SizeDocument>.Filter.Empty).ToListAsync();
        var counts = await VariantCountsAsync("$size_id");
        return list.OrderBy(s => s.DisplayOrder ?? (int)s.SizeId).ThenBy(s => s.SizeId)
            .Select(s => new AdminSizeResponse(s.SizeId, s.Name, s.Status ?? "active", counts.GetValueOrDefault(s.SizeId),
                s.DisplayOrder ?? (int)s.SizeId)).ToArray();
    }

    public async Task<(AdminSizeResponse? Size, IResult? Error)> CreateSizeAsync(SizeRequest r)
    {
        var name = r.Name?.Trim();
        var status = r.Status ?? "active";
        if (name is null || name.Length is < 1 or > 20) return (null, Invalid("name", "Name must be between 1 and 20 characters."));
        if (status is not ("active" or "hidden")) return (null, Invalid("status", "Status must be 'active' or 'hidden'."));
        if (await SizeNameTakenAsync(name, null)) return (null, Invalid("name", "A size with this name already exists."));
        if (r.DisplayOrder is < 0 or > 10_000) return (null, Invalid("displayOrder", "Display order must be between 0 and 10000."));
        var highest = (await sizes.Find(Builders<SizeDocument>.Filter.Empty).ToListAsync())
            .Select(x => x.DisplayOrder ?? (int)x.SizeId).DefaultIfEmpty(0).Max();
        var size = new SizeDocument
        {
            SizeId = await counters.NextAsync("sizes"), Name = name, Status = status, DisplayOrder = r.DisplayOrder ?? highest + 1
        };
        await sizes.InsertOneAsync(size);
        return (new AdminSizeResponse(size.SizeId, name, status, 0, size.DisplayOrder!.Value), null);
    }

    public async Task<(AdminSizeResponse? Size, IResult? Error)> UpdateSizeAsync(long id, SizeRequest r)
    {
        var existing = await sizes.Find(s => s.SizeId == id).FirstOrDefaultAsync();
        if (existing is null) return (null, Results.NotFound());
        var u = Builders<SizeDocument>.Update;
        var updates = new List<UpdateDefinition<SizeDocument>>();
        if (r.Name is not null)
        {
            var name = r.Name.Trim();
            if (name.Length is < 1 or > 20) return (null, Invalid("name", "Name must be between 1 and 20 characters."));
            if (await SizeNameTakenAsync(name, id)) return (null, Invalid("name", "A size with this name already exists."));
            updates.Add(u.Set(s => s.Name, name));
        }
        if (r.Status is not null)
        {
            if (r.Status is not ("active" or "hidden")) return (null, Invalid("status", "Status must be 'active' or 'hidden'."));
            updates.Add(u.Set(s => s.Status, r.Status));
        }
        if (r.DisplayOrder is { } order)
        {
            if (order is < 0 or > 10_000) return (null, Invalid("displayOrder", "Display order must be between 0 and 10000."));
            updates.Add(u.Set(s => s.DisplayOrder, order));
        }
        if (updates.Count > 0) await sizes.UpdateOneAsync(s => s.SizeId == id, u.Combine(updates));
        var after = await sizes.Find(s => s.SizeId == id).FirstAsync();
        return (new AdminSizeResponse(after.SizeId, after.Name, after.Status ?? "active",
            await variants.CountDocumentsAsync(v => v.SizeId == id), after.DisplayOrder ?? (int)after.SizeId), null);
    }

    public async Task<IResult> DeleteSizeAsync(long id)
    {
        if (!await sizes.Find(s => s.SizeId == id).AnyAsync()) return Results.NotFound();
        if (await variants.Find(v => v.SizeId == id).AnyAsync())
            return Results.Conflict(new { message = "Variants still use this size; hide it instead." });
        await sizes.DeleteOneAsync(s => s.SizeId == id);
        return Results.NoContent();
    }

    public async Task<IResult> DeleteColorAsync(long id)
    {
        if (!await colors.Find(c => c.ColorId == id).AnyAsync()) return Results.NotFound();
        if (await variants.Find(v => v.ColorId == id).AnyAsync()
            || await db.GetCollection<ProductImageDocument>("product_images").Find(i => i.ColorId == id).AnyAsync())
            return Results.Conflict(new { message = "Variants or images still use this color; hide it instead." });
        await colors.DeleteOneAsync(c => c.ColorId == id);
        return Results.NoContent();
    }

    private async Task<bool> SizeNameTakenAsync(string name, long? exceptId) =>
        await sizes.Find(s => s.Name.ToLower() == name.ToLower() && s.SizeId != (exceptId ?? 0)).AnyAsync();

    // ---- Colors ---------------------------------------------------------------------------------

    public async Task<AdminColorResponse[]> ListColorsAsync()
    {
        var list = await colors.Find(Builders<ColorDocument>.Filter.Empty).SortBy(c => c.ColorId).ToListAsync();
        var counts = await VariantCountsAsync("$color_id");
        return list.Select(c => new AdminColorResponse(c.ColorId, c.Name, c.HexCode, c.Status ?? "active", counts.GetValueOrDefault(c.ColorId))).ToArray();
    }

    public async Task<(AdminColorResponse? Color, IResult? Error)> CreateColorAsync(ColorRequest r)
    {
        var name = r.Name?.Trim();
        var status = r.Status ?? "active";
        var errors = new Dictionary<string, string[]>();
        if (name is null || name.Length is < 1 or > 50) errors["name"] = ["Name must be between 1 and 50 characters."];
        if (r.HexCode is null || !HexColor.IsMatch(r.HexCode)) errors["hexCode"] = ["Hex code must look like #1A2B3C."];
        if (status is not ("active" or "hidden")) errors["status"] = ["Status must be 'active' or 'hidden'."];
        if (errors.Count > 0) return (null, Results.ValidationProblem(errors));
        var hex = r.HexCode!.ToUpperInvariant();
        if (await colors.Find(c => c.Name.ToLower() == name!.ToLower() && c.HexCode == hex).AnyAsync())
            return (null, Invalid("name", "This color already exists."));
        var color = new ColorDocument { ColorId = await counters.NextAsync("colors"), Name = name!, HexCode = hex, Status = status };
        await colors.InsertOneAsync(color);
        return (new AdminColorResponse(color.ColorId, name!, hex, status, 0), null);
    }

    public async Task<(AdminColorResponse? Color, IResult? Error)> UpdateColorAsync(long id, ColorRequest r)
    {
        var existing = await colors.Find(c => c.ColorId == id).FirstOrDefaultAsync();
        if (existing is null) return (null, Results.NotFound());
        var u = Builders<ColorDocument>.Update;
        var updates = new List<UpdateDefinition<ColorDocument>>();
        var errors = new Dictionary<string, string[]>();
        if (r.Name is not null)
        {
            var name = r.Name.Trim();
            if (name.Length is < 1 or > 50) errors["name"] = ["Name must be between 1 and 50 characters."];
            else updates.Add(u.Set(c => c.Name, name));
        }
        if (r.HexCode is not null)
        {
            if (!HexColor.IsMatch(r.HexCode)) errors["hexCode"] = ["Hex code must look like #1A2B3C."];
            else updates.Add(u.Set(c => c.HexCode, r.HexCode.ToUpperInvariant()));
        }
        if (r.Status is not null)
        {
            if (r.Status is not ("active" or "hidden")) errors["status"] = ["Status must be 'active' or 'hidden'."];
            else updates.Add(u.Set(c => c.Status, r.Status));
        }
        if (errors.Count > 0) return (null, Results.ValidationProblem(errors));
        if (updates.Count > 0) await colors.UpdateOneAsync(c => c.ColorId == id, u.Combine(updates));
        var after = await colors.Find(c => c.ColorId == id).FirstAsync();
        return (new AdminColorResponse(after.ColorId, after.Name, after.HexCode, after.Status ?? "active",
            await variants.CountDocumentsAsync(v => v.ColorId == id)), null);
    }

    private async Task<Dictionary<long, long>> VariantCountsAsync(string field)
    {
        var rows = await variants.Aggregate()
            .Group<BsonDocument>(new BsonDocument { { "_id", field }, { "n", new BsonDocument("$sum", 1) } })
            .ToListAsync();
        return rows.ToDictionary(r => r["_id"].ToInt64(), r => r["n"].ToInt64());
    }

    // ---- Size charts ------------------------------------------------------------------------------

    private static AdminSizeChartItemResponse ToItemResponse(SizeChartItemDocument i, int index) => new(
        i.SizeChartItemId, i.SizeLabel, i.ChestCm, i.WaistCm, i.HipCm, i.LengthCm, i.Notes,
        i.HeightMinCm, i.HeightMaxCm, i.WeightMinKg, i.WeightMaxKg, i.DisplayOrder ?? index);

    public async Task<AdminSizeChartResponse[]> ListChartsAsync(long? categoryId)
    {
        var filter = categoryId is { } c ? Builders<SizeChartDocument>.Filter.Eq(x => x.CategoryId, c) : Builders<SizeChartDocument>.Filter.Empty;
        var list = await charts.Find(filter).SortBy(x => x.SizeChartId).ToListAsync();
        var ids = list.Select(x => x.SizeChartId).ToArray();
        var itemCounts = (await chartItems.Aggregate()
            .Match(Builders<SizeChartItemDocument>.Filter.In(i => i.SizeChartId, ids))
            .Group<BsonDocument>(new BsonDocument { { "_id", "$size_chart_id" }, { "n", new BsonDocument("$sum", 1) } })
            .ToListAsync()).ToDictionary(r => r["_id"].ToInt64(), r => r["n"].ToInt32());
        var productCounts = (await products.Aggregate()
            .Match(Builders<ProductDocument>.Filter.In(p => p.SizeChartId, ids.Select(i => (long?)i)))
            .Group<BsonDocument>(new BsonDocument { { "_id", "$size_chart_id" }, { "n", new BsonDocument("$sum", 1) } })
            .ToListAsync()).ToDictionary(r => r["_id"].ToInt64(), r => r["n"].ToInt64());
        var names = (await categories.Find(Builders<CategoryDocument>.Filter.Empty).ToListAsync()).ToDictionary(x => x.CategoryId, x => x.Name);
        return list.Select(x => new AdminSizeChartResponse(
            x.SizeChartId, x.Name, x.Description, x.CategoryId,
            x.CategoryId is { } cid ? names.GetValueOrDefault(cid) : null,
            itemCounts.GetValueOrDefault(x.SizeChartId), productCounts.GetValueOrDefault(x.SizeChartId), x.CreatedAt, null,
            x.IsDefault == true)).ToArray();
    }

    public async Task<AdminSizeChartResponse?> GetChartAsync(long id)
    {
        var chart = await charts.Find(c => c.SizeChartId == id).FirstOrDefaultAsync();
        if (chart is null) return null;
        var rows = (await chartItems.Find(i => i.SizeChartId == id).ToListAsync())
            .OrderBy(i => i.DisplayOrder ?? int.MaxValue).ThenBy(i => i.SizeChartItemId).ToList();
        var category = chart.CategoryId is { } cid ? await categories.Find(c => c.CategoryId == cid).FirstOrDefaultAsync() : null;
        return new AdminSizeChartResponse(chart.SizeChartId, chart.Name, chart.Description, chart.CategoryId, category?.Name,
            rows.Count, await products.CountDocumentsAsync(p => p.SizeChartId == id), chart.CreatedAt,
            rows.Select(ToItemResponse).ToArray(), chart.IsDefault == true);
    }

    private static bool BadRange(double? min, double? max, double lowest, double highest) =>
        min is < 0 || max is < 0 || min < lowest || max < lowest || min > highest || max > highest
        || (min is not null && max is not null && min > max);

    public async Task<(AdminSizeChartResponse? Chart, IResult? Error)> SaveChartAsync(long? id, SizeChartRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        var name = r.Name?.Trim();
        if (name is null || name.Length is < 2 or > 150) errors["name"] = ["Name must be between 2 and 150 characters."];
        if (r.Description is { Length: > 1000 }) errors["description"] = ["Description must be at most 1000 characters."];
        if (r.CategoryId is { } cid && !await categories.Find(c => c.CategoryId == cid).AnyAsync())
            errors["categoryId"] = ["Category does not exist."];
        if (r.IsDefault == true && r.CategoryId is null)
            errors["isDefault"] = ["A default size chart must belong to a category."];
        var rows = r.Items ?? [];
        if (rows.Length is 0 or > 30) errors["items"] = ["Provide between 1 and 30 size rows."];
        else
        {
            var labels = rows.Select(i => i.SizeLabel?.Trim().ToUpperInvariant()).ToList();
            if (labels.Any(string.IsNullOrEmpty) || rows.Any(i => i.SizeLabel!.Trim().Length > 20))
                errors["items"] = ["Every row needs a size label of at most 20 characters."];
            else if (labels.Distinct().Count() != labels.Count)
                errors["items"] = ["Size labels must be unique within a chart."];
            else if (rows.Any(i => new[] { i.ChestCm, i.WaistCm, i.HipCm, i.LengthCm }.Any(v => v is < 0 or > 300)
                     || i.Notes is { Length: > 300 }))
                errors["items"] = ["Measurements must be between 0 and 300 cm and notes at most 300 characters."];
            else if (rows.Any(i => BadRange(i.HeightMinCm, i.HeightMaxCm, 50, 250)))
                errors["items"] = ["Height range must be within 50-250 cm and min must not exceed max."];
            else if (rows.Any(i => BadRange(i.WeightMinKg, i.WeightMaxKg, 10, 250)))
                errors["items"] = ["Weight range must be within 10-250 kg and min must not exceed max."];
        }
        if (errors.Count > 0) return (null, Results.ValidationProblem(errors));

        var description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        var now = DateTime.UtcNow;
        long chartId;
        if (id is { } existingId)
        {
            if (!await charts.Find(c => c.SizeChartId == existingId).AnyAsync()) return (null, Results.NotFound());
            chartId = existingId;
            await charts.UpdateOneAsync(c => c.SizeChartId == chartId, Builders<SizeChartDocument>.Update
                .Set(c => c.Name, name).Set(c => c.Description, description).Set(c => c.CategoryId, r.CategoryId)
                .Set(c => c.IsDefault, r.IsDefault == true).Set(c => c.UpdatedAt, now));
        }
        else
        {
            chartId = await counters.NextAsync("size_charts");
            await charts.InsertOneAsync(new SizeChartDocument
            {
                SizeChartId = chartId, Name = name, Description = description, CategoryId = r.CategoryId,
                CreatedAt = now, UpdatedAt = now, IsDefault = r.IsDefault == true
            });
        }

        if (r.IsDefault == true)
        {
            await charts.UpdateManyAsync(c => c.CategoryId == r.CategoryId && c.SizeChartId != chartId,
                Builders<SizeChartDocument>.Update.Set(c => c.IsDefault, false));
        }

        // Rows keep their id when the label stays (products and history may point at them); only
        // removed labels are deleted and new labels inserted.
        var existing = await chartItems.Find(i => i.SizeChartId == chartId).ToListAsync();
        var byLabel = existing.ToDictionary(i => i.SizeLabel!.Trim().ToUpperInvariant());
        var keep = new HashSet<long>();
        foreach (var (row, index) in rows.Select((x, n) => (x, n)))
        {
            var label = row.SizeLabel!.Trim();
            var notes = string.IsNullOrWhiteSpace(row.Notes) ? null : row.Notes.Trim();
            if (byLabel.TryGetValue(label.ToUpperInvariant(), out var current))
            {
                keep.Add(current.SizeChartItemId);
                await chartItems.UpdateOneAsync(i => i.SizeChartItemId == current.SizeChartItemId, Builders<SizeChartItemDocument>.Update
                    .Set(i => i.SizeLabel, label).Set(i => i.ChestCm, row.ChestCm).Set(i => i.WaistCm, row.WaistCm)
                    .Set(i => i.HipCm, row.HipCm).Set(i => i.LengthCm, row.LengthCm).Set(i => i.Notes, notes)
                    .Set(i => i.HeightMinCm, row.HeightMinCm).Set(i => i.HeightMaxCm, row.HeightMaxCm)
                    .Set(i => i.WeightMinKg, row.WeightMinKg).Set(i => i.WeightMaxKg, row.WeightMaxKg)
                    .Set(i => i.DisplayOrder, row.DisplayOrder ?? index));
            }
            else
            {
                await chartItems.InsertOneAsync(new SizeChartItemDocument
                {
                    SizeChartItemId = await counters.NextAsync("size_chart_items"), SizeChartId = chartId,
                    SizeLabel = label, ChestCm = row.ChestCm, WaistCm = row.WaistCm, HipCm = row.HipCm,
                    LengthCm = row.LengthCm, Notes = notes, HeightMinCm = row.HeightMinCm, HeightMaxCm = row.HeightMaxCm,
                    WeightMinKg = row.WeightMinKg, WeightMaxKg = row.WeightMaxKg, DisplayOrder = row.DisplayOrder ?? index
                });
            }
        }

        var removed = existing.Where(i => !keep.Contains(i.SizeChartItemId)).Select(i => i.SizeChartItemId).ToArray();
        if (removed.Length > 0) await chartItems.DeleteManyAsync(Builders<SizeChartItemDocument>.Filter.In(i => i.SizeChartItemId, removed));

        return (await GetChartAsync(chartId), null);
    }

    public async Task<(AdminSizeChartResponse? Chart, IResult? Error)> DuplicateChartAsync(long id, DuplicateSizeChartRequest r)
    {
        var source = await GetChartAsync(id);
        if (source is null) return (null, Results.NotFound());
        var name = string.IsNullOrWhiteSpace(r.Name) ? $"{source.Name} (bản sao)" : r.Name.Trim();
        return await SaveChartAsync(null, new SizeChartRequest(name, source.Description, source.CategoryId,
            source.Items!.Select(i => new SizeChartItemInput(i.SizeLabel, i.ChestCm, i.WaistCm, i.HipCm, i.LengthCm, i.Notes,
                i.HeightMinCm, i.HeightMaxCm, i.WeightMinKg, i.WeightMaxKg, i.DisplayOrder)).ToArray(), false));
    }

    public async Task<IResult> DeleteChartAsync(long id)
    {
        if (!await charts.Find(c => c.SizeChartId == id).AnyAsync()) return Results.NotFound();
        if (await products.Find(p => p.SizeChartId == id).AnyAsync())
            return Results.Conflict(new { message = "Products still use this size chart." });
        await chartItems.DeleteManyAsync(i => i.SizeChartId == id);
        await charts.DeleteOneAsync(c => c.SizeChartId == id);
        return Results.NoContent();
    }
}
