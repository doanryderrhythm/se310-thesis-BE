using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace AuthApi.Features.Orders;

internal static class OrderStatuses
{
    public const string Pending = "pending";
    public const string Confirmed = "confirmed";
    public const string Packing = "packing";
    public const string Shipping = "shipping";
    public const string Delivered = "delivered";
    public const string DeliveryFailed = "delivery_failed";
    public const string ReturningToSeller = "returning_to_seller";
    public const string ReturnedToSeller = "returned_to_seller";
    public const string Cancelled = "cancelled";

    public static readonly string[] All =
    [
        Pending, Confirmed, Packing, Shipping, Delivered, DeliveryFailed,
        ReturningToSeller, ReturnedToSeller, Cancelled
    ];
}

internal static class PaymentStatuses
{
    public const string Unpaid = "unpaid";
    public const string Pending = "pending";
    public const string Paid = "paid";
    public const string RefundPending = "refund_pending";
    public const string Refunded = "refunded";
}

internal static class PaymentMethods
{
    public const string Cod = "cod";
    public const string BankTransfer = "bank_transfer";
}

/// <summary>Shared by the admin order screens and the (future) customer checkout/orders API.</summary>
[BsonIgnoreExtraElements]
internal sealed class OrderDocument
{
    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("order_id")] public long OrderId { get; set; }
    [BsonElement("order_code")] public string OrderCode { get; set; } = string.Empty;
    [BsonElement("user_id")] public long? UserId { get; set; }
    [BsonElement("coupon_id")] public long? CouponId { get; set; }
    [BsonElement("status")] public string Status { get; set; } = OrderStatuses.Pending;
    [BsonElement("payment_method")] public string? PaymentMethod { get; set; }
    [BsonElement("payment_status")] public string? PaymentStatus { get; set; }
    [BsonElement("recipient_name")] public string? RecipientName { get; set; }
    [BsonElement("recipient_phone")] public string? RecipientPhone { get; set; }
    [BsonElement("shipping_address_line")] public string? ShippingAddressLine { get; set; }
    [BsonElement("shipping_ward")] public string? ShippingWard { get; set; }
    [BsonElement("shipping_district")] public string? ShippingDistrict { get; set; }
    [BsonElement("shipping_province")] public string? ShippingProvince { get; set; }
    [BsonElement("shipping_postal_code")] public string? ShippingPostalCode { get; set; }
    [BsonElement("shipping_province_code")] public int? ShippingProvinceCode { get; set; }
    [BsonElement("shipping_district_code")] public int? ShippingDistrictCode { get; set; }
    [BsonElement("shipping_ward_code")] public int? ShippingWardCode { get; set; }
    [BsonElement("subtotal")] public double Subtotal { get; set; }
    [BsonElement("discount_amount")] public double DiscountAmount { get; set; }
    [BsonElement("shipping_fee")] public double ShippingFee { get; set; }
    [BsonElement("total_amount")] public double TotalAmount { get; set; }
    [BsonElement("customer_note")] public string? CustomerNote { get; set; }
    [BsonElement("cancel_reason")] public string? CancelReason { get; set; }
    [BsonElement("idempotency_key")] [BsonIgnoreIfNull] public string? IdempotencyKey { get; set; }
    [BsonElement("placed_at")] public DateTime PlacedAt { get; set; }
    [BsonElement("updated_at")] public DateTime UpdatedAt { get; set; }
    [BsonElement("delivered_at")] public DateTime? DeliveredAt { get; set; }
    [BsonElement("paid_at")] public DateTime? PaidAt { get; set; }
    [BsonElement("refunded_at")] public DateTime? RefundedAt { get; set; }
    [BsonElement("carrier")] public string? Carrier { get; set; }
    [BsonElement("tracking_code")] public string? TrackingCode { get; set; }
    [BsonElement("delivery_attempts")] public List<DeliveryAttemptDocument> DeliveryAttempts { get; set; } = [];
    [BsonElement("return_inspection")] public string? ReturnInspection { get; set; }

    // Short-lived locks (token + time) so a status change or a return request is applied once,
    // even with concurrent requests. A lock older than two minutes is considered abandoned.
    [BsonElement("transition_token")] [BsonIgnoreIfNull] public string? TransitionToken { get; set; }
    [BsonElement("transition_locked_at")] [BsonIgnoreIfNull] public DateTime? TransitionLockedAt { get; set; }
    [BsonElement("return_lock_token")] [BsonIgnoreIfNull] public string? ReturnLockToken { get; set; }
    [BsonElement("return_locked_at")] [BsonIgnoreIfNull] public DateTime? ReturnLockedAt { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class DeliveryAttemptDocument
{
    [BsonElement("attempt_number")] public int AttemptNumber { get; set; }
    [BsonElement("attempted_at")] public DateTime AttemptedAt { get; set; }
    [BsonElement("reason")] public string Reason { get; set; } = string.Empty;
    [BsonElement("note")] public string? Note { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class OrderItemDocument
{
    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("order_item_id")] public long OrderItemId { get; set; }
    [BsonElement("order_id")] public long OrderId { get; set; }
    [BsonElement("variant_id")] public long VariantId { get; set; }
    [BsonElement("product_id")] public long? ProductId { get; set; }
    [BsonElement("product_name_snapshot")] public string? ProductNameSnapshot { get; set; }
    [BsonElement("sku_snapshot")] public string? SkuSnapshot { get; set; }
    [BsonElement("size_snapshot")] public string? SizeSnapshot { get; set; }
    [BsonElement("color_snapshot")] public string? ColorSnapshot { get; set; }
    [BsonElement("quantity")] public int Quantity { get; set; }
    [BsonElement("unit_price")] public double UnitPrice { get; set; }
    [BsonElement("discount_amount")] public double DiscountAmount { get; set; }
    [BsonElement("line_total")] public double LineTotal { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class OrderStatusHistoryDocument
{
    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("history_id")] public long HistoryId { get; set; }
    [BsonElement("order_id")] public long OrderId { get; set; }
    [BsonElement("old_status")] public string? OldStatus { get; set; }
    [BsonElement("new_status")] public string NewStatus { get; set; } = string.Empty;
    [BsonElement("changed_by")] public long? ChangedBy { get; set; }
    [BsonElement("note")] public string? Note { get; set; }
    [BsonElement("changed_at")] public DateTime ChangedAt { get; set; }
}
