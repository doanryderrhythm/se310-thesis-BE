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
    DateTime? CreatedAt);
