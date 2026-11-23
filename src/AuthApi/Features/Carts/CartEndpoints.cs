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

        group.MapPatch("/items/{variantId:long}", async (HttpContext context, long variantId, UpdateCartItemRequest request) =>
        {
            var (user, error) = await currentUsers.ResolveAsync(context, UserRoles.Customer);
            if (error is not null) return error;
            var errors = new Dictionary<string, string[]>();
            if (request.Quantity is null && request.Selected is null)
                errors["quantity"] = ["Send quantity, selected or both."];
            if (request.Quantity is < 1 or > CartService.MaxQuantityPerAdd)
                errors["quantity"] = [$"Quantity must be between 1 and {CartService.MaxQuantityPerAdd}."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var (cart, updateError) = await carts.UpdateItemAsync(user!.UserId, variantId, request.Quantity, request.Selected);
            return updateError ?? Results.Ok(cart);
        });

        group.MapPost("/merge", async (HttpContext context, MergeCartRequest request) =>
        {
            var (user, error) = await currentUsers.ResolveAsync(context, UserRoles.Customer);
            if (error is not null) return error;
            var input = request.Items ?? [];
            var errors = new Dictionary<string, string[]>();
            if (input.Length is 0 or > 50) errors["items"] = ["Send between 1 and 50 cart lines."];
            else if (input.Any(i => i.VariantId is null or < 1 || i.Quantity is null or < 1 or > CartService.MaxQuantityPerAdd))
                errors["items"] = [$"Every line needs a variantId and a quantity between 1 and {CartService.MaxQuantityPerAdd}."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            // The same variant sent twice is summed (capped later by stock).
            var lines = input.GroupBy(i => i.VariantId!.Value)
                .Select(g => (VariantId: g.Key, Quantity: Math.Min(g.Sum(i => i.Quantity!.Value), CartService.MaxQuantityPerAdd)))
                .ToList();
            return Results.Ok(await carts.MergeAsync(user!.UserId, lines));
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
