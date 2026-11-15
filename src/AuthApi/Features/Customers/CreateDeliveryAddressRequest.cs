namespace AuthApi.Features.Customers;

internal sealed record CreateDeliveryAddressRequest(
    string? RecipientName,
    string? RecipientPhone,
    string? AddressLine,
    string? Ward,
    string? District,
    string? Province,
    bool? IsDefault);
