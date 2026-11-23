namespace AuthApi.Features.Orders;

internal sealed record OrderCustomerResponse(long? UserId, string? FullName, string? Phone);

internal sealed record AdminOrderListItem(
    long OrderId, string OrderCode, DateTime PlacedAt, OrderCustomerResponse Customer,
    string? RecipientName, string? RecipientPhone, int ItemCount, double TotalAmount,
    string? PaymentMethod, string? PaymentStatus, string Status);

internal sealed record AdminOrderItemResponse(
    long OrderItemId, long VariantId, long? ProductId, string? ProductName, string? Sku,
    string? Size, string? Color, int Quantity, double UnitPrice, double DiscountAmount, double LineTotal);

internal sealed record DeliveryAttemptResponse(int AttemptNumber, DateTime AttemptedAt, string Reason, string? Note);

internal sealed record OrderHistoryResponse(
    string? OldStatus, string NewStatus, string? ChangedBy, string? Note, DateTime ChangedAt);

internal sealed record AllowedTransitionResponse(
    string ToStatus, bool RequiresReason, bool RequiresFailureInfo, bool RequiresInspection);

internal sealed record AdminOrderDetail(
    long OrderId, string OrderCode, string Status, DateTime PlacedAt, DateTime UpdatedAt,
    DateTime? DeliveredAt, OrderCustomerResponse Customer,
    string? RecipientName, string? RecipientPhone, string? ShippingAddressLine, string? ShippingWard,
    string? ShippingDistrict, string? ShippingProvince, string? ShippingPostalCode,
    double Subtotal, double DiscountAmount, double ShippingFee, double TotalAmount,
    string? PaymentMethod, string? PaymentStatus, DateTime? PaidAt, DateTime? RefundedAt,
    string? CustomerNote, string? CancelReason, string? Carrier, string? TrackingCode,
    string? ReturnInspection, long? CouponId,
    AdminOrderItemResponse[] Items, DeliveryAttemptResponse[] DeliveryAttempts,
    OrderHistoryResponse[] History, AllowedTransitionResponse[] AllowedTransitions,
    bool CanConfirmPaid, bool CanConfirmRefunded, int MaxDeliveryAttempts);

internal sealed record OrderCountsResponse(Dictionary<string, long> ByStatus, OrderTabCounts Tabs);

/// <summary>Badges for the quick tabs: New / To pack / Shipping / Delivery failed / Returning.</summary>
internal sealed record OrderTabCounts(long New, long ToPack, long Shipping, long Failed, long Returning);

internal sealed record TransitionRequest(
    string? ToStatus, string? Note, string? Reason, string? FailureReason, DateTime? AttemptedAt,
    string? Inspection, string? Carrier, string? TrackingCode);

internal sealed record OrderPaymentRequest(string? Action, string? Reference);
