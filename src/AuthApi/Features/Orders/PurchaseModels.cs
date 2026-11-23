using AuthApi.Features.Promotions;

namespace AuthApi.Features.Orders;

internal sealed record PurchaseRequest(
    long? AddressId,
    CouponItemRequest[]? Items,
    string? Code,
    string? CouponCode,
    long? UserCouponId,
    string? PaymentMethod,
    string? CustomerNote);

internal sealed record PurchaseItemResponse(
    long VariantId, long? ProductId, string? ProductName, string? Sku,
    string? Size, string? Color, int Quantity, double UnitPrice,
    double DiscountAmount, double LineTotal);

internal sealed record PurchaseResponse(
    long OrderId, string OrderCode, string Status,
    DateTime PlacedAt, double Subtotal, double DiscountAmount,
    double ShippingFee, double ShippingDiscount, double TotalAmount,
    string PaymentMethod, string PaymentStatus,
    string RecipientName, string RecipientPhone, string ShippingAddressLine,
    string? ShippingWard, string? ShippingDistrict, string? ShippingProvince,
    string? ShippingPostalCode, int? ShippingProvinceCode, int? ShippingDistrictCode,
    int? ShippingWardCode, PurchaseItemResponse[] Items);
