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

    public async Task<DeliveryAddressResponse?> UpdateForUserAsync(
        long userId, long addressId, CreateDeliveryAddressRequest request)
    {
        var filter = Builders<DeliveryAddressDocument>.Filter.And(
            Builders<DeliveryAddressDocument>.Filter.Eq(address => address.UserId, userId),
            Builders<DeliveryAddressDocument>.Filter.Eq(address => address.AddressId, addressId));
        var update = Builders<DeliveryAddressDocument>.Update
            .Set(address => address.RecipientName, request.RecipientName!.Trim())
            .Set(address => address.RecipientPhone, request.RecipientPhone)
            .Set(address => address.AddressLine, request.AddressLine!.Trim())
            .Set(address => address.Ward, request.Ward!.Trim())
            .Set(address => address.District, request.District!.Trim())
            .Set(address => address.Province, request.Province!.Trim())
            .Set(address => address.IsDefault, request.IsDefault ?? false);
        var address = await deliveryAddresses.FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<DeliveryAddressDocument>
            {
                ReturnDocument = ReturnDocument.After
            });

        if (address is null)
        {
            return null;
        }

        if (request.IsDefault == true)
        {
            var otherDefaultsFilter = Builders<DeliveryAddressDocument>.Filter.And(
                Builders<DeliveryAddressDocument>.Filter.Eq(item => item.UserId, userId),
                Builders<DeliveryAddressDocument>.Filter.Ne(item => item.AddressId, addressId),
                Builders<DeliveryAddressDocument>.Filter.Eq(item => item.IsDefault, true));
            await deliveryAddresses.UpdateManyAsync(
                otherDefaultsFilter,
                Builders<DeliveryAddressDocument>.Update.Set(item => item.IsDefault, false));
        }

        return ToResponse(address);
    }

    public async Task<bool> DeleteForUserAsync(long userId, long addressId)
    {
        var filter = Builders<DeliveryAddressDocument>.Filter.And(
            Builders<DeliveryAddressDocument>.Filter.Eq(address => address.UserId, userId),
            Builders<DeliveryAddressDocument>.Filter.Eq(address => address.AddressId, addressId));
        var result = await deliveryAddresses.DeleteOneAsync(filter);
        return result.DeletedCount > 0;
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
