using System.Text.RegularExpressions;
using AuthApi.Common;
using AuthApi.Data;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AuthApi.Features.Catalog;

/// <summary>Which product attribute list a service instance manages.</summary>
internal sealed record AttributeKind(
    string Collection, string IdField, string Counter, string ProductField, string Noun, string NounVi)
{
    public static readonly AttributeKind Material =
        new("materials", "material_id", "materials", "material_id", "material", "chất liệu");
    public static readonly AttributeKind Occasion =
        new("occasions", "occasion_id", "occasions", "occasion_ids", "occasion", "công dụng");
}

/// <summary>
/// Materials (cotton, denim, ...) and occasions (đi chơi, đi học, công sở, ...): admin-managed lists that products
/// point to and the storefront filters by. Stored as plain documents; both lists use this one service.
/// </summary>
internal sealed class AdminAttributeService(IMongoDatabase db, CounterService counters, AttributeKind kind)
{
    private readonly IMongoCollection<BsonDocument> rows = db.GetCollection<BsonDocument>(kind.Collection);
    private readonly IMongoCollection<BsonDocument> products = db.GetCollection<BsonDocument>("products");

    public AttributeKind Kind => kind;

    public static async Task SetupAsync(IMongoDatabase db, CounterService counters, AttributeKind kind)
    {
        var rows = db.GetCollection<BsonDocument>(kind.Collection);
        // Names match the unique indexes of mongo-init/01-schema.js.
        await rows.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending(kind.IdField),
            new CreateIndexOptions { Unique = true, Name = $"{kind.IdField}_1" }));
        await rows.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("slug"),
            new CreateIndexOptions { Unique = true, Name = "slug_unique" }));
        await IdCounters.SyncAsync(db, counters, kind.Counter, kind.Collection, kind.IdField);
    }

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static AttributeResponse ToResponse(BsonDocument d, string idField, long productCount) => new(
        d[idField].ToInt64(), d["name"].AsString, d.GetValue("slug", "").AsString,
        d.GetValue("description", BsonNull.Value).IsBsonNull ? null : d["description"].AsString,
        d.GetValue("display_order", 0).ToInt32(), d.GetValue("status", "active").AsString, productCount,
        d.GetValue("created_at", BsonNull.Value).IsBsonNull ? null : d["created_at"].ToUniversalTime(),
        d.GetValue("updated_at", BsonNull.Value).IsBsonNull ? null : d["updated_at"].ToUniversalTime());

    private FilterDefinition<BsonDocument> UsedBy(long id) => Builders<BsonDocument>.Filter.Eq(kind.ProductField, id);

    private async Task<Dictionary<long, long>> ProductCountsAsync()
    {
        var rowsOut = kind.ProductField == "occasion_ids"
            ? await products.Aggregate().Unwind(kind.ProductField)
                .Group(new BsonDocument { { "_id", $"${kind.ProductField}" }, { "n", new BsonDocument("$sum", 1) } }).ToListAsync()
            : await products.Aggregate()
                .Group(new BsonDocument { { "_id", $"${kind.ProductField}" }, { "n", new BsonDocument("$sum", 1) } }).ToListAsync();
        return rowsOut.Where(r => !r["_id"].IsBsonNull).ToDictionary(r => r["_id"].ToInt64(), r => r["n"].ToInt64());
    }

    public async Task<AttributeResponse[]> ListAsync(string? q, string? status, bool publicOnly = false)
    {
        var b = Builders<BsonDocument>.Filter;
        var filter = b.Empty;
        if (publicOnly) status = "active";
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = new BsonRegularExpression(Regex.Escape(q.Trim()), "i");
            filter &= b.Regex("name", pattern) | b.Regex("slug", pattern);
        }
        if (!string.IsNullOrWhiteSpace(status)) filter &= b.Eq("status", status);
        var list = await rows.Find(filter).ToListAsync();
        var counts = await ProductCountsAsync();
        return list.OrderBy(d => d.GetValue("display_order", int.MaxValue).ToInt32()).ThenBy(d => d[kind.IdField].ToInt64())
            .Select(d => ToResponse(d, kind.IdField, counts.GetValueOrDefault(d[kind.IdField].ToInt64()))).ToArray();
    }

    public async Task<AttributeResponse?> GetAsync(long id)
    {
        var doc = await rows.Find(Builders<BsonDocument>.Filter.Eq(kind.IdField, id)).FirstOrDefaultAsync();
        return doc is null ? null : ToResponse(doc, kind.IdField, await products.CountDocumentsAsync(UsedBy(id)));
    }

    private IResult? Validate(AttributeRequest r, bool creating, out string? name, out string? slug, out string? status)
    {
        var errors = new Dictionary<string, string[]>();
        name = r.Name is null ? null : Text.Clean(r.Name);
        if (creating || r.Name is not null)
        {
            if (name is null || name.Length is < 2 or > 100)
                errors["name"] = [$"Name must be between 2 and 100 characters."];
        }
        slug = null;
        if (r.Slug is not null || creating)
        {
            slug = string.IsNullOrWhiteSpace(r.Slug) ? (name is null ? "" : Slugs.From(name)) : r.Slug.Trim();
            if (!Slugs.IsValid(slug)) errors["slug"] = ["Slug must be lowercase letters, digits and hyphens (at most 180 characters)."];
        }
        if (r.Description is { Length: > 500 }) errors["description"] = ["Description must be at most 500 characters."];
        if (r.DisplayOrder is < 0 or > 10_000) errors["displayOrder"] = ["Display order must be between 0 and 10000."];
        status = r.Status ?? (creating ? "active" : null);
        if (status is not (null or "active" or "hidden")) errors["status"] = ["Status must be 'active' or 'hidden'."];
        return errors.Count > 0 ? Results.ValidationProblem(errors) : null;
    }

    private async Task<bool> NameTakenAsync(string name, long? exceptId)
    {
        var all = await rows.Find(Builders<BsonDocument>.Filter.Empty).Project(Builders<BsonDocument>.Projection
            .Include("name").Include(kind.IdField)).ToListAsync();
        var folded = Text.Fold(name);
        return all.Any(d => d[kind.IdField].ToInt64() != (exceptId ?? 0) && Text.Fold(d["name"].AsString) == folded);
    }

    public async Task<(AttributeResponse? Row, IResult? Error)> CreateAsync(AttributeRequest r)
    {
        var invalid = Validate(r, true, out var name, out var slug, out var status);
        if (invalid is not null) return (null, invalid);
        if (await NameTakenAsync(name!, null)) return (null, Invalid("name", $"A {kind.Noun} with this name already exists."));
        if (await rows.Find(Builders<BsonDocument>.Filter.Eq("slug", slug)).AnyAsync())
            return (null, Invalid("slug", "Slug already exists."));

        var highest = (await rows.Find(Builders<BsonDocument>.Filter.Empty).Project(Builders<BsonDocument>.Projection.Include("display_order")).ToListAsync())
            .Select(d => d.GetValue("display_order", 0).ToInt32()).DefaultIfEmpty(0).Max();
        var now = DateTime.UtcNow;
        var id = await counters.NextAsync(kind.Counter);
        var doc = new BsonDocument
        {
            { kind.IdField, id }, { "name", name }, { "slug", slug },
            { "description", string.IsNullOrWhiteSpace(r.Description) ? BsonNull.Value : (BsonValue)r.Description.Trim() },
            { "display_order", r.DisplayOrder ?? highest + 1 }, { "status", status }, { "created_at", now }, { "updated_at", now }
        };
        try
        {
            await rows.InsertOneAsync(doc);
        }
        catch (MongoWriteException e) when (e.WriteError?.Code == 11000)
        {
            return (null, Invalid("slug", "Slug already exists."));
        }

        return (ToResponse(doc, kind.IdField, 0), null);
    }

    public async Task<(AttributeResponse? Row, IResult? Error)> UpdateAsync(long id, AttributeRequest r)
    {
        var idFilter = Builders<BsonDocument>.Filter.Eq(kind.IdField, id);
        if (!await rows.Find(idFilter).AnyAsync()) return (null, Results.NotFound());
        var invalid = Validate(r, false, out var name, out var slug, out var status);
        if (invalid is not null) return (null, invalid);

        var u = Builders<BsonDocument>.Update.Set("updated_at", DateTime.UtcNow);
        if (name is not null)
        {
            if (await NameTakenAsync(name, id)) return (null, Invalid("name", $"A {kind.Noun} with this name already exists."));
            u = u.Set("name", name);
        }
        if (slug is not null)
        {
            if (await rows.Find(Builders<BsonDocument>.Filter.Eq("slug", slug) & Builders<BsonDocument>.Filter.Ne(kind.IdField, id)).AnyAsync())
                return (null, Invalid("slug", "Slug already exists."));
            u = u.Set("slug", slug);
        }
        if (r.Description is not null) u = u.Set("description", string.IsNullOrWhiteSpace(r.Description) ? BsonNull.Value : (BsonValue)r.Description.Trim());
        if (r.DisplayOrder is { } order) u = u.Set("display_order", order);
        if (status is not null) u = u.Set("status", status);
        try
        {
            await rows.UpdateOneAsync(idFilter, u);
        }
        catch (MongoWriteException e) when (e.WriteError?.Code == 11000)
        {
            return (null, Invalid("slug", "Slug already exists."));
        }

        return (await GetAsync(id), null);
    }

    public async Task<IResult> DeleteAsync(long id)
    {
        var idFilter = Builders<BsonDocument>.Filter.Eq(kind.IdField, id);
        if (!await rows.Find(idFilter).AnyAsync()) return Results.NotFound();
        if (await products.Find(UsedBy(id)).AnyAsync())
            return Results.Conflict(new { message = $"Products still use this {kind.Noun}; hide it instead." });
        await rows.DeleteOneAsync(idFilter);
        return Results.NoContent();
    }

    /// <summary>Ids that exist (and are active unless <paramref name="allowHidden"/>), used to validate product input.</summary>
    public async Task<HashSet<long>> ExistingIdsAsync(IEnumerable<long> ids)
    {
        var wanted = ids.Distinct().ToArray();
        if (wanted.Length == 0) return [];
        var found = await rows.Find(Builders<BsonDocument>.Filter.In(kind.IdField, wanted))
            .Project(Builders<BsonDocument>.Projection.Include(kind.IdField)).ToListAsync();
        return found.Select(d => d[kind.IdField].ToInt64()).ToHashSet();
    }

    public async Task<Dictionary<long, BsonDocument>> AllAsync() =>
        (await rows.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync()).ToDictionary(d => d[kind.IdField].ToInt64());
}
