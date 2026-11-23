using AuthApi.Data;
using AuthApi.Security;
using MongoDB.Driver;

namespace AuthApi.Features.Notifications;

internal static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(
        this IEndpointRouteBuilder app, IMongoCollection<User> users, NotificationService notifications)
    {
        var currentUsers = new CurrentUserService(users);
        var group = app.MapGroup("/api/customers/me/notifications").RequireAuthorization();

        group.MapGet("", async (HttpContext context, bool? unreadOnly, int? page, int? pageSize) =>
        {
            var (user, error) = await currentUsers.ResolveAsync(context, UserRoles.Customer);
            if (error is not null) return error;

            var currentPage = page ?? 1;
            var currentPageSize = pageSize ?? 20;
            if (currentPage < 1 || currentPageSize < 1 || currentPageSize > 100 ||
                (long)(currentPage - 1) * currentPageSize > int.MaxValue)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["page"] = ["page must be at least 1 and pageSize must be between 1 and 100."]
                });
            }

            return Results.Ok(await notifications.ListAsync(
                user!.UserId, unreadOnly ?? false, currentPage, currentPageSize));
        });

        group.MapGet("/unread-count", async (HttpContext context) =>
        {
            var (user, error) = await currentUsers.ResolveAsync(context, UserRoles.Customer);
            return error ?? Results.Ok(new UnreadCountResponse(await notifications.UnreadCountAsync(user!.UserId)));
        });

        group.MapPost("/read-all", async (HttpContext context) =>
        {
            var (user, error) = await currentUsers.ResolveAsync(context, UserRoles.Customer);
            if (error is not null) return error;
            await notifications.MarkAllReadAsync(user!.UserId);
            return Results.NoContent();
        });

        group.MapPost("/{notificationId:long}/read", async (HttpContext context, long notificationId) =>
        {
            var (user, error) = await currentUsers.ResolveAsync(context, UserRoles.Customer);
            if (error is not null) return error;
            return await notifications.MarkReadAsync(user!.UserId, notificationId)
                ? Results.NoContent()
                : Results.NotFound();
        });
    }
}
