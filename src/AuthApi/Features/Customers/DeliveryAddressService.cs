using MongoDB.Driver;

namespace AuthApi.Features.Customers;

internal sealed class DeliveryAddressService(
    IMongoCollection<DeliveryAddressDocument> deliveryAddresses,
    IMongoCollection<Counter> counters)
{
    public async Task<DeliveryAddressResponse> CreateForUserAsync(long userId, NormalizedAddress input)
    {
        var address = new DeliveryAddressDocument
        {
            AddressId = await NextAddressIdAsync(),
            UserId = userId,
            RecipientName = input.RecipientName,
            RecipientPhone = input.RecipientPhone,
            AddressLine = input.AddressLine,
            Ward = input.Location.Ward,
            District = input.Location.District,
            Province = input.Location.Province,
            WardCode = input.Location.WardCode,
            DistrictCode = input.Location.DistrictCode,
            ProvinceCode = input.Location.ProvinceCode,
            IsDefault = input.IsDefault,
            CreatedAt = DateTime.UtcNow
        };

        await deliveryAddresses.InsertOneAsync(address);
        if (input.IsDefault) await ClearOtherDefaultsAsync(userId, address.AddressId);
        return ToResponse(address);
    }

    private Task ClearOtherDefaultsAsync(long userId, long keepAddressId) =>
        deliveryAddresses.UpdateManyAsync(
            Builders<DeliveryAddressDocument>.Filter.And(
                Builders<DeliveryAddressDocument>.Filter.Eq(item => item.UserId, userId),
                Builders<DeliveryAddressDocument>.Filter.Ne(item => item.AddressId, keepAddressId),
                Builders<DeliveryAddressDocument>.Filter.Eq(item => item.IsDefault, true)),
            Builders<DeliveryAddressDocument>.Update.Set(item => item.IsDefault, false));

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
        long userId, long addressId, NormalizedAddress input)
    {
        var filter = Builders<DeliveryAddressDocument>.Filter.And(
            Builders<DeliveryAddressDocument>.Filter.Eq(address => address.UserId, userId),
            Builders<DeliveryAddressDocument>.Filter.Eq(address => address.AddressId, addressId));
        var update = Builders<DeliveryAddressDocument>.Update
            .Set(address => address.RecipientName, input.RecipientName)
            .Set(address => address.RecipientPhone, input.RecipientPhone)
            .Set(address => address.AddressLine, input.AddressLine)
            .Set(address => address.Ward, input.Location.Ward)
            .Set(address => address.District, input.Location.District)
            .Set(address => address.Province, input.Location.Province)
            .Set(address => address.WardCode, input.Location.WardCode)
            .Set(address => address.DistrictCode, input.Location.DistrictCode)
            .Set(address => address.ProvinceCode, input.Location.ProvinceCode)
            .Set(address => address.IsDefault, input.IsDefault);
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

        if (input.IsDefault) await ClearOtherDefaultsAsync(userId, addressId);

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
            address.CreatedAt,
            address.ProvinceCode,
            address.DistrictCode,
            address.WardCode);
}
