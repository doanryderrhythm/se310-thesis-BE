namespace AuthApi.Features.Orders;

internal sealed record TransitionOption(
    string ToStatus, bool RequiresReason, bool RequiresFailureInfo, bool RequiresInspection);

/// <summary>
/// Order lifecycle. pending → confirmed → packing → shipping → delivered, with cancellation
/// before shipping and the delivery-failure / return-to-seller branch after it.
/// </summary>
internal static class OrderStateMachine
{
    public const int MaxDeliveryAttempts = 2;
    public const string CustomerRefused = "customer_refused";

    public static readonly string[] FailureReasons =
        ["unreachable", "wrong_address", "reschedule", CustomerRefused, "other"];

    public static bool CanRedeliver(OrderDocument order) =>
        order.DeliveryAttempts.Count < MaxDeliveryAttempts
        && order.DeliveryAttempts.LastOrDefault()?.Reason != CustomerRefused;

    /// <summary>Hand-over needs COD or an already-confirmed transfer.</summary>
    public static bool CanHandOver(OrderDocument order) =>
        order.PaymentMethod == PaymentMethods.Cod || order.PaymentStatus == PaymentStatuses.Paid;

    public static IReadOnlyList<TransitionOption> Options(OrderDocument order)
    {
        static TransitionOption Plain(string to) => new(to, false, false, false);
        static TransitionOption Cancel() => new(OrderStatuses.Cancelled, true, false, false);

        return order.Status switch
        {
            OrderStatuses.Pending => [Plain(OrderStatuses.Confirmed), Cancel()],
            OrderStatuses.Confirmed => [Plain(OrderStatuses.Packing), Cancel()],
            OrderStatuses.Packing => CanHandOver(order)
                ? [Plain(OrderStatuses.Shipping), Cancel()]
                : [Cancel()],
            OrderStatuses.Shipping =>
                [Plain(OrderStatuses.Delivered), new(OrderStatuses.DeliveryFailed, false, true, false)],
            OrderStatuses.DeliveryFailed => CanRedeliver(order)
                ? [Plain(OrderStatuses.Shipping), Plain(OrderStatuses.ReturningToSeller)]
                : [Plain(OrderStatuses.ReturningToSeller)],
            OrderStatuses.ReturningToSeller => [new(OrderStatuses.ReturnedToSeller, false, false, true)],
            _ => []
        };
    }

    /// <summary>True while stock for this order is only reserved (not yet shipped).</summary>
    public static bool HoldsReservation(string status) =>
        status is OrderStatuses.Pending or OrderStatuses.Confirmed or OrderStatuses.Packing;
}
