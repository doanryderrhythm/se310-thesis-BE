namespace AuthApi.Features.Reports;

internal sealed record ReportRange(string From, string To, int Days);

internal sealed record ReportKpis(
    int OrderCount, int DeliveredCount, double GrossRevenue, double RefundAmount, double NetRevenue, int UnitsSold);

internal sealed record ReportPoint(
    string Period, int OrderCount, int DeliveredCount, double GrossRevenue, double RefundAmount,
    double NetRevenue, int UnitsSold);

internal sealed record TopProductRow(long? ProductId, string? ProductName, string? Sku, int Units, double Revenue);

internal sealed record LowStockRow(long VariantId, string? Sku, string? ProductName, string? Size, string? Color, int Available, int Threshold);

internal sealed record ReportResponse(
    ReportRange Range, string GroupBy, ReportKpis Kpis, ReportPoint[] Series, TopProductRow[] TopProducts,
    LowStockRow[] LowStock, Dictionary<string, string> Definitions);

internal sealed record DashboardCounters(
    long OrdersNeedingAction, long PendingOrders, long PendingReturns, long LowStockCount);

internal sealed record RecentOrderRow(string OrderCode, string? CustomerName, DateTime PlacedAt, double TotalAmount, string Status);

internal sealed record DashboardResponse(
    ReportRange Range, ReportKpis Kpis, DashboardCounters Counters, ReportPoint[] Series,
    RecentOrderRow[] RecentOrders, LowStockRow[] LowStock, Dictionary<string, string> Definitions);
