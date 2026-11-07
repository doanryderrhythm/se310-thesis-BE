using AuthApi.Security;
using MongoDB.Driver;

namespace AuthApi.Features.Customers;

internal static class CustomerEndpoints
{
    public static void MapCustomerEndpoints(this IEndpointRouteBuilder app, IMongoCollection<User> users)
    {
        var currentUsers = new CurrentUserService(users);
        var customers = new CustomerService(users);
        var group = app.MapGroup("/api/customers").RequireAuthorization();
        Func<HttpContext, Task<IResult>> getMe = context => GetMeAsync(context, currentUsers);
        group.MapGet("/me", getMe);
        group.MapPatch("/me", (HttpContext context, UpdateProfileRequest request) =>
            UpdateMeAsync(context, request, currentUsers, customers));
    }

    private static async Task<IResult> GetMeAsync(
        HttpContext context, CurrentUserService currentUsers)
    {
        var (user, error) = await currentUsers.ResolveAsync(context, UserRoles.Customer);
        if (error is not null)
        {
            return error;
        }

        return Results.Ok(ToProfile(user!));
    }

    private static async Task<IResult> UpdateMeAsync(
        HttpContext context, UpdateProfileRequest request,
        CurrentUserService currentUsers, CustomerService customers)
    {
        var (user, error) = await currentUsers.ResolveAsync(context, UserRoles.Customer);
        if (error is not null)
        {
            return error;
        }

        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length > 100)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["fullName"] = ["Full name is required and must be at most 100 characters."]
            });
        }

        var updated = await customers.UpdateFullNameAsync(user!, request.FullName.Trim());
        if (updated is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(ToProfile(updated));
    }

    private static CustomerProfileResponse ToProfile(User user) =>
        new(user.UserId, user.FullName, user.Phone, user.Role, user.CreatedAt, user.UpdatedAt);
}
