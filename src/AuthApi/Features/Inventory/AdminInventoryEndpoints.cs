using AuthApi.Common;
using AuthApi.Security;
using MongoDB.Driver;

namespace AuthApi.Features.Inventory;

internal static class AdminInventoryEndpoints
{
    public static void MapAdminInventoryEndpoints(
        this IEndpointRouteBuilder app, IMongoCollection<User> users, AdminInventoryService inventory)
    {
        var currentUsers = new CurrentUserService(users);
        var group = app.MapGroup("/api/admin/inventory").RequireAuthorization();

        group.MapGet("", async (HttpContext c, string? q, bool? lowStock, string? status, int? page, int? pageSize) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(c, UserRoles.Admin);
            if (error is not null) return error;
            if (status is not (null or "" or "active" or "hidden"))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Status must be active or hidden."] });
            var bad = Paging.TryGet(page, pageSize, out var p, out var ps);
            return bad ?? Results.Ok(await inventory.ListAsync(q, lowStock, status, p, ps));
        });

        group.MapGet("/{variantId:long}", async (HttpContext c, long variantId) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(c, UserRoles.Admin);
            if (error is not null) return error;
            var row = await inventory.GetAsync(variantId);
            return row is null ? Results.NotFound() : Results.Ok(row);
        });

        group.MapPost("/{variantId:long}/adjustments", async (HttpContext c, long variantId, AdjustmentRequest r) =>
        {
            var (admin, error) = await currentUsers.ResolveAsync(c, UserRoles.Admin);
            if (error is not null) return error;
            var (row, adjustError) = await inventory.AdjustAsync(admin!.UserId, variantId, r);
            return adjustError ?? Results.Ok(row);
        });

        group.MapPatch("/{variantId:long}", async (HttpContext c, long variantId, ThresholdRequest r) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(c, UserRoles.Admin);
            if (error is not null) return error;
            var (row, thresholdError) = await inventory.SetThresholdAsync(variantId, r.LowStockThreshold);
            return thresholdError ?? Results.Ok(row);
        });

        group.MapGet("/{variantId:long}/movements", async (HttpContext c, long variantId, int? page, int? pageSize) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(c, UserRoles.Admin);
            if (error is not null) return error;
            var bad = Paging.TryGet(page, pageSize, out var p, out var ps);
            if (bad is not null) return bad;
            var list = await inventory.MovementsAsync(variantId, p, ps);
            return list is null ? Results.NotFound() : Results.Ok(list);
        });
    }
}
