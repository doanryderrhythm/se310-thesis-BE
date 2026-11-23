using System.Text.RegularExpressions;
using AuthApi.Common;
using AuthApi.Features.Returns;
using AuthApi.Features.Reviews;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AuthApi.Features.Orders;

/// <summary>What a customer sees of their own orders. Cancelling goes through the admin state machine.</summary>
internal sealed class CustomerOrderService(IMongoDatabase db, AdminOrderService adminOrders, BankTransferInfo? bank)
{
    public static readonly string[] CustomerCancelStatuses = [OrderStatuses.Pending, OrderStatuses.Confirmed];

    /// <summary>Tab names of the "my orders" screen mapped to the statuses they show.</summary>
    private static readonly Dictionary<string, string[]> Tabs = new()
    {
        ["active"] = [OrderStatuses.Pending, OrderStatuses.Confirmed, OrderStatuses.Packing],
        ["shipping"] = [OrderStatuses.Shipping],
        ["delivered"] = [OrderStatuses.Delivered],
        ["cancelled"] = [OrderStatuses.Cancelled],
        ["delivery_failed"] = [OrderStatuses.DeliveryFailed, OrderStatuses.ReturningToSeller, OrderStatuses.ReturnedToSeller]
    };

    private readonly IMongoCollection<OrderDocument> orders = db.GetCollection<OrderDocument>("orders");
    private readonly IMongoCollection<OrderItemDocument> items = db.GetCollection<OrderItemDocument>("order_items");
    private readonly IMongoCollection<OrderStatusHistoryDocument> history = db.GetCollection<OrderStatusHistoryDocument>("order_status_history");
    private readonly IMongoCollection<ProductImageDocument> images = db.GetCollection<ProductImageDocument>("product_images");
    private readonly IMongoCollection<ProductDocument> products = db.GetCollection<ProductDocument>("products");
    private readonly IMongoCollection<ProductReviewDocument> reviews = db.GetCollection<ProductReviewDocument>("product_reviews");
    private readonly IMongoCollection<BsonDocument> coupons = db.GetCollection<BsonDocument>("coupons");

