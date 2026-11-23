using System.Text.RegularExpressions;
using AuthApi.Common;
using AuthApi.Data;
using AuthApi.Features.Inventory;
using AuthApi.Features.Notifications;
using AuthApi.Features.Promotions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AuthApi.Features.Orders;

internal sealed class AdminOrderService(
    IMongoDatabase db, CounterService counters, InventoryService stock, CouponService coupons,
    VoucherIssuer voucherIssuer, NotificationService notifications, ILogger logger)
{
    private readonly IMongoCollection<OrderDocument> orders = db.GetCollection<OrderDocument>("orders");
    private readonly IMongoCollection<OrderItemDocument> items = db.GetCollection<OrderItemDocument>("order_items");
    private readonly IMongoCollection<OrderStatusHistoryDocument> history =
        db.GetCollection<OrderStatusHistoryDocument>("order_status_history");
    private readonly IMongoCollection<User> users = db.GetCollection<User>("users");

    public static async Task SetupAsync(IMongoDatabase db, CounterService counters)
    {
        var orders = db.GetCollection<OrderDocument>("orders");
        await orders.Indexes.CreateOneAsync(new CreateIndexModel<OrderDocument>(
            Builders<OrderDocument>.IndexKeys.Ascending(o => o.OrderCode),
            new CreateIndexOptions { Unique = true, Name = "order_code_unique" }));
        await orders.Indexes.CreateOneAsync(new CreateIndexModel<OrderDocument>(
            Builders<OrderDocument>.IndexKeys.Ascending(o => o.Status).Descending(o => o.PlacedAt),
            new CreateIndexOptions { Name = "status_placed" }));
        await orders.Indexes.CreateOneAsync(new CreateIndexModel<OrderDocument>(
            Builders<OrderDocument>.IndexKeys.Descending(o => o.PlacedAt),
            new CreateIndexOptions { Name = "placed_at_desc" }));
        await orders.Indexes.CreateOneAsync(new CreateIndexModel<OrderDocument>(
            Builders<OrderDocument>.IndexKeys.Ascending(o => o.UserId).Ascending(o => o.IdempotencyKey),
            new CreateIndexOptions<OrderDocument>
            {
                Unique = true,
                Name = "customer_idempotency_unique",
                PartialFilterExpression = new MongoDB.Bson.BsonDocument
                {
                    { "idempotency_key", new MongoDB.Bson.BsonDocument("$type", "string") }
                }
            }));
        foreach (var (counter, collection, field) in new[]
        {
            ("orders", "orders", "order_id"), ("order_items", "order_items", "order_item_id"),
            ("order_status_history", "order_status_history", "history_id")
        })
        {
            await IdCounters.SyncAsync(db, counters, counter, collection, field);
        }
    }

    // ---- Reads -------------------------------------------------------------------

    public async Task<PageResponse<AdminOrderListItem>> ListAsync(
        string? q, string? status, string? paymentStatus, string? paymentMethod,
        DateTime? fromUtc, DateTime? toUtc, int page, int pageSize)
    {
        var b = Builders<OrderDocument>.Filter;
        var filter = b.Empty;
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = new BsonRegularExpression(Regex.Escape(q.Trim()), "i");
            filter &= b.Regex(o => o.OrderCode, pattern) | b.Regex(o => o.RecipientPhone, pattern);
        }
        if (!string.IsNullOrWhiteSpace(status))
            filter &= b.In(o => o.Status, status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (!string.IsNullOrWhiteSpace(paymentStatus)) filter &= b.Eq(o => o.PaymentStatus, paymentStatus);
        if (!string.IsNullOrWhiteSpace(paymentMethod)) filter &= b.Eq(o => o.PaymentMethod, paymentMethod);
        if (fromUtc is not null) filter &= b.Gte(o => o.PlacedAt, fromUtc.Value);
        if (toUtc is not null) filter &= b.Lt(o => o.PlacedAt, toUtc.Value);

        var total = await orders.CountDocumentsAsync(filter);
        var page_ = await orders.Find(filter).SortByDescending(o => o.PlacedAt).ThenByDescending(o => o.OrderId)
            .Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync();
        var counts = await ItemCountsAsync(page_.Select(o => o.OrderId));
        var customers = await CustomersAsync(page_.Select(o => o.UserId));
        return new PageResponse<AdminOrderListItem>(
            page_.Select(o => new AdminOrderListItem(
                o.OrderId, o.OrderCode, o.PlacedAt, Customer(customers, o.UserId), o.RecipientName,
                o.RecipientPhone, counts.GetValueOrDefault(o.OrderId), o.TotalAmount,
                o.PaymentMethod, o.PaymentStatus, o.Status)).ToArray(),
            page, pageSize, total);
    }

    public async Task<OrderCountsResponse> CountsAsync()
    {
        var rows = await orders.Aggregate()
            .Group<BsonDocument>(new BsonDocument { { "_id", "$status" }, { "n", new BsonDocument("$sum", 1) } })
            .ToListAsync();
        var byStatus = OrderStatuses.All.ToDictionary(s => s, _ => 0L);
        foreach (var row in rows) byStatus[row["_id"].AsString] = row["n"].ToInt64();
        return new OrderCountsResponse(byStatus, new OrderTabCounts(
            byStatus[OrderStatuses.Pending],
            byStatus[OrderStatuses.Confirmed] + byStatus[OrderStatuses.Packing],
            byStatus[OrderStatuses.Shipping], byStatus[OrderStatuses.DeliveryFailed],
            byStatus[OrderStatuses.ReturningToSeller] + byStatus[OrderStatuses.ReturnedToSeller]));
    }

    public async Task<AdminOrderDetail?> GetAsync(string orderCode)
    {
        var order = await orders.Find(o => o.OrderCode == orderCode).FirstOrDefaultAsync();
        return order is null ? null : await BuildDetailAsync(order);
    }

    // ---- Transitions ----------------------------------------------------------------

    /// <param name="onlyOwnedBy">When set (customer actions) the order must belong to this user, otherwise it is "not found".</param>
    /// <param name="onlyFrom">When set, the move is only allowed from one of these statuses.</param>
    public async Task<(AdminOrderDetail? Detail, IResult? Error)> TransitionAsync(
        long adminId, string orderCode, TransitionRequest request, long? onlyOwnedBy = null, string[]? onlyFrom = null)
    {
        var order = await orders.Find(o => o.OrderCode == orderCode).FirstOrDefaultAsync();
        if (order is null || (onlyOwnedBy is { } owner && order.UserId != owner)) return (null, Results.NotFound());
        if (onlyFrom is not null && !onlyFrom.Contains(order.Status))
        {
            return (null, Results.Conflict(new
            {
                message = $"This action is only possible while the order is {string.Join(" or ", onlyFrom)}; it is '{order.Status}'."
            }));
        }

        var option = OrderStateMachine.Options(order).FirstOrDefault(o => o.ToStatus == request.ToStatus);
        if (option is null)
        {
            return (null, Results.Conflict(new
            {
                message = $"An order that is '{order.Status}' cannot move to '{request.ToStatus}'.",
                allowed = OrderStateMachine.Options(order).Select(o => o.ToStatus).ToArray()
            }));
        }

        var errors = new Dictionary<string, string[]>();
        var note = request.Note?.Trim();
        var reason = request.Reason?.Trim();
        if (note is { Length: > 300 }) errors["note"] = ["Note must be at most 300 characters."];
        if (option.RequiresReason && (reason is null || reason.Length is < 10 or > 300))
            errors["reason"] = ["Reason must be between 10 and 300 characters."];
        if (option.RequiresFailureInfo)
        {
            if (request.FailureReason is null || !OrderStateMachine.FailureReasons.Contains(request.FailureReason))
                errors["failureReason"] = [$"Failure reason must be one of: {string.Join(", ", OrderStateMachine.FailureReasons)}."];
            if (note is null || note.Length < 10)
                errors["note"] = ["Note must be between 10 and 300 characters."];
        }
        if (option.RequiresInspection && request.Inspection is not ("passed" or "failed"))
            errors["inspection"] = ["Inspection must be 'passed' or 'failed'."];
        if (request.Carrier is { Length: > 100 }) errors["carrier"] = ["Carrier must be at most 100 characters."];
        if (request.TrackingCode is { Length: > 100 }) errors["trackingCode"] = ["Tracking code must be at most 100 characters."];
        if (errors.Count > 0) return (null, Results.ValidationProblem(errors));

        var from = order.Status;
        var to = option.ToStatus;
        var now = DateTime.UtcNow;
        var u = Builders<OrderDocument>.Update;
        var updates = new List<UpdateDefinition<OrderDocument>> { u.Set(o => o.Status, to), u.Set(o => o.UpdatedAt, now) };
        var historyNote = note;

        switch (to)
        {
            case OrderStatuses.Cancelled:
                updates.Add(u.Set(o => o.CancelReason, reason));
                historyNote = reason;
                if (order.PaymentStatus == PaymentStatuses.Paid)
                    updates.Add(u.Set(o => o.PaymentStatus, PaymentStatuses.RefundPending));
                break;
            case OrderStatuses.Shipping when from == OrderStatuses.Packing:
                if (!string.IsNullOrWhiteSpace(request.Carrier)) updates.Add(u.Set(o => o.Carrier, request.Carrier.Trim()));
                if (!string.IsNullOrWhiteSpace(request.TrackingCode)) updates.Add(u.Set(o => o.TrackingCode, request.TrackingCode.Trim()));
                break;
            case OrderStatuses.Delivered:
                updates.Add(u.Set(o => o.DeliveredAt, now));
                if (order.PaymentMethod == PaymentMethods.Cod && order.PaymentStatus is PaymentStatuses.Unpaid or null)
                {
                    updates.Add(u.Set(o => o.PaymentStatus, PaymentStatuses.Paid));
                    updates.Add(u.Set(o => o.PaidAt, now));
                }
                break;
            case OrderStatuses.DeliveryFailed:
                var attempt = new DeliveryAttemptDocument
                {
                    AttemptNumber = order.DeliveryAttempts.Count + 1,
                    AttemptedAt = request.AttemptedAt is { } at ? CampaignRules.ToUtc(at) : now,
                    Reason = request.FailureReason!,
                    Note = note
                };
                updates.Add(u.Push(o => o.DeliveryAttempts, attempt));
                break;
            case OrderStatuses.ReturnedToSeller:
                updates.Add(u.Set(o => o.ReturnInspection, request.Inspection));
                if (order.PaymentStatus == PaymentStatuses.Paid)
                    updates.Add(u.Set(o => o.PaymentStatus, PaymentStatuses.RefundPending));
                historyNote = $"Inspection {request.Inspection}" + (note is null ? "" : $": {note}");
                break;
        }

        // 1. Lock the order in its current status so concurrent requests cannot both apply a move.
        var token = Guid.NewGuid().ToString("N");
        var stale = now.AddMinutes(-2);
        var locked = await orders.FindOneAndUpdateAsync(
            o => o.OrderId == order.OrderId && o.Status == from
                && (o.TransitionToken == null || o.TransitionLockedAt < stale),
            u.Set(o => o.TransitionToken, token).Set(o => o.TransitionLockedAt, now));
        if (locked is null)
            return (null, Results.Conflict(new { message = "The order was changed by someone else. Reload it and try again." }));

        // 2. Stock and coupon effects first, each with an undo, so a failure leaves nothing half-done.
        var lines = await items.Find(i => i.OrderId == order.OrderId).ToListAsync();
        var undo = new List<Func<Task>>();
        string? failure;
        try
        {
            failure = await ApplyEffectsAsync(adminId, order, from, to, request.Inspection, lines, undo);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Applying {From} -> {To} failed for order {OrderCode}.", from, to, orderCode);
            failure = "The change could not be completed. Nothing was changed; please try again.";
        }

        if (failure is not null)
        {
            await RollBackAsync(undo, token, order.OrderId);
            return (null, Results.Conflict(new { message = failure }));
        }

        // 3. Commit the new status and release the lock in the same write.
        updates.Add(u.Unset(o => o.TransitionToken));
        updates.Add(u.Unset(o => o.TransitionLockedAt));
        var committed = await orders.FindOneAndUpdateAsync(
            o => o.OrderId == order.OrderId && o.TransitionToken == token, u.Combine(updates),
            new FindOneAndUpdateOptions<OrderDocument> { ReturnDocument = ReturnDocument.After });
        if (committed is null)
        {
            await RollBackAsync(undo, token, order.OrderId);
            return (null, Results.Conflict(new { message = "The order was changed by someone else. Reload it and try again." }));
        }

        // 4. Follow-ups that must not undo a committed change.
        try
        {
            await history.InsertOneAsync(new OrderStatusHistoryDocument
            {
                HistoryId = await counters.NextAsync("order_status_history"),
                OrderId = order.OrderId,
                OldStatus = from,
                NewStatus = to,
                ChangedBy = adminId,
                Note = historyNote,
                ChangedAt = now
            });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Writing history failed for order {OrderCode}.", orderCode);
        }

        if (to == OrderStatuses.Delivered && order.UserId is { } buyer)
        {
            try
            {
                await voucherIssuer.IssueSpendVouchersAsync(buyer);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Spend vouchers failed for user {UserId}.", buyer);
            }
        }

        await NotifyAsync(committed, to, reason);
        return (await BuildDetailAsync(committed), null);
    }

    /// <summary>
    /// Stock and coupon side effects of a move. Registers an undo for every stock change and
    /// returns a user-facing message when the move cannot be applied.
    /// </summary>
    private async Task<string?> ApplyEffectsAsync(
        long adminId, OrderDocument order, string from, string to, string? inspection,
        List<OrderItemDocument> lines, List<Func<Task>> undo)
    {
        const string Ref = "order";
        var id = order.OrderId;
        if (to == OrderStatuses.Cancelled && OrderStateMachine.HoldsReservation(from))
        {
            foreach (var line in lines)
            {
                var released = await stock.ReleaseReservedAsync(line.VariantId, line.Quantity, Ref, id, adminId);
                if (released > 0) undo.Add(() => stock.CompensateAsync(line.VariantId, 0, released, Ref, id, adminId));
            }

            // Idempotent: a retry after a failure gives the coupon back only once.
            await coupons.ReleaseAsync(id);
        }
        else if (to == OrderStatuses.Shipping && from == OrderStatuses.Packing)
        {
            foreach (var line in lines)
            {
                if (!await stock.ShipAsync(line.VariantId, line.Quantity, Ref, id, adminId))
                    return $"Not enough reserved stock to hand over {line.SkuSnapshot ?? line.VariantId.ToString()}; check the inventory first.";
                undo.Add(() => stock.CompensateAsync(line.VariantId, line.Quantity, line.Quantity, Ref, id, adminId));
            }
        }
        else if (to == OrderStatuses.ReturnedToSeller && inspection == "passed")
        {
            foreach (var line in lines)
            {
                await stock.RestockAsync(line.VariantId, line.Quantity, Ref, id, adminId, "Returned to shop, inspection passed");
                undo.Add(() => stock.CompensateAsync(line.VariantId, -line.Quantity, 0, Ref, id, adminId));
            }
        }

        return null;
    }

    private async Task RollBackAsync(List<Func<Task>> undo, string token, long orderId)
    {
        foreach (var step in Enumerable.Reverse(undo))
        {
            try
            {
                await step();
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Rolling back stock failed for order {OrderId}.", orderId);
            }
        }

        await orders.UpdateOneAsync(o => o.OrderId == orderId && o.TransitionToken == token,
            Builders<OrderDocument>.Update.Unset(o => o.TransitionToken).Unset(o => o.TransitionLockedAt));
    }

    // ---- Payment ---------------------------------------------------------------------------

    public async Task<(AdminOrderDetail? Detail, IResult? Error)> PaymentAsync(
        long adminId, string orderCode, OrderPaymentRequest request)
    {
        var order = await orders.Find(o => o.OrderCode == orderCode).FirstOrDefaultAsync();
        if (order is null) return (null, Results.NotFound());
        if (request.Reference is { Length: > 100 })
            return (null, Results.ValidationProblem(new Dictionary<string, string[]> { ["reference"] = ["Reference must be at most 100 characters."] }));

        var now = DateTime.UtcNow;
        var u = Builders<OrderDocument>.Update;
        FilterDefinition<OrderDocument> guard;
        UpdateDefinition<OrderDocument> update;
        string note;
        switch (request.Action)
        {
            case "confirm_paid":
                if (order.PaymentMethod != PaymentMethods.BankTransfer || order.PaymentStatus != PaymentStatuses.Pending
                    || order.Status == OrderStatuses.Cancelled)
                {
                    return (null, Results.Conflict(new { message = "Only a pending bank transfer on an open order can be confirmed as paid." }));
                }
                guard = Builders<OrderDocument>.Filter.Eq(o => o.PaymentStatus, PaymentStatuses.Pending);
                update = u.Set(o => o.PaymentStatus, PaymentStatuses.Paid).Set(o => o.PaidAt, now).Set(o => o.UpdatedAt, now);
                note = "Payment confirmed" + (string.IsNullOrWhiteSpace(request.Reference) ? "" : $" (ref {request.Reference.Trim()})");
                break;
            case "confirm_refunded":
                if (order.PaymentStatus != PaymentStatuses.RefundPending)
                    return (null, Results.Conflict(new { message = "Only an order with a pending refund can be confirmed as refunded." }));
                guard = Builders<OrderDocument>.Filter.Eq(o => o.PaymentStatus, PaymentStatuses.RefundPending);
                update = u.Set(o => o.PaymentStatus, PaymentStatuses.Refunded).Set(o => o.RefundedAt, now).Set(o => o.UpdatedAt, now);
                note = "Refund confirmed" + (string.IsNullOrWhiteSpace(request.Reference) ? "" : $" (ref {request.Reference.Trim()})");
                break;
            default:
                return (null, Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = ["Action must be 'confirm_paid' or 'confirm_refunded'."]
                }));
        }

        var changed = await orders.UpdateOneAsync(Builders<OrderDocument>.Filter.Eq(o => o.OrderId, order.OrderId) & guard, update);
        if (changed.ModifiedCount == 0)
            return (null, Results.Conflict(new { message = "The order was changed by someone else. Reload it and try again." }));

        await history.InsertOneAsync(new OrderStatusHistoryDocument
        {
            HistoryId = await counters.NextAsync("order_status_history"),
            OrderId = order.OrderId,
            OldStatus = order.Status,
            NewStatus = order.Status,
            ChangedBy = adminId,
            Note = note,
            ChangedAt = now
        });
        if (order.UserId is { } userId)
        {
            await notifications.CreateAsync(userId, "order_payment",
                request.Action == "confirm_paid" ? $"Đơn {order.OrderCode} đã được xác nhận thanh toán" : $"Đơn {order.OrderCode} đã được hoàn tiền",
                request.Action == "confirm_paid" ? "Cảm ơn bạn! Shop đã nhận được thanh toán." : "Shop đã hoàn tiền cho đơn hàng của bạn.",
                new Dictionary<string, long> { ["orderId"] = order.OrderId });
        }

        return (await BuildDetailAsync(await orders.Find(o => o.OrderId == order.OrderId).FirstAsync()), null);
    }

    // ---- Helpers -------------------------------------------------------------------------------------

    private async Task NotifyAsync(OrderDocument order, string to, string? reason)
    {
        if (order.UserId is not { } userId) return;
        var (title, body) = to switch
        {
            OrderStatuses.Confirmed => ($"Đơn {order.OrderCode} đã được xác nhận", "Shop đã xác nhận đơn hàng của bạn."),
            OrderStatuses.Packing => ($"Đơn {order.OrderCode} đang được đóng gói", "Shop đang chuẩn bị hàng cho bạn."),
            OrderStatuses.Shipping => ($"Đơn {order.OrderCode} đang được giao", "Đơn hàng đã được bàn giao cho đơn vị vận chuyển."),
            OrderStatuses.Delivered => ($"Đơn {order.OrderCode} đã giao thành công", "Cảm ơn bạn đã mua sắm tại Themis Fashion Studio."),
            OrderStatuses.DeliveryFailed => ($"Giao đơn {order.OrderCode} chưa thành công", "Đơn vị vận chuyển chưa giao được hàng. Shop sẽ liên hệ với bạn."),
            OrderStatuses.ReturningToSeller => ($"Đơn {order.OrderCode} đang hoàn về shop", "Đơn hàng không giao được và đang được hoàn về shop."),
            OrderStatuses.ReturnedToSeller => ($"Đơn {order.OrderCode} đã hoàn về shop", "Shop đã nhận lại đơn hàng."),
            OrderStatuses.Cancelled => ($"Đơn {order.OrderCode} đã bị hủy", $"Lý do: {reason}"),
            _ => ($"Đơn {order.OrderCode} đã cập nhật", "")
        };
        try
        {
            await notifications.CreateAsync(userId, "order_status", title, body,
                new Dictionary<string, long> { ["orderId"] = order.OrderId });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Order notification failed for {OrderCode}.", order.OrderCode);
        }
    }

    private async Task<Dictionary<long, int>> ItemCountsAsync(IEnumerable<long> orderIds)
    {
        var rows = await items.Aggregate()
            .Match(Builders<OrderItemDocument>.Filter.In(i => i.OrderId, orderIds))
            .Group<BsonDocument>(new BsonDocument { { "_id", "$order_id" }, { "n", new BsonDocument("$sum", "$quantity") } })
            .ToListAsync();
        return rows.ToDictionary(r => r["_id"].ToInt64(), r => r["n"].ToInt32());
    }

    private async Task<Dictionary<long, User>> CustomersAsync(IEnumerable<long?> userIds)
    {
        var ids = userIds.Where(i => i is not null).Select(i => i!.Value).Distinct().ToArray();
        return (await users.Find(Builders<User>.Filter.In(u => u.UserId, ids)).ToListAsync()).ToDictionary(u => u.UserId);
    }

    private static OrderCustomerResponse Customer(Dictionary<long, User> customers, long? userId) =>
        userId is { } id && customers.TryGetValue(id, out var user)
            ? new OrderCustomerResponse(id, user.FullName, user.Phone)
            : new OrderCustomerResponse(userId, null, null);

    private async Task<AdminOrderDetail> BuildDetailAsync(OrderDocument o)
    {
        var lines = await items.Find(i => i.OrderId == o.OrderId).SortBy(i => i.OrderItemId).ToListAsync();
        var events = await history.Find(h => h.OrderId == o.OrderId).SortBy(h => h.ChangedAt).ThenBy(h => h.HistoryId).ToListAsync();
        var customers = await CustomersAsync([o.UserId, .. events.Select(e => e.ChangedBy)]);
        return new AdminOrderDetail(
            o.OrderId, o.OrderCode, o.Status, o.PlacedAt, o.UpdatedAt, o.DeliveredAt, Customer(customers, o.UserId),
            o.RecipientName, o.RecipientPhone, o.ShippingAddressLine, o.ShippingWard, o.ShippingDistrict,
            o.ShippingProvince, o.ShippingPostalCode, o.Subtotal, o.DiscountAmount, o.ShippingFee, o.TotalAmount,
            o.PaymentMethod, o.PaymentStatus, o.PaidAt, o.RefundedAt, o.CustomerNote, o.CancelReason,
            o.Carrier, o.TrackingCode, o.ReturnInspection, o.CouponId,
            lines.Select(i => new AdminOrderItemResponse(i.OrderItemId, i.VariantId, i.ProductId, i.ProductNameSnapshot,
                i.SkuSnapshot, i.SizeSnapshot, i.ColorSnapshot, i.Quantity, i.UnitPrice, i.DiscountAmount, i.LineTotal)).ToArray(),
            o.DeliveryAttempts.Select(a => new DeliveryAttemptResponse(a.AttemptNumber, a.AttemptedAt, a.Reason, a.Note)).ToArray(),
            events.Select(e => new OrderHistoryResponse(e.OldStatus, e.NewStatus,
                e.ChangedBy is { } by && customers.TryGetValue(by, out var actor) ? actor.FullName : null, e.Note, e.ChangedAt)).ToArray(),
            OrderStateMachine.Options(o).Select(t => new AllowedTransitionResponse(
                t.ToStatus, t.RequiresReason, t.RequiresFailureInfo, t.RequiresInspection)).ToArray(),
            o.PaymentMethod == PaymentMethods.BankTransfer && o.PaymentStatus == PaymentStatuses.Pending && o.Status != OrderStatuses.Cancelled,
            o.PaymentStatus == PaymentStatuses.RefundPending,
            OrderStateMachine.MaxDeliveryAttempts);
    }
}
