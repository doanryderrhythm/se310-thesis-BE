using System.Globalization;
using System.Text;
using AuthApi.Common;
using AuthApi.Features.Orders;
using AuthApi.Features.Products;
using AuthApi.Features.Returns;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AuthApi.Features.Reports;

/// <summary>
/// Numbers for the admin dashboard and reports. Days are Vietnam local days (UTC+7).
/// </summary>
internal sealed class ReportService(IMongoDatabase db)
{
    private const int DefaultThreshold = 5;
    private readonly IMongoCollection<OrderDocument> orders = db.GetCollection<OrderDocument>("orders");
    private readonly IMongoCollection<OrderItemDocument> orderItems = db.GetCollection<OrderItemDocument>("order_items");
    private readonly IMongoCollection<CustomerReturnDocument> returns = db.GetCollection<CustomerReturnDocument>("returns");
    private readonly IMongoCollection<InventoryDocument> inventory = db.GetCollection<InventoryDocument>("inventory");
    private readonly IMongoCollection<ProductVariantDocument> variants = db.GetCollection<ProductVariantDocument>("product_variants");
    private readonly IMongoCollection<ProductDocument> products = db.GetCollection<ProductDocument>("products");
    private readonly IMongoCollection<SizeDocument> sizes = db.GetCollection<SizeDocument>("sizes");
    private readonly IMongoCollection<ColorDocument> colors = db.GetCollection<ColorDocument>("colors");
    private readonly IMongoCollection<User> users = db.GetCollection<User>("users");

    public static readonly Dictionary<string, string> Definitions = new()
    {
        ["orderCount"] = "Số đơn đặt trong khoảng thời gian (mọi trạng thái, theo ngày đặt).",
        ["deliveredCount"] = "Số đơn đã giao thành công trong khoảng (theo ngày giao).",
        ["grossRevenue"] = "Tổng tiền (đã trừ giảm giá, gồm phí vận chuyển) của các đơn đã giao trong khoảng, theo ngày giao.",
        ["refundAmount"] = "Tổng tiền đã hoàn cho các yêu cầu trả hàng có ngày hoàn nằm trong khoảng. Hoàn tiền đơn bị hủy hoặc hoàn về shop không trừ vì các đơn đó không tính vào doanh thu.",
        ["netRevenue"] = "Doanh thu thuần = doanh thu gộp − tiền hoàn.",
        ["unitsSold"] = "Tổng số sản phẩm trong các đơn đã giao trong khoảng.",
        ["lowStock"] = "Biến thể đang bán có số khả dụng (tồn − đang giữ) nhỏ hơn hoặc bằng ngưỡng cảnh báo.",
        ["timezone"] = "Ngày được tính theo giờ Việt Nam (UTC+7); tuần bắt đầu từ thứ Hai."
    };

    public const int MaxDays = 366;

    public static ReportRange ToRange(DateOnly from, DateOnly to) =>
        new(from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            to.DayNumber - from.DayNumber + 1);

    // ---- Report ---------------------------------------------------------------------------------

    public async Task<ReportResponse> ReportAsync(DateOnly from, DateOnly to, bool weekly)
    {
        var (kpis, series, topIds) = await ComputeAsync(from, to, weekly);
        var topProducts = await TopProductsAsync(topIds, 10);
        return new ReportResponse(ToRange(from, to), weekly ? "week" : "day", kpis, series, topProducts,
            (await LowStockAsync(20)).Rows, Definitions);
    }