    public static bool IsStatusFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) || value == "all" || Tabs.ContainsKey(value)
        || value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(OrderStatuses.All.Contains);

    private static bool CanCancel(OrderDocument o) => CustomerCancelStatuses.Contains(o.Status);

    // ---- Reads --------------------------------------------------------------------------------------

    public async Task<PageResponse<CustomerOrderListItem>> ListAsync(
        long userId, string? status, string? q, DateTime? fromUtc, DateTime? toUtc, int page, int pageSize)
    {
        var b = Builders<OrderDocument>.Filter;
        var filter = b.Eq(o => o.UserId, userId);
        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            var statuses = Tabs.TryGetValue(status, out var tab)
                ? tab
                : status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            filter &= b.In(o => o.Status, statuses);
        }
        if (!string.IsNullOrWhiteSpace(q))
            filter &= b.Regex(o => o.OrderCode, new BsonRegularExpression(Regex.Escape(q.Trim()), "i"));
        if (fromUtc is { } from) filter &= b.Gte(o => o.PlacedAt, from);
        if (toUtc is { } to) filter &= b.Lt(o => o.PlacedAt, to);

        var total = await orders.CountDocumentsAsync(filter);
        var list = await orders.Find(filter).SortByDescending(o => o.PlacedAt).ThenByDescending(o => o.OrderId)
            .Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync();
        var lines = (await items.Find(Builders<OrderItemDocument>.Filter.In(i => i.OrderId, list.Select(o => o.OrderId))).ToListAsync())
            .OrderBy(i => i.OrderItemId).GroupBy(i => i.OrderId).ToDictionary(g => g.Key, g => g.ToList());
        var imageByProduct = await PrimaryImagesAsync(lines.Values.SelectMany(l => l));

        return new PageResponse<CustomerOrderListItem>(list.Select(o =>
        {
            var own = lines.GetValueOrDefault(o.OrderId, []);
            return new CustomerOrderListItem(o.OrderId, o.OrderCode, o.Status, o.PlacedAt, o.TotalAmount, o.PaymentMethod,
                o.PaymentStatus, own.Sum(i => i.Quantity),
                own.Take(3).Select(i => new CustomerOrderPreviewItem(i.ProductNameSnapshot, ImageOf(i, imageByProduct),
                    i.SizeSnapshot, i.ColorSnapshot, i.Quantity)).ToArray(), CanCancel(o));
        }).ToArray(), page, pageSize, total);
    }

    public async Task<CustomerOrderCounts> CountsAsync(long userId)
    {
        var rows = await orders.Aggregate().Match(o => o.UserId == userId)
            .Group<BsonDocument>(new BsonDocument { { "_id", "$status" }, { "n", new BsonDocument("$sum", 1) } }).ToListAsync();
        var byStatus = OrderStatuses.All.ToDictionary(s => s, _ => 0L);
        foreach (var row in rows) byStatus[row["_id"].AsString] = row["n"].ToInt64();
        long Sum(string tab) => Tabs[tab].Sum(s => byStatus[s]);
        return new CustomerOrderCounts(byStatus.Values.Sum(), Sum("active"), Sum("shipping"), Sum("delivered"),
            Sum("cancelled"), Sum("delivery_failed"), byStatus);
    }

    public async Task<CustomerOrderDetail?> GetAsync(long userId, string orderCode)
    {
        var order = await orders.Find(o => o.OrderCode == orderCode && o.UserId == userId).FirstOrDefaultAsync();
        return order is null ? null : await BuildDetailAsync(order);
    }

    private async Task<Dictionary<long, string>> PrimaryImagesAsync(IEnumerable<OrderItemDocument> lines)
    {
        var missing = lines.Where(i => i.ImageUrlSnapshot is null && i.ProductId is not null)
            .Select(i => i.ProductId!.Value).Distinct().ToArray();
        if (missing.Length == 0) return [];
        var list = await images.Find(Builders<ProductImageDocument>.Filter.In(i => i.ProductId, missing)).ToListAsync();
        return list.GroupBy(i => i.ProductId).ToDictionary(g => g.Key,
            g => g.OrderBy(i => i.SortOrder ?? int.MaxValue).ThenBy(i => i.ImageId).First().ImageUrl ?? "");
    }

    private static string? ImageOf(OrderItemDocument i, Dictionary<long, string> fallback) =>
        i.ImageUrlSnapshot ?? (i.ProductId is { } pid && fallback.TryGetValue(pid, out var url) && url.Length > 0 ? url : null);

    private async Task<CustomerOrderDetail> BuildDetailAsync(OrderDocument o)
    {
        var lines = await items.Find(i => i.OrderId == o.OrderId).SortBy(i => i.OrderItemId).ToListAsync();
        var imageByProduct = await PrimaryImagesAsync(lines);
        var events = await history.Find(h => h.OrderId == o.OrderId).SortBy(h => h.ChangedAt).ThenBy(h => h.HistoryId).ToListAsync();

        // Only real status changes; payment bookkeeping notes (bank references) stay internal.
        var timeline = new List<CustomerTimelineEvent>();
        if (!events.Any(e => e.NewStatus == OrderStatuses.Pending))
            timeline.Add(new CustomerTimelineEvent(OrderStatuses.Pending, null, o.PlacedAt));
        timeline.AddRange(events.Where(e => e.OldStatus != e.NewStatus)
            .Select(e => new CustomerTimelineEvent(e.NewStatus, e.NewStatus == OrderStatuses.Cancelled ? e.Note : null, e.ChangedAt)));

        // Products this customer may still review (delivered order, product still sold, not reviewed yet).
        var reviewedProducts = new HashSet<long>();
        if (o.Status == OrderStatuses.Delivered)
        {
            var productIds = lines.Where(i => i.ProductId is not null).Select(i => i.ProductId!.Value).Distinct().ToArray();
            reviewedProducts = (await reviews.Find(r => r.UserId == o.UserId && productIds.Contains(r.ProductId))
                .Project(r => r.ProductId).ToListAsync()).ToHashSet();
            var active = (await products.Find(Builders<ProductDocument>.Filter.In(p => p.ProductId, productIds)
                & Builders<ProductDocument>.Filter.Eq(p => p.Status, "active")).Project(p => p.ProductId).ToListAsync()).ToHashSet();
            reviewedProducts.UnionWith(productIds.Where(id => !active.Contains(id)));
        }

        string? couponCode = null;
        if (o.CouponId is { } couponId)
        {
            var coupon = await coupons.Find(Builders<BsonDocument>.Filter.Eq("coupon_id", couponId)).FirstOrDefaultAsync();
            couponCode = coupon?.GetValue("code", BsonNull.Value) is { IsString: true } code ? code.AsString : null;
        }

        var deadline = o.Status == OrderStatuses.Delivered ? o.DeliveredAt?.AddDays(ReturnService.ReturnWindowDays) : null;
        var transferDue = bank is not null && o.PaymentMethod == PaymentMethods.BankTransfer
            && o.PaymentStatus == PaymentStatuses.Pending && o.Status != OrderStatuses.Cancelled;

        return new CustomerOrderDetail(
            o.OrderId, o.OrderCode, o.Status, o.PlacedAt, o.DeliveredAt, o.RecipientName, o.RecipientPhone,
            o.ShippingAddressLine, o.ShippingWard, o.ShippingDistrict, o.ShippingProvince, o.ShippingPostalCode,
            o.Subtotal, o.DiscountAmount, o.ShippingFee, o.TotalAmount, couponCode, o.PaymentMethod, o.PaymentStatus,
            o.PaidAt, o.CustomerNote, o.CancelReason, o.Carrier, o.TrackingCode,
            lines.Select(i => new CustomerOrderItem(i.OrderItemId, i.VariantId, i.ProductId, i.ProductNameSnapshot,
                ImageOf(i, imageByProduct), i.SkuSnapshot, i.SizeSnapshot, i.ColorSnapshot, i.Quantity, i.UnitPrice,
                i.DiscountAmount, i.LineTotal,
                o.Status == OrderStatuses.Delivered && i.ProductId is { } pid && !reviewedProducts.Contains(pid))).ToArray(),
            [.. timeline],
            o.DeliveryAttempts.Select(a => new DeliveryAttemptResponse(a.AttemptNumber, a.AttemptedAt, a.Reason, a.Note)).ToArray(),
            CanCancel(o), deadline is { } d && d >= DateTime.UtcNow, deadline, ReturnService.ReturnWindowDays,
            transferDue ? new CustomerBankTransfer(bank!.BankName, bank.AccountNumber, bank.AccountName, o.OrderCode, o.TotalAmount) : null);
    }

    // ---- Cancel --------------------------------------------------------------------------------------

    public async Task<(CustomerOrderDetail? Detail, IResult? Error)> CancelAsync(long userId, string orderCode, string? reason)
    {
        var (_, error) = await adminOrders.TransitionAsync(userId, orderCode,
            new TransitionRequest(OrderStatuses.Cancelled, null, reason, null, null, null, null, null),
            onlyOwnedBy: userId, onlyFrom: CustomerCancelStatuses);
        if (error is not null) return (null, error);
        return (await GetAsync(userId, orderCode), null);
    }
}
