using AuthApi.Data;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AuthApi.Features.Inventory;

[BsonIgnoreExtraElements]
internal sealed class InventoryMovementDocument
{
    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("movement_id")] public long MovementId { get; set; }
    [BsonElement("variant_id")] public long VariantId { get; set; }
    [BsonElement("type")] public string Type { get; set; } = string.Empty;
    [BsonElement("delta_on_hand")] public int DeltaOnHand { get; set; }
    [BsonElement("delta_reserved")] public int DeltaReserved { get; set; }
    [BsonElement("on_hand_after")] public int OnHandAfter { get; set; }
    [BsonElement("reserved_after")] public int ReservedAfter { get; set; }
    [BsonElement("reason")] public string? Reason { get; set; }
    [BsonElement("ref_type")] public string? RefType { get; set; }
    [BsonElement("ref_id")] public long? RefId { get; set; }
    [BsonElement("actor_id")] public long? ActorId { get; set; }
    [BsonElement("created_at")] public DateTime CreatedAt { get; set; }
}

/// <summary>
/// The single place that changes stock. Every change is a conditional update (Mongo here has no
/// transactions) and leaves an inventory_movements row. The orders API reuses these methods:
/// place order = Reserve, cancel before handover = ReleaseReserved, hand over to carrier = Ship,
/// returned goods that pass inspection = Restock.
/// </summary>
internal sealed class InventoryService(IMongoDatabase db, CounterService counters)
{
    private readonly IMongoCollection<InventoryDocument> inventory = db.GetCollection<InventoryDocument>("inventory");
    private readonly IMongoCollection<InventoryMovementDocument> movements =
        db.GetCollection<InventoryMovementDocument>("inventory_movements");

    public static async Task SetupAsync(IMongoDatabase db, CounterService counters)
    {
        await db.GetCollection<InventoryMovementDocument>("inventory_movements").Indexes.CreateOneAsync(
            new CreateIndexModel<InventoryMovementDocument>(
                Builders<InventoryMovementDocument>.IndexKeys.Ascending(m => m.VariantId).Descending(m => m.CreatedAt),
                new CreateIndexOptions { Name = "variant_created" }));
        await IdCounters.SyncAsync(db, counters, "inventory_movements", "inventory_movements", "movement_id");
    }

    private static FilterDefinition<InventoryDocument> AvailableAtLeast(int quantity) =>
        new BsonDocumentFilterDefinition<InventoryDocument>(new BsonDocument("$expr",
            new BsonDocument("$gte", new BsonArray
            {
                new BsonDocument("$subtract", new BsonArray { "$quantity_on_hand", "$quantity_reserved" }),
                quantity
            })));

    /// <summary>Creates the stock row of a new variant (inventory_id equals variant_id, as in the seed).</summary>
    public async Task InitializeAsync(long variantId, int onHand, int lowStockThreshold, long? actorId)
    {
        var now = DateTime.UtcNow;
        await inventory.InsertOneAsync(new InventoryDocument
        {
            InventoryId = variantId,
            VariantId = variantId,
            QuantityOnHand = onHand,
            QuantityReserved = 0,
            LowStockThreshold = lowStockThreshold,
            UpdatedAt = now
        });
        if (onHand > 0)
        {
            await movements.InsertOneAsync(new InventoryMovementDocument
            {
                MovementId = await counters.NextAsync("inventory_movements"),
                VariantId = variantId,
                Type = "initial",
                DeltaOnHand = onHand,
                OnHandAfter = onHand,
                Reason = "Initial stock",
                ActorId = actorId,
                CreatedAt = now
            });
        }
    }

    /// <summary>Reserves stock for an order. False if not enough is available.</summary>
    public Task<bool> ReserveAsync(long variantId, int quantity, string refType, long refId, long? actorId) =>
        ApplyAsync(variantId, AvailableAtLeast(quantity), 0, quantity, "reserve", null, refType, refId, actorId);