    public async Task<DashboardResponse> DashboardAsync(DateOnly from, DateOnly to)
    {
        var (kpis, series, _) = await ComputeAsync(from, to, false);
        var lowStock = await LowStockAsync(5);
        var byStatus = (await orders.Aggregate()
            .Group<BsonDocument>(new BsonDocument { { "_id", "$status" }, { "n", new BsonDocument("$sum", 1) } }).ToListAsync())
            .ToDictionary(r => r["_id"].AsString, r => r["n"].ToInt64());
        long Count(string s) => byStatus.GetValueOrDefault(s);
        var recent = await orders.Find(Builders<OrderDocument>.Filter.Empty).SortByDescending(o => o.PlacedAt).Limit(5).ToListAsync();
        var names = (await users.Find(Builders<User>.Filter.In(u => u.UserId, recent.Where(o => o.UserId is not null).Select(o => o.UserId!.Value)))
            .ToListAsync()).ToDictionary(u => u.UserId, u => u.FullName);
        return new DashboardResponse(ToRange(from, to), kpis,
            new DashboardCounters(Count(OrderStatuses.Pending) + Count(OrderStatuses.Confirmed) + Count(OrderStatuses.Packing),
                Count(OrderStatuses.Pending), await returns.CountDocumentsAsync(r => r.Status == ReturnStatuses.Requested), lowStock.Total),
            series,
            recent.Select(o => new RecentOrderRow(o.OrderCode, o.UserId is { } id ? names.GetValueOrDefault(id) : null,
                o.PlacedAt, o.TotalAmount, o.Status)).ToArray(),
            lowStock.Rows, Definitions);
    }

    /// <summary>CSV of the same daily/weekly series shown on the report screen (UTF-8 with BOM for Excel).</summary>
    public async Task<byte[]> ExportCsvAsync(DateOnly from, DateOnly to, bool weekly)
    {
        var (kpis, series, _) = await ComputeAsync(from, to, weekly);
        var text = new StringBuilder("period,orders,delivered_orders,gross_revenue,refund_amount,net_revenue,units_sold\r\n");
        foreach (var p in series)
            text.Append(string.Join(',', p.Period, p.OrderCount, p.DeliveredCount, Money(p.GrossRevenue), Money(p.RefundAmount), Money(p.NetRevenue), p.UnitsSold)).Append("\r\n");
        text.Append(string.Join(',', "TOTAL", kpis.OrderCount, kpis.DeliveredCount, Money(kpis.GrossRevenue), Money(kpis.RefundAmount), Money(kpis.NetRevenue), kpis.UnitsSold)).Append("\r\n");
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    private static string Money(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    // ---- Core computation ----------------------------------------------------------------------------

    private async Task<(ReportKpis Kpis, ReportPoint[] Series, List<long> DeliveredOrderIds)> ComputeAsync(
        DateOnly from, DateOnly to, bool weekly)
    {
        var fromUtc = VnTime.StartOfDayUtc(from);
        var toUtc = VnTime.StartOfDayUtc(to.AddDays(1));

        var placed = await orders.Find(o => o.PlacedAt >= fromUtc && o.PlacedAt < toUtc).Project(o => o.PlacedAt).ToListAsync();
        var delivered = await orders.Find(o => o.Status == OrderStatuses.Delivered && o.DeliveredAt >= fromUtc && o.DeliveredAt < toUtc)
            .Project(o => new { o.OrderId, o.DeliveredAt, o.TotalAmount }).ToListAsync();
        var deliveredIds = delivered.Select(o => o.OrderId).ToList();
        var unitsByOrder = (await orderItems.Aggregate()
            .Match(Builders<OrderItemDocument>.Filter.In(i => i.OrderId, deliveredIds))
            .Group<BsonDocument>(new BsonDocument { { "_id", "$order_id" }, { "n", new BsonDocument("$sum", "$quantity") } }).ToListAsync())
            .ToDictionary(r => r["_id"].ToInt64(), r => r["n"].ToInt32());
        var refunds = await returns.Find(r => r.Status == ReturnStatuses.Refunded && r.RefundedAt >= fromUtc && r.RefundedAt < toUtc)
            .Project(r => new { r.RefundedAt, r.RefundAmount }).ToListAsync();

        var points = new Dictionary<string, (int Orders, int Delivered, double Gross, double Refund, int Units)>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var key = VnTime.Key(VnTime.StartOfDayUtc(day), weekly);
            points.TryAdd(key, (0, 0, 0, 0, 0));
        }

        void Add(string key, int orders_ = 0, int delivered_ = 0, double gross = 0, double refund = 0, int units = 0)
        {
            var p = points[key];
            points[key] = (p.Orders + orders_, p.Delivered + delivered_, p.Gross + gross, p.Refund + refund, p.Units + units);
        }

        foreach (var at in placed) Add(VnTime.Key(at, weekly), orders_: 1);
        foreach (var o in delivered)
            Add(VnTime.Key(o.DeliveredAt!.Value, weekly), delivered_: 1, gross: o.TotalAmount, units: unitsByOrder.GetValueOrDefault(o.OrderId));
        foreach (var r in refunds) Add(VnTime.Key(r.RefundedAt!.Value, weekly), refund: r.RefundAmount ?? 0);

        var series = points.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new ReportPoint(p.Key, p.Value.Orders, p.Value.Delivered, p.Value.Gross, p.Value.Refund,
                p.Value.Gross - p.Value.Refund, p.Value.Units)).ToArray();
        var kpis = new ReportKpis(series.Sum(p => p.OrderCount), series.Sum(p => p.DeliveredCount), series.Sum(p => p.GrossRevenue),
            series.Sum(p => p.RefundAmount), series.Sum(p => p.NetRevenue), series.Sum(p => p.UnitsSold));
        return (kpis, series, deliveredIds);
    }

