using MongoDB.Bson;
using MongoDB.Driver;

namespace AuthApi.Features.Promotions;

/// <summary>
/// Lifetime spend of a customer = sum of <c>total_amount</c> over their <c>delivered</c> orders.
/// Cancelled, pending and returned-to-seller orders do not count.
/// </summary>
internal sealed record UserOrderStats(int OrderCount, int DeliveredCount, double TotalSpent, DateTime? LastOrderAt);

internal sealed class OrderSpendService(IMongoDatabase db)
{
    private readonly IMongoCollection<BsonDocument> orders = db.GetCollection<BsonDocument>("orders");

    public async Task<double> LifetimeSpendAsync(long userId)
    {
        var rows = await orders.Aggregate()
            .Match(new BsonDocument { { "status", "delivered" }, { "user_id", userId } })
            .Group<BsonDocument>(new BsonDocument
            {
                { "_id", BsonNull.Value },
                { "spent", new BsonDocument("$sum", "$total_amount") }
            })
            .ToListAsync();
        return rows.Count == 0 ? 0 : rows[0]["spent"].ToDouble();
    }

    public async Task<Dictionary<long, UserOrderStats>> StatsForUsersAsync(IEnumerable<long> userIds)
    {
        var rows = await orders.Aggregate()
            .Match(new BsonDocument("user_id", new BsonDocument("$in", new BsonArray(userIds))))
            .Group<BsonDocument>(new BsonDocument
            {
                { "_id", "$user_id" },
                { "orderCount", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                    { new BsonDocument("$ne", new BsonArray { "$status", "cancelled" }), 1, 0 })) },
                { "deliveredCount", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                    { new BsonDocument("$eq", new BsonArray { "$status", "delivered" }), 1, 0 })) },
                { "spent", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                    { new BsonDocument("$eq", new BsonArray { "$status", "delivered" }), "$total_amount", 0 })) },
                { "last", new BsonDocument("$max", "$placed_at") }
            })
            .ToListAsync();
        return rows.ToDictionary(r => r["_id"].ToInt64(), r => new UserOrderStats(
            r["orderCount"].ToInt32(), r["deliveredCount"].ToInt32(), r["spent"].ToDouble(),
            r["last"].IsBsonNull ? null : r["last"].ToUniversalTime()));
    }

    public async Task<List<long>> UserIdsWithSpendAtLeastAsync(double minSpent)
    {
        var rows = await orders.Aggregate()
            .Match(new BsonDocument { { "status", "delivered" }, { "user_id", new BsonDocument("$ne", BsonNull.Value) } })
            .Group<BsonDocument>(new BsonDocument
            {
                { "_id", "$user_id" },
                { "spent", new BsonDocument("$sum", "$total_amount") }
            })
            .Match(new BsonDocument("spent", new BsonDocument("$gte", minSpent)))
            .ToListAsync();
        return rows.Select(r => r["_id"].ToInt64()).ToList();
    }
}
