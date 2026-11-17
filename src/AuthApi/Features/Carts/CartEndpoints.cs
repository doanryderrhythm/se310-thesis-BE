using AuthApi.Data;
using AuthApi.Features.Products;
using AuthApi.Security;
using MongoDB.Driver;

namespace AuthApi.Features.Carts;

internal static class CartEndpoints
{
    public static void MapCartEndpoints(
        this IEndpointRouteBuilder app, IMongoDatabase db, IMongoCollection<User> users,
        CounterService counters)
    {
        var currentUsers = new CurrentUserService(users);
        var carts = new CartService(db, counters, new ProductCatalogService(db));
        var group = app.MapGroup("/api/cart").RequireAuthorization();

        group.MapGet("", async (HttpContext context) =>
        {
            var (user, error) = await currentUsers.ResolveAsync(context, UserRoles.Customer);
            return error ?? Results.Ok(await carts.GetAsync(user!.UserId));
        });

        group.MapPost("/items", async (HttpContext context, AddCartItemRequest request) =>
        {
            var (user, error) = await currentUsers.ResolveAsync(context, UserRoles.Customer);
            if (error is not null) return error;

            var errors = new Dictionary<string, string[]>();
            if (request.VariantId is null or < 1)
                errors["variantId"] = ["Variant id is required."];
            if (request.Quantity is null or < 1 or > CartService.MaxQuantityPerAdd)
                errors["quantity"] = [$"Quantity must be between 1 and {CartService.MaxQuantityPerAdd}."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var (cart, addError) = await carts.AddItemAsync(
                user!.UserId, request.VariantId!.Value, request.Quantity!.Value);
            return addError ?? Results.Ok(cart);
        });

        group.MapDelete("/items/{variantId:long}", async (HttpContext context, long variantId) =>
        {
            var (user, error) = await currentUsers.ResolveAsync(context, UserRoles.Customer);
            if (error is not null) return error;
            var cart = await carts.RemoveItemAsync(user!.UserId, variantId);
            return cart is null
                ? Results.NotFound(new { message = "Cart item not found." })
                : Results.Ok(cart);
        });
    }
}