    private async Task<TopProductRow[]> TopProductsAsync(List<long> deliveredOrderIds, int take)
    {
        var rows = await orderItems.Aggregate()
            .Match(Builders<OrderItemDocument>.Filter.In(i => i.OrderId, deliveredOrderIds))
            .Group<BsonDocument>(new BsonDocument
            {
                { "_id", "$sku_snapshot" },
                { "name", new BsonDocument("$first", "$product_name_snapshot") },
                { "productId", new BsonDocument("$first", "$product_id") },
                { "units", new BsonDocument("$sum", "$quantity") },
                { "revenue", new BsonDocument("$sum", "$line_total") }
            })
            .Sort(new BsonDocument { { "units", -1 }, { "revenue", -1 } })
            .Limit(take)
            .ToListAsync();
        return rows.Select(r => new TopProductRow(
            r["productId"].IsBsonNull ? null : r["productId"].ToInt64(),
            r["name"].IsBsonNull ? null : r["name"].AsString,
            r["_id"].IsBsonNull ? null : r["_id"].AsString,
            r["units"].ToInt32(), r["revenue"].ToDouble())).ToArray();
    }

    private async Task<(LowStockRow[] Rows, long Total)> LowStockAsync(int take)
    {
        var active = await variants.Find(v => v.Status == "active").ToListAsync();
        var ids = active.Select(v => v.VariantId).ToArray();
        var stock = await inventory.Find(Builders<InventoryDocument>.Filter.In(i => i.VariantId, ids)).ToListAsync();
        var low = stock.Select(i => (Row: i, Available: Math.Max(0, (i.QuantityOnHand ?? 0) - (i.QuantityReserved ?? 0)),
                Threshold: i.LowStockThreshold ?? DefaultThreshold))
            .Where(x => x.Available <= x.Threshold)
            .OrderBy(x => x.Available).ThenBy(x => x.Row.VariantId).ToList();
        var top = low.Take(take).ToList();

        var byVariant = active.ToDictionary(v => v.VariantId);
        var ps = (await products.Find(Builders<ProductDocument>.Filter.In(p => p.ProductId, top.Select(x => byVariant[x.Row.VariantId].ProductId).Distinct())).ToListAsync())
            .ToDictionary(p => p.ProductId, p => p.Name);
        var ss = (await sizes.Find(Builders<SizeDocument>.Filter.In(s => s.SizeId, top.Select(x => byVariant[x.Row.VariantId].SizeId).Distinct())).ToListAsync())
            .ToDictionary(s => s.SizeId, s => s.Name);
        var cs = (await colors.Find(Builders<ColorDocument>.Filter.In(c => c.ColorId, top.Select(x => byVariant[x.Row.VariantId].ColorId).Distinct())).ToListAsync())
            .ToDictionary(c => c.ColorId, c => c.Name);
        return (top.Select(x =>
        {
            var v = byVariant[x.Row.VariantId];
            return new LowStockRow(v.VariantId, v.Sku, ps.GetValueOrDefault(v.ProductId), ss.GetValueOrDefault(v.SizeId),
                cs.GetValueOrDefault(v.ColorId), x.Available, x.Threshold);
        }).ToArray(), low.Count);
    }
}
