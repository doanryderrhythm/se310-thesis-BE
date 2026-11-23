using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace AuthApi.Features.Purchasing;

internal static class ReceiptStatuses
{
    public const string Draft = "draft";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public static readonly string[] All = [Draft, Completed, Cancelled];
}

[BsonIgnoreExtraElements]
internal sealed class SupplierDocument
{
    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("supplier_id")] public long SupplierId { get; set; }
    [BsonElement("name")] public string Name { get; set; } = string.Empty;
    [BsonElement("phone")] [BsonIgnoreIfNull] public string? Phone { get; set; }
    [BsonElement("email")] [BsonIgnoreIfNull] public string? Email { get; set; }
    [BsonElement("address")] [BsonIgnoreIfNull] public string? Address { get; set; }
    [BsonElement("tax_code")] [BsonIgnoreIfNull] public string? TaxCode { get; set; }
    [BsonElement("note")] [BsonIgnoreIfNull] public string? Note { get; set; }
    [BsonElement("status")] public string Status { get; set; } = "active";
    [BsonElement("created_at")] public DateTime CreatedAt { get; set; }
    [BsonElement("updated_at")] public DateTime UpdatedAt { get; set; }
}

/// <summary>A line of a goods receipt. <c>Applied</c> marks lines whose stock was already added, so a retry never doubles them.</summary>
[BsonIgnoreExtraElements]
internal sealed class GoodsReceiptItemDocument
{
    [BsonElement("variant_id")] public long VariantId { get; set; }
    [BsonElement("product_id")] public long ProductId { get; set; }
    [BsonElement("product_name")] public string? ProductName { get; set; }
    [BsonElement("sku")] public string? Sku { get; set; }
    [BsonElement("size")] public string? Size { get; set; }
    [BsonElement("color")] public string? Color { get; set; }
    [BsonElement("quantity")] public int Quantity { get; set; }
    [BsonElement("unit_cost")] public double UnitCost { get; set; }
    [BsonElement("line_total")] public double LineTotal { get; set; }
    [BsonElement("applied")] public bool Applied { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class GoodsReceiptDocument
{
    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("receipt_id")] public long ReceiptId { get; set; }
    [BsonElement("receipt_code")] public string ReceiptCode { get; set; } = string.Empty;
    [BsonElement("supplier_id")] public long SupplierId { get; set; }
    [BsonElement("supplier_name")] public string? SupplierName { get; set; }
    [BsonElement("supplier_invoice_no")] [BsonIgnoreIfNull] public string? SupplierInvoiceNo { get; set; }
    [BsonElement("received_at")] public DateTime ReceivedAt { get; set; }
    [BsonElement("note")] [BsonIgnoreIfNull] public string? Note { get; set; }
    [BsonElement("status")] public string Status { get; set; } = ReceiptStatuses.Draft;
    [BsonElement("items")] public List<GoodsReceiptItemDocument> Items { get; set; } = [];
    [BsonElement("total_quantity")] public int TotalQuantity { get; set; }
    [BsonElement("total_amount")] public double TotalAmount { get; set; }
    [BsonElement("created_by")] public long? CreatedBy { get; set; }
    [BsonElement("created_at")] public DateTime CreatedAt { get; set; }
    [BsonElement("updated_at")] public DateTime UpdatedAt { get; set; }
    [BsonElement("completed_at")] [BsonIgnoreIfNull] public DateTime? CompletedAt { get; set; }
    [BsonElement("completed_by")] [BsonIgnoreIfNull] public long? CompletedBy { get; set; }
    [BsonElement("cancelled_at")] [BsonIgnoreIfNull] public DateTime? CancelledAt { get; set; }
    [BsonElement("cancel_reason")] [BsonIgnoreIfNull] public string? CancelReason { get; set; }

    // Short-lived lock while the stock is being added (stale after two minutes).
    [BsonElement("completion_token")] [BsonIgnoreIfNull] public string? CompletionToken { get; set; }
    [BsonElement("completion_locked_at")] [BsonIgnoreIfNull] public DateTime? CompletionLockedAt { get; set; }
}
