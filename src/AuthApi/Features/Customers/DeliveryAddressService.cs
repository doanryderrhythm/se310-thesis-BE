using MongoDB.Driver;

namespace AuthApi.Features.Customers;

internal sealed class DeliveryAddressService(
    IMongoCollection<DeliveryAddressDocument> deliveryAddresses,
    IMongoCollection<Counter> counters)
{
    public async Task<DeliveryAddressResponse> CreateForUserAsync(
        long userId, CreateDeliveryAddressRequest request)
    {
        var address = new DeliveryAddressDocument
        {
            AddressId = await NextAddressIdAsync(),
            UserId = userId,
            RecipientName = request.RecipientName!.Trim(),
            RecipientPhone = request.RecipientPhone,
            AddressLine = request.AddressLine!.Trim(),
            Ward = request.Ward!.Trim(),
            District = request.District!.Trim(),
            Province = request.Province!.Trim(),
            IsDefault = request.IsDefault ?? false,
            CreatedAt = DateTime.UtcNow
        };

        await deliveryAddresses.InsertOneAsync(address);
        return ToResponse(address);
    }

    public async Task<DeliveryAddressResponse[]> GetForUserAsync(long userId)
    {
        var addresses = await deliveryAddresses
            .Find(address => address.UserId == userId)
            .SortByDescending(address => address.IsDefault)
            .ThenBy(address => address.AddressId)
            .ToListAsync();

        return addresses.Select(ToResponse).ToArray();
    }

    private async Task<long> NextAddressIdAsync()
    {
        var counter = await counters.FindOneAndUpdateAsync(
            item => item.Id == "addresses",
            Builders<Counter>.Update.Inc(item => item.SequenceValue, 1),
            new FindOneAndUpdateOptions<Counter>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After
            });
        return counter.SequenceValue;
    }

    private static DeliveryAddressResponse ToResponse(DeliveryAddressDocument address) =>
        new(
            address.AddressId,
            address.RecipientName,
            address.RecipientPhone,
            address.AddressLine,
            address.Ward,
            address.District,
            address.Province,
            address.IsDefault,
            address.CreatedAt);
}
