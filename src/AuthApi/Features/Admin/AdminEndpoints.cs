using AuthApi.Features.Auth;
using AuthApi.Security;
using MongoDB.Driver;

namespace AuthApi.Features.Admin;

internal static class AdminEndpoints
{
    public static void MapAdminEndpoints(
        this IEndpointRouteBuilder app, IMongoCollection<User> users, IMongoCollection<Counter> counters)
    {
        var currentUsers = new CurrentUserService(users);
        var admins = new AdminService(users);
        var accounts = new AccountService(users, counters);
        var group = app.MapGroup("/api/admin").RequireAuthorization();
        group.MapGet("/customers", (HttpContext context, int? page, int? pageSize) =>
            GetCustomersAsync(context, page, pageSize, currentUsers, admins));
        group.MapPost("/admins", (HttpContext context, global::RegisterRequest request) =>
            CreateAdminAsync(context, request, currentUsers, accounts));
    }

    private static async Task<IResult> GetCustomersAsync(
        HttpContext context, int? page, int? pageSize,
        CurrentUserService currentUsers, AdminService admins)
    {
        var (_, error) = await currentUsers.ResolveAsync(context, UserRoles.Admin);
        if (error is not null)
        {
            return error;
        }

        var currentPage = 1;
        if (page.HasValue)
        {
            currentPage = page.Value;
        }

        var currentPageSize = 20;
        if (pageSize.HasValue)
        {
            currentPageSize = pageSize.Value;
        }
        if (currentPage < 1 || currentPageSize < 1 || currentPageSize > 100 ||
            (long)(currentPage - 1) * currentPageSize > int.MaxValue)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["page"] = ["page must be at least 1 and pageSize must be between 1 and 100."]
            });
        }

        return Results.Ok(await admins.GetCustomersAsync(currentPage, currentPageSize));
    }

    private static async Task<IResult> CreateAdminAsync(
        HttpContext context, global::RegisterRequest request,
        CurrentUserService currentUsers, AccountService accounts)
    {
        var (_, error) = await currentUsers.ResolveAsync(context, UserRoles.Admin);
        if (error is not null)
        {
            return error;
        }

        var validationErrors = AccountValidator.ValidateRegistration(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        if (await accounts.PhoneExistsAsync(request.Phone))
        {
            return Results.Conflict(new { message = "A user with this phone number already exists." });
        }

        try
        {
            var admin = await accounts.CreateAsync(request, UserRoles.Admin);
            return Results.Json(
                new AdminResponse(admin.UserId, admin.FullName, admin.Phone, admin.Role,
                    admin.Status, admin.CreatedAt),
                statusCode: StatusCodes.Status201Created);
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Code == 11000)
        {
            return Results.Conflict(new { message = "A user with this phone number already exists." });
        }
    }
}
