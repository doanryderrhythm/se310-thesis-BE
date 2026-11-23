using System.Text.RegularExpressions;
using AuthApi.Common;
using AuthApi.Data;
using AuthApi.Features.Inventory;
using AuthApi.Features.Promotions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AuthApi.Features.Purchasing;

/// <summary>
/// Goods receipts (phiếu nhập hàng): the only way stock goes up. A receipt starts as a draft and can be edited;
/// completing it adds every line to the stock (and updates the average cost) exactly once, even when two admins
/// press "complete" together or the server stops half way (lines carry an "applied" flag, the receipt a lock token).
/// </summary>
internal sealed class GoodsReceiptService(
    IMongoDatabase db, CounterService counters, InventoryService stock, ILogger logger)
{
    private const string RefType = "goods_receipt";
    private const int MaxLines = 200;
    private const double MaxUnitCost = 100_000_000;

    private readonly IMongoCollection<GoodsReceiptDocument> receipts = db.GetCollection<GoodsReceiptDocument>("goods_receipts");
    private readonly IMongoCollection<SupplierDocument> suppliers = db.GetCollection<SupplierDocument>("suppliers");
    private readonly IMongoCollection<ProductVariantDocument> variants = db.GetCollection<ProductVariantDocument>("product_variants");
    private readonly IMongoCollection<ProductDocument> products = db.GetCollection<ProductDocument>("products");
    private readonly IMongoCollection<SizeDocument> sizes = db.GetCollection<SizeDocument>("sizes");
    private readonly IMongoCollection<ColorDocument> colors = db.GetCollection<ColorDocument>("colors");
    private readonly IMongoCollection<InventoryDocument> inventory = db.GetCollection<InventoryDocument>("inventory");
    private readonly IMongoCollection<User> users = db.GetCollection<User>("users");

    public static async Task SetupAsync(IMongoDatabase db, CounterService counters)
    {
        var receipts = db.GetCollection<GoodsReceiptDocument>("goods_receipts");
        await receipts.Indexes.CreateOneAsync(new CreateIndexModel<GoodsReceiptDocument>(
            Builders<GoodsReceiptDocument>.IndexKeys.Ascending(r => r.ReceiptId),
            new CreateIndexOptions { Unique = true, Name = "receipt_id_1" }));
        await receipts.Indexes.CreateOneAsync(new CreateIndexModel<GoodsReceiptDocument>(
            Builders<GoodsReceiptDocument>.IndexKeys.Ascending(r => r.ReceiptCode),
            new CreateIndexOptions { Unique = true, Name = "receipt_code_unique" }));
        await receipts.Indexes.CreateOneAsync(new CreateIndexModel<GoodsReceiptDocument>(
            Builders<GoodsReceiptDocument>.IndexKeys.Ascending(r => r.SupplierId).Descending(r => r.ReceivedAt),
            new CreateIndexOptions { Name = "supplier_received" }));
        await IdCounters.SyncAsync(db, counters, "goods_receipts", "goods_receipts", "receipt_id");
    }

    public static string CodeFor(long id) => $"PN-{1000 + id}";

    private static IResult Conflict(string message) => Results.Conflict(new { message });

    // ---- Reads ------------------------------------------------------------------------------------

    public async Task<PageResponse<ReceiptListItem>> ListAsync(
        string? q, string? status, long? supplierId, DateTime? fromUtc, DateTime? toUtc, int page, int pageSize)
    {
        var b = Builders<GoodsReceiptDocument>.Filter;
        var filter = b.Empty;
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = new BsonRegularExpression(Regex.Escape(q.Trim()), "i");
            filter &= b.Regex(r => r.ReceiptCode, pattern) | b.Regex(r => r.SupplierInvoiceNo, pattern) | b.Regex(r => r.SupplierName, pattern);
        }
        if (!string.IsNullOrWhiteSpace(status)) filter &= b.Eq(r => r.Status, status);
        if (supplierId is { } sid) filter &= b.Eq(r => r.SupplierId, sid);
        if (fromUtc is { } from) filter &= b.Gte(r => r.ReceivedAt, from);
        if (toUtc is { } to) filter &= b.Lt(r => r.ReceivedAt, to);
        var total = await receipts.CountDocumentsAsync(filter);
        var list = await receipts.Find(filter).SortByDescending(r => r.ReceivedAt).ThenByDescending(r => r.ReceiptId)
            .Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync();
        return new PageResponse<ReceiptListItem>(list.Select(r => new ReceiptListItem(
            r.ReceiptId, r.ReceiptCode, r.SupplierId, r.SupplierName, r.SupplierInvoiceNo, r.ReceivedAt,
            r.Items.Count, r.TotalQuantity, r.TotalAmount, r.Status, r.CreatedAt)).ToArray(), page, pageSize, total);
    }

    public Task<GoodsReceiptDocument?> FindAsync(string code) =>
        receipts.Find(r => r.ReceiptCode == code).FirstOrDefaultAsync()!;

    public async Task<ReceiptDetail?> GetAsync(string code)
    {
        var receipt = await FindAsync(code);
        return receipt is null ? null : await BuildDetailAsync(receipt);
    }

    private async Task<ReceiptDetail> BuildDetailAsync(GoodsReceiptDocument r)
    {
        var supplier = await suppliers.Find(s => s.SupplierId == r.SupplierId).FirstOrDefaultAsync();
        var onHand = (await inventory.Find(Builders<InventoryDocument>.Filter.In(i => i.VariantId, r.Items.Select(i => i.VariantId))).ToListAsync())
            .ToDictionary(i => i.VariantId, i => i.QuantityOnHand ?? 0);
        var actors = (await users.Find(Builders<User>.Filter.In(u => u.UserId,
                new[] { r.CreatedBy, r.CompletedBy }.Where(x => x is not null).Select(x => x!.Value))).ToListAsync())
            .ToDictionary(u => u.UserId, u => u.FullName);
        var draft = r.Status == ReceiptStatuses.Draft;
        return new ReceiptDetail(
            r.ReceiptId, r.ReceiptCode, r.Status,
            supplier is null ? null : new SupplierResponse(supplier.SupplierId, supplier.Name, supplier.Phone, supplier.Email,
                supplier.Address, supplier.TaxCode, supplier.Note, supplier.Status, 0, supplier.CreatedAt, supplier.UpdatedAt),
            r.SupplierInvoiceNo, r.ReceivedAt, r.Note, r.TotalQuantity, r.TotalAmount,
            r.Items.Select(i => new ReceiptItemResponse(i.VariantId, i.ProductId, i.ProductName, i.Sku, i.Size, i.Color,
                i.Quantity, i.UnitCost, i.LineTotal, i.Applied, onHand.TryGetValue(i.VariantId, out var n) ? n : null)).ToArray(),
            r.CreatedBy is { } c ? actors.GetValueOrDefault(c) : null, r.CreatedAt, r.UpdatedAt,
            r.CompletedBy is { } d ? actors.GetValueOrDefault(d) : null, r.CompletedAt, r.CancelledAt, r.CancelReason,
            draft, draft && r.Items.Count > 0, draft);
    }

    // ---- Create / update (draft only) ----------------------------------------------------------------

    private sealed record Checked(
        SupplierDocument Supplier, string? Invoice, DateTime ReceivedAt, string? Note, List<GoodsReceiptItemDocument> Items);

    private async Task<(Checked? Value, IResult? Error)> CheckAsync(ReceiptRequest r, long? exceptReceiptId)
    {
        var errors = new Dictionary<string, string[]>();
        SupplierDocument? supplier = null;
        if (r.SupplierId is not { } supplierId)
            errors["supplierId"] = ["Choose a supplier."];
        else
        {
            supplier = await suppliers.Find(s => s.SupplierId == supplierId).FirstOrDefaultAsync();
            if (supplier is null) errors["supplierId"] = ["Supplier does not exist."];
            else if (supplier.Status != "active") errors["supplierId"] = ["Supplier is hidden; choose an active supplier."];
        }

        var invoice = Text.Clean(r.SupplierInvoiceNo);
        if (invoice is { Length: > 50 }) errors["supplierInvoiceNo"] = ["Supplier invoice number must be at most 50 characters."];
        var note = Text.Clean(r.Note);
        if (note is { Length: > 500 }) errors["note"] = ["Note must be at most 500 characters."];
        var receivedAt = r.ReceivedAt is { } at ? CampaignRules.ToUtc(at) : DateTime.UtcNow;
        if (receivedAt > DateTime.UtcNow.AddDays(1)) errors["receivedAt"] = ["Received date cannot be in the future."];

        var input = r.Items ?? [];
        var lineErrors = new List<string>();
        if (input.Length is 0 or > MaxLines) lineErrors.Add($"Add between 1 and {MaxLines} lines.");
        var variantIds = input.Where(i => i.VariantId is not null).Select(i => i.VariantId!.Value).ToArray();
        if (variantIds.Distinct().Count() != variantIds.Length) lineErrors.Add("Each variant may appear only once; merge the quantities.");
        var known = (await variants.Find(Builders<ProductVariantDocument>.Filter.In(v => v.VariantId, variantIds)).ToListAsync())
            .ToDictionary(v => v.VariantId);
        foreach (var (line, index) in input.Select((x, n) => (x, n)))
        {
            var label = $"Line {index + 1}";
            if (line.VariantId is not { } vid || !known.ContainsKey(vid)) lineErrors.Add($"{label}: variant does not exist.");
            if (line.Quantity is not (> 0 and <= 100_000)) lineErrors.Add($"{label}: quantity must be between 1 and 100000.");
            if (line.UnitCost is not (>= 0 and <= MaxUnitCost)) lineErrors.Add($"{label}: unit cost must be between 0 and 100,000,000.");
        }
        if (lineErrors.Count > 0) errors["items"] = [.. lineErrors];

        if (invoice is not null && supplier is not null && !errors.ContainsKey("supplierInvoiceNo"))
        {
            var taken = await receipts.Find(x => x.SupplierId == supplier.SupplierId && x.SupplierInvoiceNo == invoice
                && x.Status != ReceiptStatuses.Cancelled && x.ReceiptId != (exceptReceiptId ?? 0)).AnyAsync();
            if (taken) errors["supplierInvoiceNo"] = ["This supplier invoice is already on another goods receipt."];
        }
        if (errors.Count > 0) return (null, Results.ValidationProblem(errors));

        var products_ = (await products.Find(Builders<ProductDocument>.Filter.In(p => p.ProductId, known.Values.Select(v => v.ProductId).Distinct())).ToListAsync())
            .ToDictionary(p => p.ProductId, p => p.Name);
        var sizeNames = (await sizes.Find(Builders<SizeDocument>.Filter.In(s => s.SizeId, known.Values.Select(v => v.SizeId).Distinct())).ToListAsync())
            .ToDictionary(s => s.SizeId, s => s.Name);
        var colorNames = (await colors.Find(Builders<ColorDocument>.Filter.In(c => c.ColorId, known.Values.Select(v => v.ColorId).Distinct())).ToListAsync())
            .ToDictionary(c => c.ColorId, c => c.Name);

        var items = input.Select(i =>
        {
            var v = known[i.VariantId!.Value];
            return new GoodsReceiptItemDocument
            {
                VariantId = v.VariantId, ProductId = v.ProductId, ProductName = products_.GetValueOrDefault(v.ProductId),
                Sku = v.Sku, Size = sizeNames.GetValueOrDefault(v.SizeId), Color = colorNames.GetValueOrDefault(v.ColorId),
                Quantity = i.Quantity!.Value, UnitCost = i.UnitCost!.Value,
                LineTotal = Math.Round(i.Quantity!.Value * i.UnitCost!.Value, 2), Applied = false
            };
        }).ToList();
        return (new Checked(supplier!, invoice, receivedAt, note, items), null);
    }

    public async Task<(ReceiptDetail? Receipt, IResult? Error)> CreateAsync(long adminId, ReceiptRequest r)
    {
        var (value, error) = await CheckAsync(r, null);
        if (error is not null) return (null, error);
        var now = DateTime.UtcNow;
        var id = await counters.NextAsync("goods_receipts");
        var receipt = new GoodsReceiptDocument
        {
            ReceiptId = id, ReceiptCode = CodeFor(id), SupplierId = value!.Supplier.SupplierId, SupplierName = value.Supplier.Name,
            SupplierInvoiceNo = value.Invoice, ReceivedAt = value.ReceivedAt, Note = value.Note, Status = ReceiptStatuses.Draft,
            Items = value.Items, TotalQuantity = value.Items.Sum(i => i.Quantity), TotalAmount = value.Items.Sum(i => i.LineTotal),
            CreatedBy = adminId, CreatedAt = now, UpdatedAt = now
        };
        await receipts.InsertOneAsync(receipt);
        return (await BuildDetailAsync(receipt), null);
    }

    public async Task<(ReceiptDetail? Receipt, IResult? Error)> UpdateAsync(string code, ReceiptRequest r)
    {
        var existing = await FindAsync(code);
        if (existing is null) return (null, Results.NotFound());
        if (existing.Status != ReceiptStatuses.Draft) return (null, Conflict("Only a draft goods receipt can be edited."));
        var (value, error) = await CheckAsync(r, existing.ReceiptId);
        if (error is not null) return (null, error);

        var updated = await receipts.FindOneAndUpdateAsync(
            x => x.ReceiptId == existing.ReceiptId && x.Status == ReceiptStatuses.Draft && x.CompletionToken == null,
            Builders<GoodsReceiptDocument>.Update
                .Set(x => x.SupplierId, value!.Supplier.SupplierId).Set(x => x.SupplierName, value.Supplier.Name)
                .Set(x => x.SupplierInvoiceNo, value.Invoice).Set(x => x.ReceivedAt, value.ReceivedAt).Set(x => x.Note, value.Note)
                .Set(x => x.Items, value.Items).Set(x => x.TotalQuantity, value.Items.Sum(i => i.Quantity))
                .Set(x => x.TotalAmount, value.Items.Sum(i => i.LineTotal)).Set(x => x.UpdatedAt, DateTime.UtcNow),
            new FindOneAndUpdateOptions<GoodsReceiptDocument> { ReturnDocument = ReturnDocument.After });
        return updated is null
            ? (null, Conflict("The goods receipt was changed or is being completed. Reload it and try again."))
            : (await BuildDetailAsync(updated), null);
    }

    public async Task<(ReceiptDetail? Receipt, IResult? Error)> CancelAsync(long adminId, string code, string? reason)
    {
        var existing = await FindAsync(code);
        if (existing is null) return (null, Results.NotFound());
        var text = Text.Clean(reason);
        if (text is null || text.Length is < 10 or > 300)
            return (null, Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["Reason must be between 10 and 300 characters."] }));
        var now = DateTime.UtcNow;
        var updated = await receipts.FindOneAndUpdateAsync(
            x => x.ReceiptId == existing.ReceiptId && x.Status == ReceiptStatuses.Draft && x.CompletionToken == null,
            Builders<GoodsReceiptDocument>.Update.Set(x => x.Status, ReceiptStatuses.Cancelled).Set(x => x.CancelReason, text)
                .Set(x => x.CancelledAt, now).Set(x => x.UpdatedAt, now),
            new FindOneAndUpdateOptions<GoodsReceiptDocument> { ReturnDocument = ReturnDocument.After });
        return updated is null
            ? (null, Conflict("Only a draft goods receipt that is not being completed can be cancelled."))
            : (await BuildDetailAsync(updated), null);
    }

    // ---- Complete ---------------------------------------------------------------------------------------

    public async Task<(ReceiptDetail? Receipt, IResult? Error)> CompleteAsync(long adminId, string code)
    {
        var existing = await FindAsync(code);
        if (existing is null) return (null, Results.NotFound());
        if (existing.Status != ReceiptStatuses.Draft) return (null, Conflict($"The goods receipt is already {existing.Status}."));
        if (existing.Items.Count == 0) return (null, Conflict("The goods receipt has no lines."));

        // 1. Take the lock so two requests cannot both add the stock.
        var token = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var stale = now.AddMinutes(-2);
        var locked = await receipts.FindOneAndUpdateAsync(
            x => x.ReceiptId == existing.ReceiptId && x.Status == ReceiptStatuses.Draft
                && (x.CompletionToken == null || x.CompletionLockedAt < stale),
            Builders<GoodsReceiptDocument>.Update.Set(x => x.CompletionToken, token).Set(x => x.CompletionLockedAt, now),
            new FindOneAndUpdateOptions<GoodsReceiptDocument> { ReturnDocument = ReturnDocument.After });
        if (locked is null) return (null, Conflict("The goods receipt is being completed by someone else or was already completed."));

        // 2. Add each line once; undo this run's lines if one fails.
        var done = new List<(GoodsReceiptItemDocument Line, InventoryService.ReceiveResult Before)>();
        string? failure = null;
        try
        {
            foreach (var line in locked.Items.Where(i => !i.Applied))
            {
                var before = await stock.ReceiveAsync(line.VariantId, line.Quantity, line.UnitCost, RefType, locked.ReceiptId, adminId,
                    $"Goods receipt {locked.ReceiptCode}");
                if (before is null)
                {
                    failure = $"Variant {line.Sku ?? line.VariantId.ToString()} has no stock record (was it deleted?). Remove the line and try again.";
                    break;
                }

                done.Add((line, before));
                await receipts.UpdateOneAsync(x => x.ReceiptId == locked.ReceiptId && x.CompletionToken == token,
                    Builders<GoodsReceiptDocument>.Update.Set("items.$[line].applied", true),
                    new UpdateOptions { ArrayFilters = [new BsonDocumentArrayFilterDefinition<BsonDocument>(new BsonDocument("line.variant_id", line.VariantId))] });
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Completing goods receipt {Code} failed.", code);
            failure = "The goods receipt could not be completed. Nothing was added; please try again.";
        }

        if (failure is not null)
        {
            foreach (var (line, before) in Enumerable.Reverse(done))
            {
                try
                {
                    await stock.UndoReceiveAsync(line.VariantId, line.Quantity, before.AverageCostBefore, RefType, locked.ReceiptId, adminId);
                    await receipts.UpdateOneAsync(x => x.ReceiptId == locked.ReceiptId && x.CompletionToken == token,
                        Builders<GoodsReceiptDocument>.Update.Set("items.$[line].applied", false),
                        new UpdateOptions { ArrayFilters = [new BsonDocumentArrayFilterDefinition<BsonDocument>(new BsonDocument("line.variant_id", line.VariantId))] });
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Undoing receipt line {Variant} of {Code} failed.", line.VariantId, code);
                }
            }

            await receipts.UpdateOneAsync(x => x.ReceiptId == locked.ReceiptId && x.CompletionToken == token,
                Builders<GoodsReceiptDocument>.Update.Unset(x => x.CompletionToken).Unset(x => x.CompletionLockedAt));
            return (null, Conflict(failure));
        }

        // 3. Commit.
        var completedAt = DateTime.UtcNow;
        var committed = await receipts.FindOneAndUpdateAsync(
            x => x.ReceiptId == locked.ReceiptId && x.CompletionToken == token,
            Builders<GoodsReceiptDocument>.Update.Set(x => x.Status, ReceiptStatuses.Completed)
                .Set(x => x.CompletedAt, completedAt).Set(x => x.CompletedBy, adminId).Set(x => x.UpdatedAt, completedAt)
                .Unset(x => x.CompletionToken).Unset(x => x.CompletionLockedAt),
            new FindOneAndUpdateOptions<GoodsReceiptDocument> { ReturnDocument = ReturnDocument.After });
        return committed is null
            ? (null, Conflict("The goods receipt changed while completing. Reload it to check its status."))
            : (await BuildDetailAsync(committed), null);
    }
}
