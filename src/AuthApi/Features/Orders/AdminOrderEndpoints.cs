using AuthApi.Common;
using AuthApi.Security;
using MongoDB.Driver;

namespace AuthApi.Features.Orders;

internal static class AdminOrderEndpoints
{
    public static void MapAdminOrderEndpoints(
        this IEndpointRouteBuilder app, IMongoCollection<User> users, AdminOrderService orders)
    {
        var currentUsers = new CurrentUserService(users);
        var group = app.MapGroup("/api/admin/orders").RequireAuthorization();

        group.MapGet("", async (HttpContext context, string? q, string? status, string? paymentStatus,
            string? paymentMethod, string? from, string? to, int? page, int? pageSize) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(context, UserRoles.Admin);
            if (error is not null) return error;
            var errors = new Dictionary<string, string[]>();
            DateTime? fromUtc = null, toUtc = null;
            if (!string.IsNullOrWhiteSpace(from))
            {
                if (VnTime.TryDay(from, out var f)) fromUtc = VnTime.StartOfDayUtc(f);
                else errors["from"] = ["from must be a date like 2026-11-23."];
            }
            if (!string.IsNullOrWhiteSpace(to))
            {
                if (VnTime.TryDay(to, out var t)) toUtc = VnTime.StartOfDayUtc(t.AddDays(1));
                else errors["to"] = ["to must be a date like 2026-11-23."];
            }
            if (status is not null && status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Any(s => !OrderStatuses.All.Contains(s)))
            {
                errors["status"] = [$"status must be a comma-separated list of: {string.Join(", ", OrderStatuses.All)}."];
            }
            var bad = Paging.TryGet(page, pageSize, out var p, out var ps);
            if (bad is not null) return bad;
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            return Results.Ok(await orders.ListAsync(q, status, paymentStatus, paymentMethod, fromUtc, toUtc, p, ps));
        });

        group.MapGet("/counts", async (HttpContext context) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(context, UserRoles.Admin);
            return error ?? Results.Ok(await orders.CountsAsync());
        });

        group.MapGet("/{orderCode}", async (HttpContext context, string orderCode) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(context, UserRoles.Admin);
            if (error is not null) return error;
            var detail = await orders.GetAsync(orderCode);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        group.MapPost("/{orderCode}/transitions", async (HttpContext context, string orderCode, TransitionRequest request) =>
        {
            var (admin, error) = await currentUsers.ResolveAsync(context, UserRoles.Admin);
            if (error is not null) return error;
            var (detail, transitionError) = await orders.TransitionAsync(admin!.UserId, orderCode, request);
            return transitionError ?? Results.Ok(detail);
        });

        group.MapPost("/{orderCode}/payment", async (HttpContext context, string orderCode, OrderPaymentRequest request) =>
        {
            var (admin, error) = await currentUsers.ResolveAsync(context, UserRoles.Admin);
            if (error is not null) return error;
            var (detail, paymentError) = await orders.PaymentAsync(admin!.UserId, orderCode, request);
            return paymentError ?? Results.Ok(detail);
        });
    }
}
