using System.Net.Mail;
using System.Text.RegularExpressions;
using AuthApi.Common;
using AuthApi.Data;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AuthApi.Features.Purchasing;

internal sealed partial class SupplierService(IMongoDatabase db, CounterService counters)
{
    private readonly IMongoCollection<SupplierDocument> suppliers = db.GetCollection<SupplierDocument>("suppliers");
    private readonly IMongoCollection<GoodsReceiptDocument> receipts = db.GetCollection<GoodsReceiptDocument>("goods_receipts");

    [GeneratedRegex(@"\A[0-9]{9,12}\z")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"\A[0-9]{10}(-[0-9]{3})?\z")]
    private static partial Regex TaxCodePattern();

    public static async Task SetupAsync(IMongoDatabase db, CounterService counters)
    {
        var suppliers = db.GetCollection<SupplierDocument>("suppliers");
        await suppliers.Indexes.CreateOneAsync(new CreateIndexModel<SupplierDocument>(
            Builders<SupplierDocument>.IndexKeys.Ascending(s => s.SupplierId),
            new CreateIndexOptions { Unique = true, Name = "supplier_id_1" }));
        await IdCounters.SyncAsync(db, counters, "suppliers", "suppliers", "supplier_id");
    }

    private static SupplierResponse ToResponse(SupplierDocument s, long receiptCount) =>
        new(s.SupplierId, s.Name, s.Phone, s.Email, s.Address, s.TaxCode, s.Note, s.Status, receiptCount, s.CreatedAt, s.UpdatedAt);

    public async Task<PageResponse<SupplierResponse>> ListAsync(string? q, string? status, int page, int pageSize)
    {
        var b = Builders<SupplierDocument>.Filter;
        var filter = b.Empty;
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = new BsonRegularExpression(Regex.Escape(q.Trim()), "i");
            filter &= b.Regex(s => s.Name, pattern) | b.Regex(s => s.Phone, pattern) | b.Regex(s => s.TaxCode, pattern);
        }
        if (!string.IsNullOrWhiteSpace(status)) filter &= b.Eq(s => s.Status, status);
        var total = await suppliers.CountDocumentsAsync(filter);
        var list = await suppliers.Find(filter).SortBy(s => s.Name).ThenBy(s => s.SupplierId)
            .Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync();
        var counts = await ReceiptCountsAsync(list.Select(s => s.SupplierId));
        return new PageResponse<SupplierResponse>(
            list.Select(s => ToResponse(s, counts.GetValueOrDefault(s.SupplierId))).ToArray(), page, pageSize, total);
    }

    public async Task<SupplierResponse?> GetAsync(long id)
    {
        var supplier = await suppliers.Find(s => s.SupplierId == id).FirstOrDefaultAsync();
        return supplier is null ? null : ToResponse(supplier, await receipts.CountDocumentsAsync(r => r.SupplierId == id));
    }

    private async Task<Dictionary<long, long>> ReceiptCountsAsync(IEnumerable<long> ids)
    {
        var rows = await receipts.Aggregate().Match(Builders<GoodsReceiptDocument>.Filter.In(r => r.SupplierId, ids))
            .Group<BsonDocument>(new BsonDocument { { "_id", "$supplier_id" }, { "n", new BsonDocument("$sum", 1) } }).ToListAsync();
        return rows.ToDictionary(r => r["_id"].ToInt64(), r => r["n"].ToInt64());
    }

    private static Dictionary<string, string[]> Validate(
        SupplierRequest r, bool creating, out string? name, out string? phone, out string? email,
        out string? address, out string? taxCode, out string? note)
    {
        var errors = new Dictionary<string, string[]>();
        name = Text.Clean(r.Name);
        if ((creating || r.Name is not null) && (name is null || name.Length is < 2 or > 150))
            errors["name"] = ["Name must be between 2 and 150 characters."];
        phone = PhoneNumbers.Normalize(r.Phone);
        if (phone is not null && !PhonePattern().IsMatch(phone)) errors["phone"] = ["Phone must be 9 to 12 digits."];
        email = Text.Clean(r.Email);
        if (email is not null && (email.Length > 150 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email))
            errors["email"] = ["Email is not valid."];
        address = Text.Clean(r.Address);
        if (address is { Length: > 255 }) errors["address"] = ["Address must be at most 255 characters."];
        taxCode = Text.Clean(r.TaxCode);
        if (taxCode is not null && !TaxCodePattern().IsMatch(taxCode)) errors["taxCode"] = ["Tax code must be 10 digits, optionally followed by -NNN."];
        note = Text.Clean(r.Note);
        if (note is { Length: > 500 }) errors["note"] = ["Note must be at most 500 characters."];
        if (r.Status is not (null or "active" or "hidden")) errors["status"] = ["Status must be 'active' or 'hidden'."];
        return errors;
    }

    private async Task<bool> NameTakenAsync(string name, long? exceptId)
    {
        var all = await suppliers.Find(Builders<SupplierDocument>.Filter.Empty).Project(s => new { s.SupplierId, s.Name }).ToListAsync();
        var folded = Text.Fold(name);
        return all.Any(s => s.SupplierId != (exceptId ?? 0) && Text.Fold(s.Name) == folded);
    }

    public async Task<(SupplierResponse? Supplier, IResult? Error)> CreateAsync(SupplierRequest r)
    {
        var errors = Validate(r, true, out var name, out var phone, out var email, out var address, out var taxCode, out var note);
        if (errors.Count > 0) return (null, Results.ValidationProblem(errors));
        if (await NameTakenAsync(name!, null))
            return (null, Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["A supplier with this name already exists."] }));
        var now = DateTime.UtcNow;
        var supplier = new SupplierDocument
        {
            SupplierId = await counters.NextAsync("suppliers"), Name = name!, Phone = phone, Email = email, Address = address,
            TaxCode = taxCode, Note = note, Status = r.Status ?? "active", CreatedAt = now, UpdatedAt = now
        };
        await suppliers.InsertOneAsync(supplier);
        return (ToResponse(supplier, 0), null);
    }

    public async Task<(SupplierResponse? Supplier, IResult? Error)> UpdateAsync(long id, SupplierRequest r)
    {
        var existing = await suppliers.Find(s => s.SupplierId == id).FirstOrDefaultAsync();
        if (existing is null) return (null, Results.NotFound());
        var errors = Validate(r, false, out var name, out var phone, out var email, out var address, out var taxCode, out var note);
        if (errors.Count > 0) return (null, Results.ValidationProblem(errors));

        var u = Builders<SupplierDocument>.Update.Set(s => s.UpdatedAt, DateTime.UtcNow);
        if (r.Name is not null)
        {
            if (await NameTakenAsync(name!, id))
                return (null, Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["A supplier with this name already exists."] }));
            u = u.Set(s => s.Name, name!);
        }
        // For contact fields: omitted = unchanged, empty string = clear.
        if (r.Phone is not null) u = u.Set(s => s.Phone, phone);
        if (r.Email is not null) u = u.Set(s => s.Email, email);
        if (r.Address is not null) u = u.Set(s => s.Address, address);
        if (r.TaxCode is not null) u = u.Set(s => s.TaxCode, taxCode);
        if (r.Note is not null) u = u.Set(s => s.Note, note);
        if (r.Status is not null) u = u.Set(s => s.Status, r.Status);
        await suppliers.UpdateOneAsync(s => s.SupplierId == id, u);
        return (await GetAsync(id), null);
    }

    public async Task<IResult> DeleteAsync(long id)
    {
        if (!await suppliers.Find(s => s.SupplierId == id).AnyAsync()) return Results.NotFound();
        if (await receipts.Find(r => r.SupplierId == id).AnyAsync())
            return Results.Conflict(new { message = "The supplier has goods receipts and cannot be deleted; hide it instead." });
        await suppliers.DeleteOneAsync(s => s.SupplierId == id);
        return Results.NoContent();
    }
}
