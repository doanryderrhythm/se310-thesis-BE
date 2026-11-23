namespace AuthApi.Features.Inventory;

internal sealed record AdminInventoryRow(
    long VariantId, string? Sku, long ProductId, string? ProductName, string? Size, string? Color,
    string VariantStatus, int OnHand, int Reserved, int Available, int LowStockThreshold, bool IsLow,
    DateTime? UpdatedAt);

internal sealed record AdjustmentRequest(string? Type, int? Quantity, string? Reason);
internal sealed record ThresholdRequest(int? LowStockThreshold);

internal sealed record MovementResponse(
    long MovementId, string Type, int DeltaOnHand, int DeltaReserved, int OnHandAfter, int ReservedAfter,
    string? Reason, string? RefType, long? RefId, string? Actor, DateTime CreatedAt);
