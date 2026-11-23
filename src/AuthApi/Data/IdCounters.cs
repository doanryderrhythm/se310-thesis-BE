using MongoDB.Bson;
using MongoDB.Driver;

namespace AuthApi.Data;

internal static class IdCounters
{
    /// <summary>Moves the named counter up to the largest id already stored in a collection.</summary>
    public static async Task SyncAsync(
        IMongoDatabase db, CounterService counters, string counterName, string collectionName, string idField)
    {
        var top = await db.GetCollection<BsonDocument>(collectionName)
            .Find(Builders<BsonDocument>.Filter.Empty)
            .Sort(Builders<BsonDocument>.Sort.Descending(idField))
            .Project<BsonDocument>(Builders<BsonDocument>.Projection.Include(idField))
            .Limit(1)
            .FirstOrDefaultAsync();
        var largest = top is not null && top.Contains(idField) ? top[idField].ToInt64() : 0;
        await counters.InitializeAsync(counterName, largest);
    }
}
