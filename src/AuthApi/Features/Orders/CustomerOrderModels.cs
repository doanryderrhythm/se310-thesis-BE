namespace AuthApi.Features.Orders;

internal sealed record CustomerOrderPreviewItem(
    string? ProductName, string? ImageUrl, string? Size, string? Color, int Quantity);

internal sealed record CustomerOrderListItem(
    long OrderId, string OrderCode, string Status, DateTime PlacedAt, double TotalAmount, string? PaymentMethod,
    string? PaymentStatus, int ItemCount, CustomerOrderPreviewItem[] Preview, bool CanCancel);

internal sealed record CustomerOrderItem(
    long OrderItemId, long VariantId, long? ProductId, string? ProductName, string? ImageUrl, string? Sku,
    string? Size, string? Color, int Quantity, double UnitPrice, double DiscountAmount, double LineTotal, bool Reviewable);

internal sealed record CustomerTimelineEvent(string Status, string? Note, DateTime At);

internal sealed record BankTransferInfo(string? BankName, string AccountNumber, string? AccountName);

internal sealed record CustomerBankTransfer(string? BankName, string AccountNumber, string? AccountName, string TransferContent, double Amount);

internal sealed record CustomerOrderDetail(
    long OrderId, string OrderCode, string Status, DateTime PlacedAt, DateTime? DeliveredAt,
    string? RecipientName, string? RecipientPhone, string? AddressLine, string? Ward, string? District,
    string? Province, string? PostalCode, double Subtotal, double DiscountAmount, double ShippingFee, double TotalAmount,
    string? CouponCode, string? PaymentMethod, string? PaymentStatus, DateTime? PaidAt, string? CustomerNote,
    string? CancelReason, string? Carrier, string? TrackingCode, CustomerOrderItem[] Items,
    CustomerTimelineEvent[] Timeline, DeliveryAttemptResponse[] DeliveryAttempts,
    bool CanCancel, bool CanReturn, DateTime? ReturnDeadline, int ReturnWindowDays, CustomerBankTransfer? BankTransfer);

/// <summary>Counts for the tabs of "my orders"; <c>ByStatus</c> has every status.</summary>
internal sealed record CustomerOrderCounts(
    long All, long Active, long Shipping, long Delivered, long Cancelled, long DeliveryFailed,
    Dictionary<string, long> ByStatus);

internal sealed record CancelOrderRequest(string? Reason);

/// <summary>An order the customer can still act on: cancel it (not handed to the carrier yet) or return goods (delivered, inside the window).</summary>
internal sealed record ActionableOrder(
    long OrderId, string OrderCode, string Status, DateTime PlacedAt, DateTime? DeliveredAt, double TotalAmount,
    string? PaymentMethod, string? PaymentStatus, int ItemCount, CustomerOrderPreviewItem[] Preview,
    bool CanCancel, bool CanReturn, DateTime? ReturnDeadline, int ReturnableQuantity);

internal sealed record ActionableOrdersResponse(
    ActionableOrder[] Items, long CancellableCount, long ReturnableCount, int ReturnWindowDays);
