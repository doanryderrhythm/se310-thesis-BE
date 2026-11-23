using AuthApi.Security;
using MongoDB.Driver;

namespace AuthApi.Features.Promotions;

internal static class CouponEndpoints
{
    public static void MapCouponEndpoints(
        this IEndpointRouteBuilder app, IMongoCollection<User> users, CouponService coupons)
    {
        var currentUsers = new CurrentUserService(users);

        app.MapGet("/api/coupons", async () => Results.Ok(await coupons.GetPublicCouponsAsync()));

        app.MapGet("/api/customers/me/vouchers", async (HttpContext context, string? status) =>
        {
            var (user, error) = await currentUsers.ResolveAsync(context, UserRoles.Customer);
            if (error is not null) return error;
            var filter = string.IsNullOrEmpty(status) ? "available" : status;
            if (filter is not ("available" or "used" or "expired" or "all"))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = ["Status must be available, used, expired or all."]
                });
            }

            return Results.Ok(await coupons.GetMyVouchersAsync(user!.UserId, filter));
        }).RequireAuthorization();

        app.MapPost("/api/coupons/validate", async (HttpContext context, ValidateCouponRequest request) =>
        {
            var (user, error) = await currentUsers.ResolveAsync(context, UserRoles.Customer);
            if (error is not null) return error;

            var errors = new Dictionary<string, string[]>();
            var hasCode = !string.IsNullOrWhiteSpace(request.Code);
            if (hasCode == request.UserCouponId.HasValue)
                errors["couponCode"] = ["Provide either code or userCouponId."];
            ValidateItems(request.Items, errors);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var outcome = await coupons.QuoteAsync(user!.UserId, request.Code, request.UserCouponId, request.Items!);
            if (outcome.ItemsError is not null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["items"] = [outcome.ItemsError] });
            }

            return outcome.CouponError is not null
                ? Results.ValidationProblem(new Dictionary<string, string[]> { ["couponCode"] = [outcome.CouponError] })
                : Results.Ok(outcome.Quote);
        }).RequireAuthorization();

        // The checkout "choose a coupon" list: wallet vouchers + public codes judged against the cart.
        app.MapPost("/api/coupons/applicable", async (HttpContext context, ApplicableCouponsRequest request) =>
        {
            var (user, error) = await currentUsers.ResolveAsync(context, UserRoles.Customer);
            if (error is not null) return error;

            var errors = new Dictionary<string, string[]>();
            ValidateItems(request.Items, errors);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var (options, itemsError) = await coupons.GetApplicableAsync(user!.UserId, request.Items!);
            return itemsError is not null
                ? Results.ValidationProblem(new Dictionary<string, string[]> { ["items"] = [itemsError] })
                : Results.Ok(options);
        }).RequireAuthorization();
    }

    private static void ValidateItems(CouponItemRequest[]? items, Dictionary<string, string[]> errors)
    {
        if (items is not { Length: > 0 } || items.Length > CheckoutPricing.MaxCartLines)
            errors["items"] = [$"Provide between 1 and {CheckoutPricing.MaxCartLines} items."];
        else if (items.Any(i => i.VariantId is null or < 1 || i.Quantity is null or < 1 or > 99))
            errors["items"] = ["Each item needs a variantId and a quantity between 1 and 99."];
    }
}