    /// <summary>
    /// Gives back reserved stock (order cancelled before hand-over). Never fails on inconsistent
    /// data: the reservation is lowered by at most what is reserved. Returns the amount released.
    /// </summary>
    public async Task<int> ReleaseReservedAsync(long variantId, int quantity, string refType, long refId, long? actorId)
    {
        var now = DateTime.UtcNow;
        var pipeline = new EmptyPipelineDefinition<InventoryDocument>().AppendStage<InventoryDocument, InventoryDocument, InventoryDocument>(
            new BsonDocument("$set", new BsonDocument
            {
                { "quantity_reserved", new BsonDocument("$max", new BsonArray
                    { 0, new BsonDocument("$subtract", new BsonArray { new BsonDocument("$ifNull", new BsonArray { "$quantity_reserved", 0 }), quantity }) }) },
                { "updated_at", now }
            }));
        var before = await inventory.FindOneAndUpdateAsync(
            Builders<InventoryDocument>.Filter.Eq(i => i.VariantId, variantId),
            new PipelineUpdateDefinition<InventoryDocument>(pipeline),
            new FindOneAndUpdateOptions<InventoryDocument> { ReturnDocument = ReturnDocument.Before });
        if (before is null) return 0;
        var released = Math.Min(quantity, before.QuantityReserved ?? 0);
        await RecordAsync(variantId, "release", 0, -released, before.QuantityOnHand ?? 0,
            (before.QuantityReserved ?? 0) - released, null, refType, refId, actorId, now);
        return released;
    }

    /// <summary>Undoes an earlier change when a later step of the same operation fails.</summary>
    public Task CompensateAsync(long variantId, int deltaOnHand, int deltaReserved, string refType, long refId, long? actorId) =>
        ApplyAsync(variantId, Builders<InventoryDocument>.Filter.Empty, deltaOnHand, deltaReserved,
            "compensation", "Rolled back: the operation did not complete", refType, refId, actorId);

    /// <summary>Order handed to the carrier: stock leaves the shelf and the reservation ends.</summary>
    public Task<bool> ShipAsync(long variantId, int quantity, string refType, long refId, long? actorId) =>
        ApplyAsync(variantId,
            Builders<InventoryDocument>.Filter.Gte(i => i.QuantityOnHand, quantity)
                & Builders<InventoryDocument>.Filter.Gte(i => i.QuantityReserved, quantity),
            -quantity, -quantity, "ship", null, refType, refId, actorId);

    /// <summary>Goods came back and passed inspection.</summary>
    public Task<bool> RestockAsync(long variantId, int quantity, string refType, long refId, long? actorId, string? reason = null) =>
        ApplyAsync(variantId, Builders<InventoryDocument>.Filter.Empty, quantity, 0, "restock", reason, refType, refId, actorId);

    /// <summary>Stock before a goods receipt line was applied, kept so a failed receipt can be undone exactly.</summary>
    internal sealed record ReceiveResult(int OnHandBefore, double? AverageCostBefore);

    /// <summary>
    /// Goods receipt line: stock goes up and the moving-average unit cost is updated in one atomic step.
    /// Stock with no known cost takes the cost of the first receipt. Returns null if the variant has no stock row.
    /// </summary>
    public async Task<ReceiveResult?> ReceiveAsync(
        long variantId, int quantity, double unitCost, string refType, long refId, long? actorId, string? reason = null)
    {
        var now = DateTime.UtcNow;
        var onHand = new BsonDocument("$ifNull", new BsonArray { "$quantity_on_hand", 0 });
        var newOnHand = new BsonDocument("$add", new BsonArray { onHand, quantity });
        var weighted = new BsonDocument("$round", new BsonArray
        {
            new BsonDocument("$divide", new BsonArray
            {
                new BsonDocument("$add", new BsonArray
                {
                    new BsonDocument("$multiply", new BsonArray { onHand, "$average_cost" }),
                    new BsonDocument("$multiply", new BsonArray { quantity, unitCost })
                }),
                newOnHand
            }),
            2
        });
        var average = new BsonDocument("$cond", new BsonArray
        {
            new BsonDocument("$or", new BsonArray
            {
                new BsonDocument("$lte", new BsonArray { onHand, 0 }),
                new BsonDocument("$eq", new BsonArray { new BsonDocument("$ifNull", new BsonArray { "$average_cost", BsonNull.Value }), BsonNull.Value })
            }),
            unitCost,
            weighted
        });
        var pipeline = new EmptyPipelineDefinition<InventoryDocument>().AppendStage<InventoryDocument, InventoryDocument, InventoryDocument>(
            new BsonDocument("$set", new BsonDocument
            {
                { "average_cost", average }, { "quantity_on_hand", newOnHand }, { "updated_at", now }
            }));
        var before = await inventory.FindOneAndUpdateAsync(
            Builders<InventoryDocument>.Filter.Eq(i => i.VariantId, variantId),
            new PipelineUpdateDefinition<InventoryDocument>(pipeline),
            new FindOneAndUpdateOptions<InventoryDocument> { ReturnDocument = ReturnDocument.Before });
        if (before is null) return null;

        var beforeOnHand = before.QuantityOnHand ?? 0;
        await RecordAsync(variantId, "receipt", quantity, 0, beforeOnHand + quantity, before.QuantityReserved ?? 0,
            reason, refType, refId, actorId, now);
        return new ReceiveResult(beforeOnHand, before.AverageCost);
    }

