using MongoDB.Driver;

namespace AuthApi.Data;

internal sealed class CounterService(IMongoCollection<Counter> counters)
{
    public async Task<long> NextAsync(string name)
    {
        var counter = await counters.FindOneAndUpdateAsync(
            item => item.Id == name,
            Builders<Counter>.Update.Inc(item => item.SequenceValue, 1),
            new FindOneAndUpdateOptions<Counter>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After
            });
        return counter.SequenceValue;
    }

    public async Task InitializeAsync(string name, long largestExistingId)
    {
        var update = Builders<Counter>.Update.Max(counter => counter.SequenceValue, largestExistingId);
        try
        {
            await counters.UpdateOneAsync(
                counter => counter.Id == name, update, new UpdateOptions { IsUpsert = true });
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Code == 11000)
        {
            await counters.UpdateOneAsync(counter => counter.Id == name, update);
        }
    }
}

internal static class InventoryMath
{
    public static int Available(InventoryDocument? stock) =>
        stock is null ? 0 : Math.Max(0, (stock.QuantityOnHand ?? 0) - (stock.QuantityReserved ?? 0));
}
