namespace AuthApi.Features.Purchasing;

// ---- Suppliers -----------------------------------------------------------------------------

internal sealed record SupplierRequest(
    string? Name, string? Phone, string? Email, string? Address, string? TaxCode, string? Note, string? Status);

internal sealed record SupplierResponse(
    long SupplierId, string Name, string? Phone, string? Email, string? Address, string? TaxCode, string? Note,
    string Status, long ReceiptCount, DateTime CreatedAt, DateTime UpdatedAt);

// ---- Goods receipts ------------------------------------------------------------------------------

internal sealed record ReceiptItemInput(long? VariantId, int? Quantity, double? UnitCost);

internal sealed record ReceiptRequest(
    long? SupplierId, string? SupplierInvoiceNo, DateTime? ReceivedAt, string? Note, ReceiptItemInput[]? Items);

internal sealed record CancelReceiptRequest(string? Reason);

internal sealed record ReceiptListItem(
    long ReceiptId, string ReceiptCode, long SupplierId, string? SupplierName, string? SupplierInvoiceNo,
    DateTime ReceivedAt, int ItemCount, int TotalQuantity, double TotalAmount, string Status, DateTime CreatedAt);

internal sealed record ReceiptItemResponse(
    long VariantId, long ProductId, string? ProductName, string? Sku, string? Size, string? Color,
    int Quantity, double UnitCost, double LineTotal, bool Applied, int? CurrentOnHand);

internal sealed record ReceiptDetail(
    long ReceiptId, string ReceiptCode, string Status, SupplierResponse? Supplier, string? SupplierInvoiceNo,
    DateTime ReceivedAt, string? Note, int TotalQuantity, double TotalAmount, ReceiptItemResponse[] Items,
    string? CreatedBy, DateTime CreatedAt, DateTime UpdatedAt, string? CompletedBy, DateTime? CompletedAt,
    DateTime? CancelledAt, string? CancelReason, bool CanEdit, bool CanComplete, bool CanCancel);
