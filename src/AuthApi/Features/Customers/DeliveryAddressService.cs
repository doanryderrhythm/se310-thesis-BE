using MongoDB.Driver;

namespace AuthApi.Features.Customers;

internal sealed class DeliveryAddressService(
    IMongoCollection<DeliveryAddressDocument> deliveryAddresses)
{
    public async Task<DeliveryAddressResponse[]> GetForUserAsync(long userId)
    {
        var addresses = await deliveryAddresses
            .Find(address => address.UserId == userId)
            .SortByDescending(address => address.IsDefault)
            .ThenBy(address => address.AddressId)
            .ToListAsync();

        return addresses.Select(address => new DeliveryAddressResponse(
            address.AddressId,
            address.RecipientName,
            address.RecipientPhone,
            address.AddressLine,
            address.Ward,
            address.District,
            address.Province,
            address.IsDefault,
            address.CreatedAt))
            .ToArray();
    }
}
