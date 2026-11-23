using AuthApi.Common;
using AuthApi.Security;
using MongoDB.Driver;

namespace AuthApi.Features.Orders;

internal static class CustomerOrderEndpoints
{
    public static void MapCustomerOrderEndpoints(
        this IEndpointRouteBuilder app, IMongoCollection<User> users, CustomerOrderService service)
    {
        var currentUsers = new CurrentUserService(users);
        var group = app.MapGroup("/api/customers/me/orders").RequireAuthorization();

        group.MapGet("", async (HttpContext c, string? status, string? q, string? from, string? to, int? page, int? pageSize) =>
        {
            var (user, error) = await currentUsers.ResolveAsync(c, UserRoles.Customer);
            if (error is not null) return error;
            var errors = new Dictionary<string, string[]>();
            if (!CustomerOrderService.IsStatusFilter(status))
                errors["status"] = ["Status must be all, active, shipping, delivered, cancelled, delivery_failed or a list of order statuses."];
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
            var bad = Paging.TryGet(page, pageSize, out var p, out var ps, defaultSize: 10, maxSize: 50);
            if (bad is not null) return bad;
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            return Results.Ok(await service.ListAsync(user!.UserId, status, q, fromUtc, toUtc, p, ps));
        });

        group.MapGet("/counts", async (HttpContext c) =>
        {
            var (user, error) = await currentUsers.ResolveAsync(c, UserRoles.Customer);
            return error ?? Results.Ok(await service.CountsAsync(user!.UserId));
        });

        group.MapGet("/{orderCode}", async (HttpContext c, string orderCode) =>
        {
            var (user, error) = await currentUsers.ResolveAsync(c, UserRoles.Customer);
            if (error is not null) return error;
            var detail = await service.GetAsync(user!.UserId, orderCode);
            return detail is null ? Results.NotFound(new { message = "Order not found." }) : Results.Ok(detail);
        });

        group.MapPost("/{orderCode}/cancel", async (HttpContext c, string orderCode, CancelOrderRequest request) =>
        {
            var (user, error) = await currentUsers.ResolveAsync(c, UserRoles.Customer);
            if (error is not null) return error;
            var (detail, cancelError) = await service.CancelAsync(user!.UserId, orderCode, request.Reason);
            return cancelError ?? Results.Ok(detail);
        });
    }
}
