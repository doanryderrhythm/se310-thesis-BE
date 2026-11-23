using AuthApi.Security;
using MongoDB.Driver;

namespace AuthApi.Features.Orders;

internal static class PurchaseEndpoints
{
    public static void MapPurchaseEndpoints(
        this IEndpointRouteBuilder app,
        IMongoCollection<User> users,
        PurchaseService purchases)
    {
        var currentUsers = new CurrentUserService(users);

        async Task<IResult> PurchaseAsync(HttpContext context, PurchaseRequest request)
        {
            var (user, error) = await currentUsers.ResolveAsync(context, UserRoles.Customer);
            if (error is not null) return error;
            var idempotencyKey = context.Request.Headers["Idempotency-Key"].FirstOrDefault()?.Trim();
            var (response, purchaseError) = await purchases.PurchaseAsync(
                user!.UserId, request, idempotencyKey);
            return purchaseError ?? (response is null ? Results.Problem() : Results.Created($"/api/orders/{response.OrderCode}", response));
        }

        app.MapPost("/api/orders", PurchaseAsync).RequireAuthorization();
        app.MapPost("/api/orders/purchase", PurchaseAsync).RequireAuthorization();
    }
}
