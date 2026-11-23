using AuthApi.Common;
using AuthApi.Security;
using MongoDB.Driver;

namespace AuthApi.Features.Returns;

internal static class ReturnEndpoints
{
    public static void MapReturnEndpoints(
        this IEndpointRouteBuilder app, IMongoCollection<User> users, ReturnService returns)
    {
        var currentUsers = new CurrentUserService(users);

        // ---- Customer
        var mine = app.MapGroup("/api/customers/me").RequireAuthorization();
        mine.MapGet("/returns", async (HttpContext c) =>
        {
            var (user, error) = await currentUsers.ResolveAsync(c, UserRoles.Customer);
            return error ?? Results.Ok(await returns.ListMineAsync(user!.UserId));
        });
        mine.MapGet("/returns/{returnCode}", async (HttpContext c, string returnCode) =>
        {
            var (user, error) = await currentUsers.ResolveAsync(c, UserRoles.Customer);
            if (error is not null) return error;
            var detail = await returns.GetMineAsync(user!.UserId, returnCode);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });
        mine.MapPost("/returns", async (HttpContext c, CreateReturnRequest request) =>
        {
            var (user, error) = await currentUsers.ResolveAsync(c, UserRoles.Customer);
            if (error is not null) return error;
            var (created, createError) = await returns.CreateAsync(user!.UserId, request);
            return createError ?? Results.Created($"/api/customers/me/returns/{created!.ReturnCode}", created);
        });
        mine.MapGet("/orders/{orderCode}/returnable-items", async (HttpContext c, string orderCode) =>
        {
            var (user, error) = await currentUsers.ResolveAsync(c, UserRoles.Customer);
            if (error is not null) return error;
            var result = await returns.ReturnableAsync(user!.UserId, orderCode);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        // ---- Admin
        var admin = app.MapGroup("/api/admin/returns").RequireAuthorization();
        admin.MapGet("", async (HttpContext c, string? status, string? q, string? from, string? to, int? page, int? pageSize) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(c, UserRoles.Admin);
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
                    .Any(s => !ReturnStatuses.All.Contains(s)))
            {
                errors["status"] = [$"status must be a comma-separated list of: {string.Join(", ", ReturnStatuses.All)}."];
            }
            var bad = Paging.TryGet(page, pageSize, out var p, out var ps);
            if (bad is not null) return bad;
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            return Results.Ok(await returns.ListAsync(status, q, fromUtc, toUtc, p, ps));
        });
        admin.MapGet("/counts", async (HttpContext c) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(c, UserRoles.Admin);
            return error ?? Results.Ok(await returns.CountsAsync());
        });
        admin.MapGet("/{returnCode}", async (HttpContext c, string returnCode) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(c, UserRoles.Admin);
            if (error is not null) return error;
            var detail = await returns.GetAsync(returnCode);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });
        admin.MapPost("/{returnCode}/transitions", async (HttpContext c, string returnCode, ReturnTransitionRequest request) =>
        {
            var (admin_, error) = await currentUsers.ResolveAsync(c, UserRoles.Admin);
            if (error is not null) return error;
            var (detail, transitionError) = await returns.TransitionAsync(admin_!.UserId, returnCode, request);
            return transitionError ?? Results.Ok(detail);
        });
    }
}
