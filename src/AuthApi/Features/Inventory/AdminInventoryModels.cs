namespace AuthApi.Features.Inventory;

internal sealed record AdminInventoryRow(
    long VariantId, string? Sku, long ProductId, string? ProductName, string? Size, string? Color,
    string VariantStatus, int OnHand, int Reserved, int Available, int LowStockThreshold, bool IsLow,
    DateTime? UpdatedAt, double? AverageCost = null, double StockValue = 0);

/// <summary>
/// Manual stock decrease (damaged, lost, stocktake). Stock goes up only through goods receipts, so
/// <c>type</c> is optional and must be "decrease" when sent.
/// </summary>
internal sealed record AdjustmentRequest(string? Type, int? Quantity, string? Reason, string? ReasonCode = null);
internal sealed record ThresholdRequest(int? LowStockThreshold);

internal sealed record MovementResponse(
    long MovementId, string Type, int DeltaOnHand, int DeltaReserved, int OnHandAfter, int ReservedAfter,
    string? Reason, string? RefType, long? RefId, string? Actor, DateTime CreatedAt);