    /// <summary>Takes a receipt line back (receipt failed half way): stock and average cost return to what they were.</summary>
    public async Task UndoReceiveAsync(
        long variantId, int quantity, double? averageCostBefore, string refType, long refId, long? actorId)
    {
        var now = DateTime.UtcNow;
        var pipeline = new EmptyPipelineDefinition<InventoryDocument>().AppendStage<InventoryDocument, InventoryDocument, InventoryDocument>(
            new BsonDocument("$set", new BsonDocument
            {
                { "quantity_on_hand", new BsonDocument("$subtract", new BsonArray { new BsonDocument("$ifNull", new BsonArray { "$quantity_on_hand", 0 }), quantity }) },
                { "average_cost", averageCostBefore is { } cost ? new BsonDouble(cost) : BsonNull.Value },
                { "updated_at", now }
            }));
        var after = await inventory.FindOneAndUpdateAsync(
            Builders<InventoryDocument>.Filter.Eq(i => i.VariantId, variantId),
            new PipelineUpdateDefinition<InventoryDocument>(pipeline),
            new FindOneAndUpdateOptions<InventoryDocument> { ReturnDocument = ReturnDocument.After });
        if (after is null) return;
        await RecordAsync(variantId, "compensation", -quantity, 0, after.QuantityOnHand ?? 0, after.QuantityReserved ?? 0,
            "Rolled back: the goods receipt did not complete", refType, refId, actorId, now);
    }

    /// <summary>Manual correction by an admin; never lets on-hand drop below what is reserved.</summary>
    public Task<bool> AdjustAsync(long variantId, bool increase, int quantity, string reason, long actorId) =>
        increase
            ? ApplyAsync(variantId, Builders<InventoryDocument>.Filter.Empty, quantity, 0,
                "adjust_increase", reason, "adjustment", null, actorId)
            : ApplyAsync(variantId, AvailableAtLeast(quantity), -quantity, 0,
                "adjust_decrease", reason, "adjustment", null, actorId);

    private async Task<bool> ApplyAsync(
        long variantId, FilterDefinition<InventoryDocument> condition, int deltaOnHand, int deltaReserved,
        string type, string? reason, string? refType, long? refId, long? actorId)
    {
        var now = DateTime.UtcNow;
        var update = Builders<InventoryDocument>.Update.Set(i => i.UpdatedAt, now);
        if (deltaOnHand != 0) update = update.Inc(i => i.QuantityOnHand, deltaOnHand);
        if (deltaReserved != 0) update = update.Inc(i => i.QuantityReserved, deltaReserved);

        var after = await inventory.FindOneAndUpdateAsync(
            Builders<InventoryDocument>.Filter.Eq(i => i.VariantId, variantId) & condition, update,
            new FindOneAndUpdateOptions<InventoryDocument> { ReturnDocument = ReturnDocument.After });
        if (after is null) return false;

        await RecordAsync(variantId, type, deltaOnHand, deltaReserved, after.QuantityOnHand ?? 0,
            after.QuantityReserved ?? 0, reason, refType, refId, actorId, now);
        return true;
    }

    private async Task RecordAsync(
        long variantId, string type, int deltaOnHand, int deltaReserved, int onHandAfter, int reservedAfter,
        string? reason, string? refType, long? refId, long? actorId, DateTime at)
    {
        await movements.InsertOneAsync(new InventoryMovementDocument
        {
            MovementId = await counters.NextAsync("inventory_movements"),
            VariantId = variantId,
            Type = type,
            DeltaOnHand = deltaOnHand,
            DeltaReserved = deltaReserved,
            OnHandAfter = onHandAfter,
            ReservedAfter = reservedAfter,
            Reason = reason,
            RefType = refType,
            RefId = refId,
            ActorId = actorId,
            CreatedAt = at
        });
    }
}
