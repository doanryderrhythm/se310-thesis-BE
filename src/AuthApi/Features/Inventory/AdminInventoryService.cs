using System.Text.RegularExpressions;
using AuthApi.Common;
using AuthApi.Features.Products;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AuthApi.Features.Inventory;

internal sealed class AdminInventoryService(IMongoDatabase db, InventoryService stock)
{
    private const int DefaultThreshold = 5;
    private readonly IMongoCollection<InventoryDocument> inventory = db.GetCollection<InventoryDocument>("inventory");
    private readonly IMongoCollection<ProductVariantDocument> variants = db.GetCollection<ProductVariantDocument>("product_variants");
    private readonly IMongoCollection<ProductDocument> products = db.GetCollection<ProductDocument>("products");
    private readonly IMongoCollection<SizeDocument> sizes = db.GetCollection<SizeDocument>("sizes");
    private readonly IMongoCollection<ColorDocument> colors = db.GetCollection<ColorDocument>("colors");
    private readonly IMongoCollection<InventoryMovementDocument> movements = db.GetCollection<InventoryMovementDocument>("inventory_movements");
    private readonly IMongoCollection<User> users = db.GetCollection<User>("users");

    public async Task<PageResponse<AdminInventoryRow>> ListAsync(
        string? q, bool? lowStock, string? status, int page, int pageSize)
    {
        // Narrow by variant first (search text and variant status), then page over the stock rows.
        var vb = Builders<ProductVariantDocument>.Filter;
        var variantFilter = vb.Empty;
        if (!string.IsNullOrWhiteSpace(status)) variantFilter &= status == "hidden" ? vb.In(v => v.Status, ["hidden", "inactive"]) : vb.Eq(v => v.Status, status);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = new BsonRegularExpression(Regex.Escape(q.Trim()), "i");
            var productIds = await products.Find(Builders<ProductDocument>.Filter.Regex(p => p.Name, pattern)).Project(p => p.ProductId).ToListAsync();
            variantFilter &= vb.Regex(v => v.Sku, pattern) | vb.In(v => v.ProductId, productIds);
        }

        var ib = Builders<InventoryDocument>.Filter;
        var filter = ib.Empty;
        if (!string.IsNullOrWhiteSpace(q) || !string.IsNullOrWhiteSpace(status))
            filter &= ib.In(i => i.VariantId, await variants.Find(variantFilter).Project(v => v.VariantId).ToListAsync());
        if (lowStock == true)
        {
            filter &= new BsonDocumentFilterDefinition<InventoryDocument>(new BsonDocument("$expr",
                new BsonDocument("$lte", new BsonArray
                {
                    new BsonDocument("$subtract", new BsonArray { "$quantity_on_hand", "$quantity_reserved" }),
                    new BsonDocument("$ifNull", new BsonArray { "$low_stock_threshold", DefaultThreshold })
                })));
        }

