using System.Text.RegularExpressions;
using AuthApi.Common;
using AuthApi.Data;
using AuthApi.Features.Inventory;
using AuthApi.Features.Notifications;
using AuthApi.Features.Orders;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AuthApi.Features.Returns;

internal sealed class ReturnService(
    IMongoDatabase db, CounterService counters, InventoryService stock, NotificationService notifications, ILogger logger)
{
    public const int ReturnWindowDays = 7;
    public static readonly string[] Reasons = ["wrong_size", "wrong_item", "defective", "changed_mind", "other"];

    private readonly IMongoCollection<CustomerReturnDocument> returns = db.GetCollection<CustomerReturnDocument>("returns");
    private readonly IMongoCollection<OrderDocument> orders = db.GetCollection<OrderDocument>("orders");
    private readonly IMongoCollection<OrderItemDocument> orderItems = db.GetCollection<OrderItemDocument>("order_items");
    private readonly IMongoCollection<User> users = db.GetCollection<User>("users");

    public static async Task SetupAsync(IMongoDatabase db, CounterService counters)
    {
        var returns = db.GetCollection<CustomerReturnDocument>("returns");
        await returns.Indexes.CreateOneAsync(new CreateIndexModel<CustomerReturnDocument>(
            Builders<CustomerReturnDocument>.IndexKeys.Ascending(r => r.ReturnCode),
            new CreateIndexOptions { Unique = true, Name = "return_code_unique" }));
        await returns.Indexes.CreateOneAsync(new CreateIndexModel<CustomerReturnDocument>(
            Builders<CustomerReturnDocument>.IndexKeys.Ascending(r => r.UserId).Descending(r => r.RequestedAt),
            new CreateIndexOptions { Name = "user_requested" }));
        await returns.Indexes.CreateOneAsync(new CreateIndexModel<CustomerReturnDocument>(
            Builders<CustomerReturnDocument>.IndexKeys.Ascending(r => r.Status).Descending(r => r.RequestedAt),
            new CreateIndexOptions { Name = "status_requested" }));
        await IdCounters.SyncAsync(db, counters, "returns", "returns", "return_id");
    }

    // ---- Customer side ----------------------------------------------------------------------------

    public async Task<ReturnableOrderResponse?> ReturnableAsync(long userId, string orderCode)
    {
        var order = await orders.Find(o => o.OrderCode == orderCode && o.UserId == userId).FirstOrDefaultAsync();
        if (order is null) return null;
        var lines = await orderItems.Find(i => i.OrderId == order.OrderId).SortBy(i => i.OrderItemId).ToListAsync();
        var already = await ReturnedQuantitiesAsync(order.OrderId);
        var deliveredAt = order.DeliveredAt ?? (order.Status == OrderStatuses.Delivered ? order.UpdatedAt : null);
        var deadline = deliveredAt?.AddDays(ReturnWindowDays);
        string? why = null;
        if (order.Status != OrderStatuses.Delivered) why = "Only delivered orders can be returned.";
        else if (deadline < DateTime.UtcNow) why = $"The {ReturnWindowDays}-day return window has ended.";

        return new ReturnableOrderResponse(order.OrderCode, deliveredAt, deadline, why is null, why,
            lines.Select(i =>
            {
                var returned = already.GetValueOrDefault(i.OrderItemId);
                return new ReturnableItem(i.OrderItemId, i.ProductNameSnapshot, i.SkuSnapshot, i.SizeSnapshot, i.ColorSnapshot,
                    i.Quantity, returned, Math.Max(0, i.Quantity - returned), i.UnitPrice, RefundFor(i, 1));
            }).ToArray());
    }

    public async Task<(ReturnDetailResponse? Return, IResult? Error)> CreateAsync(long userId, CreateReturnRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        if (r.Reason is null || !Reasons.Contains(r.Reason)) errors["reason"] = [$"Reason must be one of: {string.Join(", ", Reasons)}."];
        var description = r.Description?.Trim();
        if (description is null || description.Length is < 10 or > 1000) errors["description"] = ["Description must be between 10 and 1000 characters."];
        var requested = (r.Items ?? []).Where(i => i.OrderItemId is not null)
            .GroupBy(i => i.OrderItemId!.Value).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity ?? 0));
        if (requested.Count == 0 || (r.Items ?? []).Any(i => i.OrderItemId is null || i.Quantity is null or < 1))
            errors["items"] = ["Choose at least one item, each with a quantity of 1 or more."];
        var order = string.IsNullOrWhiteSpace(r.OrderCode) ? null
            : await orders.Find(o => o.OrderCode == r.OrderCode.Trim() && o.UserId == userId).FirstOrDefaultAsync();
        if (order is null) errors["orderCode"] = ["Order not found."];
        if (errors.Count > 0) return (null, Results.ValidationProblem(errors));

        // Serialize requests per order so two concurrent requests cannot exceed the bought quantity.
        var token = await AcquireReturnLockAsync(order!.OrderId);
        if (token is null)
            return (null, Results.Conflict(new { message = "Another return request for this order is being saved. Please try again." }));
        try
        {
            return await CreateLockedAsync(userId, order, r, requested, description!);
        }
        finally
        {
            await orders.UpdateOneAsync(o => o.OrderId == order.OrderId && o.ReturnLockToken == token,
                Builders<OrderDocument>.Update.Unset(o => o.ReturnLockToken).Unset(o => o.ReturnLockedAt));
        }
    }

    private async Task<string?> AcquireReturnLockAsync(long orderId)
    {
        var token = Guid.NewGuid().ToString("N");
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var now = DateTime.UtcNow;
            var stale = now.AddMinutes(-2);
            var locked = await orders.UpdateOneAsync(
                o => o.OrderId == orderId && (o.ReturnLockToken == null || o.ReturnLockedAt < stale),
                Builders<OrderDocument>.Update.Set(o => o.ReturnLockToken, token).Set(o => o.ReturnLockedAt, now));
            if (locked.ModifiedCount > 0) return token;
            await Task.Delay(50);
        }

        return null;
    }

    private async Task<(ReturnDetailResponse? Return, IResult? Error)> CreateLockedAsync(
        long userId, OrderDocument order, CreateReturnRequest r, Dictionary<long, int> requested, string description)
    {
        var returnable = (await ReturnableAsync(userId, order.OrderCode))!;
        if (!returnable.Eligible) return (null, Results.Conflict(new { message = returnable.IneligibleReason }));
        var byId = returnable.Items.ToDictionary(i => i.OrderItemId);
        var itemErrors = new List<string>();
        foreach (var (itemId, quantity) in requested)
        {
            if (!byId.TryGetValue(itemId, out var item)) itemErrors.Add($"Item {itemId} is not part of this order.");
            else if (quantity > item.RemainingQuantity) itemErrors.Add($"Item {itemId}: only {item.RemainingQuantity} can still be returned.");
        }
        if (itemErrors.Count > 0) return (null, Results.ValidationProblem(new Dictionary<string, string[]> { ["items"] = [.. itemErrors] }));

        var lines = (await orderItems.Find(i => i.OrderId == order.OrderId).ToListAsync()).ToDictionary(i => i.OrderItemId);
        var items = requested.Select(kv =>
        {
            var line = lines[kv.Key];
            return new ReturnItemDocument
            {
                OrderItemId = line.OrderItemId, VariantId = line.VariantId, ProductName = line.ProductNameSnapshot,
                Sku = line.SkuSnapshot, Size = line.SizeSnapshot, Color = line.ColorSnapshot, Quantity = kv.Value,
                UnitPrice = line.UnitPrice,
                AllocatedDiscount = Math.Round(line.DiscountAmount * kv.Value / line.Quantity, MidpointRounding.AwayFromZero),
                EstimatedRefund = RefundFor(line, kv.Value)
            };
        }).ToList();

        var now = DateTime.UtcNow;
        var id = await counters.NextAsync("returns");
        var created = new CustomerReturnDocument
        {
            ReturnId = id, ReturnCode = $"RTN-{id}", OrderId = order.OrderId, OrderCode = order.OrderCode, UserId = userId,
            Status = ReturnStatuses.Requested, Reason = r.Reason!, Description = description, Items = items,
            EstimatedRefund = items.Sum(i => i.EstimatedRefund), RequestedAt = now, UpdatedAt = now,
            Events = [new ReturnEventDocument { Status = ReturnStatuses.Requested, Note = description, ActorId = userId, At = now }]
        };
        await returns.InsertOneAsync(created);
        return (await ToDetailAsync(created, includeCustomer: false), null);
    }

    public async Task<ReturnListItem[]> ListMineAsync(long userId)
    {
        var list = await returns.Find(r => r.UserId == userId).SortByDescending(r => r.RequestedAt).ToListAsync();
        var customer = await CustomersAsync(list.Select(r => r.UserId));
        return list.Select(r => ToListItem(r, customer)).ToArray();
    }

    public async Task<ReturnDetailResponse?> GetMineAsync(long userId, string code)
    {
        var item = await returns.Find(r => r.ReturnCode == code && r.UserId == userId).FirstOrDefaultAsync();
        return item is null ? null : await ToDetailAsync(item, includeCustomer: false);
    }

    // ---- Admin side ---------------------------------------------------------------------------------

    public async Task<PageResponse<ReturnListItem>> ListAsync(
        string? status, string? q, DateTime? fromUtc, DateTime? toUtc, int page, int pageSize)
    {
        var b = Builders<CustomerReturnDocument>.Filter;
        var filter = b.Empty;
        if (!string.IsNullOrWhiteSpace(status))
            filter &= b.In(r => r.Status, status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (fromUtc is not null) filter &= b.Gte(r => r.RequestedAt, fromUtc.Value);
        if (toUtc is not null) filter &= b.Lt(r => r.RequestedAt, toUtc.Value);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = new BsonRegularExpression(Regex.Escape(q.Trim()), "i");
            var userIds = await users.Find(Builders<User>.Filter.Regex(u => u.FullName, pattern) | Builders<User>.Filter.Regex(u => u.Phone, pattern))
                .Project(u => u.UserId).ToListAsync();
            filter &= b.Regex(r => r.ReturnCode, pattern) | b.Regex(r => r.OrderCode, pattern) | b.In(r => r.UserId, userIds);
        }

        var total = await returns.CountDocumentsAsync(filter);
        var list = await returns.Find(filter).SortByDescending(r => r.RequestedAt).ThenByDescending(r => r.ReturnId)
            .Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync();
        var customers = await CustomersAsync(list.Select(r => r.UserId));
        return new PageResponse<ReturnListItem>(list.Select(r => ToListItem(r, customers)).ToArray(), page, pageSize, total);
    }

    public async Task<ReturnCountsResponse> CountsAsync()
    {
        var rows = await returns.Aggregate()
            .Group<BsonDocument>(new BsonDocument { { "_id", "$status" }, { "n", new BsonDocument("$sum", 1) } }).ToListAsync();
        var byStatus = ReturnStatuses.All.ToDictionary(s => s, _ => 0L);
        foreach (var row in rows) byStatus[row["_id"].AsString] = row["n"].ToInt64();
        return new ReturnCountsResponse(byStatus);
    }

    public async Task<ReturnDetailResponse?> GetAsync(string code)
    {
        var item = await returns.Find(r => r.ReturnCode == code).FirstOrDefaultAsync();
        return item is null ? null : await ToDetailAsync(item, includeCustomer: true);
    }

    public static ReturnActionOption[] ActionsFor(string status) => status switch
    {
        ReturnStatuses.Requested => [new("approve", false, false), new("reject", true, false)],
        ReturnStatuses.AwaitingReturn => [new("receive", false, false)],
        ReturnStatuses.Received => [new("inspect_pass", false, false), new("inspect_fail", true, false)],
        ReturnStatuses.RefundPending => [new("refund", false, true)],
        _ => []
    };

    public async Task<(ReturnDetailResponse? Return, IResult? Error)> TransitionAsync(
        long adminId, string code, ReturnTransitionRequest r)
    {
        var current = await returns.Find(x => x.ReturnCode == code).FirstOrDefaultAsync();
        if (current is null) return (null, Results.NotFound());
        var option = ActionsFor(current.Status).FirstOrDefault(a => a.Action == r.Action);
        if (option is null)
        {
            return (null, Results.Conflict(new
            {
                message = $"A return that is '{current.Status}' does not allow '{r.Action}'.",
                allowed = ActionsFor(current.Status).Select(a => a.Action).ToArray()
            }));
        }

        var errors = new Dictionary<string, string[]>();
        var reason = r.Reason?.Trim();
        var note = r.Note?.Trim();
        var reference = r.RefundReference?.Trim();
        if (option.RequiresReason && (reason is null || reason.Length is < 10 or > 300)) errors["reason"] = ["Reason must be between 10 and 300 characters."];
        if (note is { Length: > 300 }) errors["note"] = ["Note must be at most 300 characters."];
        if (option.RequiresRefundReference && (reference is null || reference.Length is < 1 or > 100))
            errors["refundReference"] = ["Refund reference is required (at most 100 characters)."];
        if (errors.Count > 0) return (null, Results.ValidationProblem(errors));

        var now = DateTime.UtcNow;
        var u = Builders<CustomerReturnDocument>.Update;
        var (to, eventNote) = r.Action switch
        {
            "approve" => (ReturnStatuses.AwaitingReturn, note),
            "reject" => (ReturnStatuses.Rejected, reason),
            "receive" => (ReturnStatuses.Received, note),
            "inspect_pass" => (ReturnStatuses.RefundPending, note ?? "Inspection passed"),
            "inspect_fail" => (ReturnStatuses.Rejected, reason),
            _ => (ReturnStatuses.Refunded, $"Refund reference: {reference}")
        };
        var updates = new List<UpdateDefinition<CustomerReturnDocument>>
        {
            u.Set(x => x.Status, to), u.Set(x => x.UpdatedAt, now), u.Set(x => x.AssigneeId, adminId),
            u.Push(x => x.Events, new ReturnEventDocument { Status = to, Note = eventNote, ActorId = adminId, At = now })
        };
        if (to == ReturnStatuses.Rejected) updates.Add(u.Set(x => x.RejectReason, reason));
        if (to == ReturnStatuses.Refunded)
        {
            updates.Add(u.Set(x => x.RefundReference, reference));
            updates.Add(u.Set(x => x.RefundAmount, current.EstimatedRefund));
            updates.Add(u.Set(x => x.RefundedAt, now));
        }

        // Goods that pass inspection go back on the shelf before the status changes; if the status
        // write then loses a race, the restock is rolled back so stock is never counted twice.
        var restocked = new List<CustomerReturnItemDocumentRef>();
        if (r.Action == "inspect_pass")
        {
            try
            {
                foreach (var item in current.Items)
                {
                    await stock.RestockAsync(item.VariantId, item.Quantity, "return", current.ReturnId, adminId, "Return inspection passed");
                    restocked.Add(new(item.VariantId, item.Quantity));
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Restock failed for return {ReturnCode}.", current.ReturnCode);
                await UndoRestockAsync(restocked, current.ReturnId, adminId);
                return (null, Results.Conflict(new { message = "The stock could not be updated. Nothing was changed; please try again." }));
            }
        }

        var claimed = await returns.FindOneAndUpdateAsync(x => x.ReturnId == current.ReturnId && x.Status == current.Status,
            u.Combine(updates), new FindOneAndUpdateOptions<CustomerReturnDocument> { ReturnDocument = ReturnDocument.After });
        if (claimed is null)
        {
            await UndoRestockAsync(restocked, current.ReturnId, adminId);
            return (null, Results.Conflict(new { message = "The return was changed by someone else. Reload it and try again." }));
        }

        await NotifyAsync(claimed, to, reason);
        return (await ToDetailAsync(claimed, includeCustomer: true), null);
    }

    // ---- Helpers ---------------------------------------------------------------------------------------

    private sealed record CustomerReturnItemDocumentRef(long VariantId, int Quantity);

    private async Task UndoRestockAsync(List<CustomerReturnItemDocumentRef> restocked, long returnId, long adminId)
    {
        foreach (var item in restocked)
        {
            try
            {
                await stock.CompensateAsync(item.VariantId, -item.Quantity, 0, "return", returnId, adminId);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Rolling back restock failed for return {ReturnId}.", returnId);
            }
        }
    }

    private static double RefundFor(OrderItemDocument line, int quantity) =>
        line.Quantity == 0 ? 0 : Math.Round(line.LineTotal * quantity / line.Quantity, MidpointRounding.AwayFromZero);

    private async Task<Dictionary<long, int>> ReturnedQuantitiesAsync(long orderId)
    {
        var active = await returns.Find(r => r.OrderId == orderId && r.Status != ReturnStatuses.Rejected).ToListAsync();
        return active.SelectMany(r => r.Items).GroupBy(i => i.OrderItemId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
    }

    private async Task<Dictionary<long, User>> CustomersAsync(IEnumerable<long> userIds) =>
        (await users.Find(Builders<User>.Filter.In(u => u.UserId, userIds.Distinct())).ToListAsync()).ToDictionary(u => u.UserId);

    private static ReturnListItem ToListItem(CustomerReturnDocument r, Dictionary<long, User> customers) =>
        new(r.ReturnId, r.ReturnCode, r.OrderCode, r.Status, r.Items.Sum(i => i.Quantity), r.EstimatedRefund, r.RequestedAt,
            (int)(DateTime.UtcNow - r.RequestedAt).TotalDays,
            new ReturnCustomerResponse(r.UserId, customers.GetValueOrDefault(r.UserId)?.FullName, customers.GetValueOrDefault(r.UserId)?.Phone));

    private async Task<ReturnDetailResponse> ToDetailAsync(CustomerReturnDocument r, bool includeCustomer)
    {
        var people = await CustomersAsync([r.UserId, .. r.Events.Where(e => e.ActorId is not null).Select(e => e.ActorId!.Value)]);
        return new ReturnDetailResponse(r.ReturnId, r.ReturnCode, r.OrderCode, r.Status, r.Reason, r.Description,
            r.Items.Select(i => new ReturnItemResponse(i.OrderItemId, i.ProductName, i.Sku, i.Size, i.Color, i.Quantity,
                i.UnitPrice, i.AllocatedDiscount, i.EstimatedRefund)).ToArray(),
            r.EstimatedRefund, r.RefundAmount, r.RefundReference, r.RejectReason, r.RequestedAt, r.UpdatedAt, r.RefundedAt,
            r.Events.Select(e => new ReturnEventResponse(e.Status, e.Note,
                e.ActorId is { } a && a != r.UserId ? people.GetValueOrDefault(a)?.FullName : null, e.At)).ToArray(),
            includeCustomer ? new ReturnCustomerResponse(r.UserId, people.GetValueOrDefault(r.UserId)?.FullName, people.GetValueOrDefault(r.UserId)?.Phone) : null,
            includeCustomer ? ActionsFor(r.Status) : []);
    }

    private async Task NotifyAsync(CustomerReturnDocument r, string to, string? reason)
    {
        var (title, body) = to switch
        {
            ReturnStatuses.AwaitingReturn => ($"Yêu cầu trả {r.ReturnCode} đã được duyệt", "Vui lòng gửi hàng về shop theo hướng dẫn."),
            ReturnStatuses.Received => ($"Shop đã nhận hàng trả {r.ReturnCode}", "Shop đang kiểm tra sản phẩm."),
            ReturnStatuses.RefundPending => ($"Hàng trả {r.ReturnCode} đạt kiểm tra", "Shop sẽ hoàn tiền cho bạn."),
            ReturnStatuses.Refunded => ($"Đã hoàn tiền cho {r.ReturnCode}", $"Số tiền hoàn: {Promotions.VoucherText.Vnd(r.RefundAmount ?? 0)}."),
            ReturnStatuses.Rejected => ($"Yêu cầu trả {r.ReturnCode} bị từ chối", $"Lý do: {reason}"),
            _ => ($"Yêu cầu trả {r.ReturnCode} đã cập nhật", "")
        };
        try
        {
            await notifications.CreateAsync(r.UserId, "return_status", title, body,
                new Dictionary<string, long> { ["returnId"] = r.ReturnId });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Return notification failed for {ReturnCode}.", r.ReturnCode);
        }
    }
}
