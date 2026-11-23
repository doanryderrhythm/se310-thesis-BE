namespace AuthApi.Features.Customers;

internal sealed record DeliveryAddressResponse(
    long AddressId,
    string? RecipientName,
    string? RecipientPhone,
    string? AddressLine,
    string? Ward,
    string? District,
    string? Province,
    bool? IsDefault,
    DateTime? CreatedAt,
    int? ProvinceCode = null,
    int? DistrictCode = null,
    int? WardCode = null);