        var total = await inventory.CountDocumentsAsync(filter);
        var rows = await inventory.Find(filter).SortBy(i => i.VariantId).Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync();
        return new PageResponse<AdminInventoryRow>(await ToRowsAsync(rows), page, pageSize, total);
    }

    public async Task<AdminInventoryRow?> GetAsync(long variantId)
    {
        var row = await inventory.Find(i => i.VariantId == variantId).FirstOrDefaultAsync();
        return row is null ? null : (await ToRowsAsync([row]))[0];
    }

    public static readonly string[] ReasonCodes = ["damaged", "lost", "stocktake", "other"];

    public async Task<(AdminInventoryRow? Row, IResult? Error)> AdjustAsync(long adminId, long variantId, AdjustmentRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        if (r.Type == "increase")
            errors["type"] = ["Stock can only be increased with a goods receipt (POST /api/admin/goods-receipts)."];
        else if (r.Type is not (null or "" or "decrease"))
            errors["type"] = ["Type must be 'decrease'."];
        if (r.ReasonCode is null || !ReasonCodes.Contains(r.ReasonCode))
            errors["reasonCode"] = [$"Reason code must be one of: {string.Join(", ", ReasonCodes)}."];
        if (r.Quantity is not (> 0 and <= 100_000)) errors["quantity"] = ["Quantity must be between 1 and 100000."];
        var reason = r.Reason?.Trim();
        if (reason is null || reason.Length is < 10 or > 300) errors["reason"] = ["Reason must be between 10 and 300 characters."];
        if (errors.Count > 0) return (null, Results.ValidationProblem(errors));
        if (!await inventory.Find(i => i.VariantId == variantId).AnyAsync()) return (null, Results.NotFound());

        if (!await stock.AdjustAsync(variantId, false, r.Quantity!.Value, $"{r.ReasonCode}: {reason}", adminId))
        {
            return (null, Results.Conflict(new
            {
                message = "You cannot remove more than the available stock; on-hand stock cannot drop below the quantity reserved by orders."
            }));
        }

        return (await GetAsync(variantId), null);
    }

    public async Task<(AdminInventoryRow? Row, IResult? Error)> SetThresholdAsync(long variantId, int? threshold)
    {
        if (threshold is null or < 0 or > 100_000)
        {
            return (null, Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["lowStockThreshold"] = ["Threshold must be between 0 and 100000."]
            }));
        }

        var result = await inventory.UpdateOneAsync(i => i.VariantId == variantId,
            Builders<InventoryDocument>.Update.Set(i => i.LowStockThreshold, threshold).Set(i => i.UpdatedAt, DateTime.UtcNow));
        return result.MatchedCount == 0 ? (null, Results.NotFound()) : (await GetAsync(variantId), null);
    }

    public async Task<PageResponse<MovementResponse>?> MovementsAsync(long variantId, int page, int pageSize)
    {
        if (!await inventory.Find(i => i.VariantId == variantId).AnyAsync()) return null;
        var filter = Builders<InventoryMovementDocument>.Filter.Eq(m => m.VariantId, variantId);
        var total = await movements.CountDocumentsAsync(filter);
        var list = await movements.Find(filter).SortByDescending(m => m.CreatedAt).ThenByDescending(m => m.MovementId)
            .Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync();
        var actorIds = list.Where(m => m.ActorId is not null).Select(m => m.ActorId!.Value).Distinct().ToArray();
        var names = (await users.Find(Builders<User>.Filter.In(u => u.UserId, actorIds)).ToListAsync()).ToDictionary(u => u.UserId, u => u.FullName);
        return new PageResponse<MovementResponse>(list.Select(m => new MovementResponse(
            m.MovementId, m.Type, m.DeltaOnHand, m.DeltaReserved, m.OnHandAfter, m.ReservedAfter, m.Reason,
            m.RefType, m.RefId, m.ActorId is { } a ? names.GetValueOrDefault(a) : null, m.CreatedAt)).ToArray(),
            page, pageSize, total);
    }

    private async Task<AdminInventoryRow[]> ToRowsAsync(List<InventoryDocument> rows)
    {
        var vs = (await variants.Find(Builders<ProductVariantDocument>.Filter.In(v => v.VariantId, rows.Select(r => r.VariantId))).ToListAsync())
            .ToDictionary(v => v.VariantId);
        var ps = (await products.Find(Builders<ProductDocument>.Filter.In(p => p.ProductId, vs.Values.Select(v => v.ProductId).Distinct())).ToListAsync())
            .ToDictionary(p => p.ProductId);
        var ss = (await sizes.Find(Builders<SizeDocument>.Filter.In(s => s.SizeId, vs.Values.Select(v => v.SizeId).Distinct())).ToListAsync())
            .ToDictionary(s => s.SizeId, s => s.Name);
        var cs = (await colors.Find(Builders<ColorDocument>.Filter.In(c => c.ColorId, vs.Values.Select(v => v.ColorId).Distinct())).ToListAsync())
            .ToDictionary(c => c.ColorId, c => c.Name);
        return rows.Where(r => vs.ContainsKey(r.VariantId)).Select(r =>
        {
            var v = vs[r.VariantId];
            var onHand = r.QuantityOnHand ?? 0;
            var reserved = r.QuantityReserved ?? 0;
            var threshold = r.LowStockThreshold ?? DefaultThreshold;
            var available = Math.Max(0, onHand - reserved);
            return new AdminInventoryRow(v.VariantId, v.Sku, v.ProductId, ps.GetValueOrDefault(v.ProductId)?.Name,
                ss.GetValueOrDefault(v.SizeId), cs.GetValueOrDefault(v.ColorId), v.Status == "inactive" ? "hidden" : v.Status ?? "active",
                onHand, reserved, available, threshold, available <= threshold, r.UpdatedAt,
                r.AverageCost, Math.Round(onHand * (r.AverageCost ?? 0), 0));
        }).ToArray();
    }
}
