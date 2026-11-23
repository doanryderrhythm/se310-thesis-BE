namespace AuthApi.Features.Reports;

internal sealed record ReportRange(string From, string To, int Days);

internal sealed record ReportKpis(
    int OrderCount, int DeliveredCount, double GrossRevenue, double RefundAmount, double NetRevenue, int UnitsSold,
    double MerchandiseRevenue, double ShippingRevenue, double DiscountAmount, double AverageOrderValue,
    int CancelledCount);

/// <summary>
/// One bucket of the series. <c>Period</c> is the machine key (2026-11-23, 2026-W47, 2026-11, 2026-Q4, 2026),
/// <c>Start</c>/<c>End</c> are the full calendar span of the bucket (the first and last bucket of a range may
/// only be partly covered by the requested dates; only orders inside the range are counted).
/// </summary>
internal sealed record ReportPoint(
    string Period, string Label, string Start, string End,
    int OrderCount, int DeliveredCount, double GrossRevenue, double RefundAmount, double NetRevenue, int UnitsSold,
    double MerchandiseRevenue, double ShippingRevenue, double DiscountAmount, double AverageOrderValue,
    int CancelledCount);

internal sealed record TopProductRow(long? ProductId, string? ProductName, string? Sku, int Units, double Revenue);

internal sealed record LowStockRow(long VariantId, string? Sku, string? ProductName, string? Size, string? Color, int Available, int Threshold);

/// <summary>The previous period of the same length with the percentage change of the main figures.</summary>
internal sealed record ReportComparison(
    ReportRange PreviousRange, ReportKpis Previous, Dictionary<string, double?> ChangePercent);

internal sealed record ReportResponse(
    ReportRange Range, string GroupBy, ReportKpis Kpis, ReportComparison Comparison, ReportPoint[] Series,
    TopProductRow[] TopProducts, LowStockRow[] LowStock, Dictionary<string, string> Definitions);

internal sealed record DashboardCounters(
    long OrdersNeedingAction, long PendingOrders, long PendingReturns, long LowStockCount);

internal sealed record RecentOrderRow(string OrderCode, string? CustomerName, DateTime PlacedAt, double TotalAmount, string Status);

internal sealed record DashboardResponse(
    ReportRange Range, string GroupBy, ReportKpis Kpis, DashboardCounters Counters, ReportPoint[] Series,
    RecentOrderRow[] RecentOrders, LowStockRow[] LowStock, Dictionary<string, string> Definitions);
