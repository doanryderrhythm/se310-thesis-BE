using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace AuthApi.Features.Returns;

internal static class ReturnStatuses
{
    public const string Requested = "requested";
    public const string AwaitingReturn = "awaiting_return";
    public const string Received = "received";
    public const string RefundPending = "refund_pending";
    public const string Refunded = "refunded";
    public const string Rejected = "rejected";

    public static readonly string[] All = [Requested, AwaitingReturn, Received, RefundPending, Refunded, Rejected];
}

[BsonIgnoreExtraElements]
internal sealed class CustomerReturnDocument
{
    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("return_id")] public long ReturnId { get; set; }
    [BsonElement("return_code")] public string ReturnCode { get; set; } = string.Empty;
    [BsonElement("order_id")] public long OrderId { get; set; }
    [BsonElement("order_code")] public string OrderCode { get; set; } = string.Empty;
    [BsonElement("user_id")] public long UserId { get; set; }
    [BsonElement("status")] public string Status { get; set; } = ReturnStatuses.Requested;
    [BsonElement("reason")] public string Reason { get; set; } = string.Empty;
    [BsonElement("description")] public string? Description { get; set; }
    [BsonElement("items")] public List<ReturnItemDocument> Items { get; set; } = [];
    [BsonElement("estimated_refund")] public double EstimatedRefund { get; set; }
    [BsonElement("refund_amount")] public double? RefundAmount { get; set; }
    [BsonElement("refund_reference")] public string? RefundReference { get; set; }
    [BsonElement("reject_reason")] public string? RejectReason { get; set; }
    [BsonElement("assignee_id")] public long? AssigneeId { get; set; }
    [BsonElement("requested_at")] public DateTime RequestedAt { get; set; }
    [BsonElement("updated_at")] public DateTime UpdatedAt { get; set; }
    [BsonElement("refunded_at")] public DateTime? RefundedAt { get; set; }
    [BsonElement("events")] public List<ReturnEventDocument> Events { get; set; } = [];
}

[BsonIgnoreExtraElements]
internal sealed class ReturnItemDocument
{
    [BsonElement("order_item_id")] public long OrderItemId { get; set; }
    [BsonElement("variant_id")] public long VariantId { get; set; }
    [BsonElement("product_name_snapshot")] public string? ProductName { get; set; }
    [BsonElement("sku_snapshot")] public string? Sku { get; set; }
    [BsonElement("size_snapshot")] public string? Size { get; set; }
    [BsonElement("color_snapshot")] public string? Color { get; set; }
    [BsonElement("quantity")] public int Quantity { get; set; }
    [BsonElement("unit_price")] public double UnitPrice { get; set; }
    [BsonElement("allocated_discount")] public double AllocatedDiscount { get; set; }
    [BsonElement("estimated_refund")] public double EstimatedRefund { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class ReturnEventDocument
{
    [BsonElement("status")] public string Status { get; set; } = string.Empty;
    [BsonElement("note")] public string? Note { get; set; }
    [BsonElement("actor_id")] public long? ActorId { get; set; }
    [BsonElement("at")] public DateTime At { get; set; }
}

// ---- Requests --------------------------------------------------------------------------------

internal sealed record ReturnItemInput(long? OrderItemId, int? Quantity);
internal sealed record CreateReturnRequest(string? OrderCode, string? Reason, string? Description, ReturnItemInput[]? Items);
internal sealed record ReturnTransitionRequest(string? Action, string? Reason, string? Note, string? RefundReference);

// ---- Responses -------------------------------------------------------------------------------

internal sealed record ReturnItemResponse(
    long OrderItemId, string? ProductName, string? Sku, string? Size, string? Color, int Quantity,
    double UnitPrice, double AllocatedDiscount, double EstimatedRefund);

internal sealed record ReturnEventResponse(string Status, string? Note, string? Actor, DateTime At);
internal sealed record ReturnCustomerResponse(long UserId, string? FullName, string? Phone);
internal sealed record ReturnActionOption(string Action, bool RequiresReason, bool RequiresRefundReference);

internal sealed record ReturnDetailResponse(
    long ReturnId, string ReturnCode, string OrderCode, string Status, string Reason, string? Description,
    ReturnItemResponse[] Items, double EstimatedRefund, double? RefundAmount, string? RefundReference,
    string? RejectReason, DateTime RequestedAt, DateTime UpdatedAt, DateTime? RefundedAt,
    ReturnEventResponse[] Events, ReturnCustomerResponse? Customer, ReturnActionOption[] AllowedActions);

internal sealed record ReturnListItem(
    long ReturnId, string ReturnCode, string OrderCode, string Status, int ItemCount, double EstimatedRefund,
    DateTime RequestedAt, int WaitingDays, ReturnCustomerResponse Customer);

internal sealed record ReturnCountsResponse(Dictionary<string, long> ByStatus);

internal sealed record ReturnableItem(
    long OrderItemId, string? ProductName, string? Sku, string? Size, string? Color, int PurchasedQuantity,
    int ReturnedQuantity, int RemainingQuantity, double UnitPrice, double RefundPerUnit);

internal sealed record ReturnableOrderResponse(
    string OrderCode, DateTime? DeliveredAt, DateTime? ReturnDeadline, bool Eligible, string? IneligibleReason,
    ReturnableItem[] Items);
