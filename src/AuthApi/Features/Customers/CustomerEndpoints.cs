using System.Text.RegularExpressions;
using AuthApi.Security;
using MongoDB.Driver;

namespace AuthApi.Features.Customers;

internal static class CustomerEndpoints
{
    public static void MapCustomerEndpoints(
        this IEndpointRouteBuilder app,
        IMongoCollection<User> users,
        IMongoCollection<DeliveryAddressDocument> deliveryAddresses,
        IMongoCollection<Counter> counters)
    {
        var currentUsers = new CurrentUserService(users);
        var customers = new CustomerService(users);
        var addresses = new DeliveryAddressService(deliveryAddresses, counters);
        var group = app.MapGroup("/api/customers").RequireAuthorization();
        Func<HttpContext, Task<IResult>> getMe = context => GetMeAsync(context, currentUsers);
        Func<HttpContext, Task<IResult>> getMyDeliveryAddresses =
            context => GetMyDeliveryAddressesAsync(context, currentUsers, addresses);
        Func<HttpContext, CreateDeliveryAddressRequest, Task<IResult>> addMyDeliveryAddress =
            (context, request) => AddMyDeliveryAddressAsync(context, request, currentUsers, addresses);
        group.MapGet("/me", getMe);
        group.MapGet("/me/addresses", getMyDeliveryAddresses);
        group.MapPost("/me/addresses", addMyDeliveryAddress);
        group.MapPatch("/me", (HttpContext context, UpdateProfileRequest request) =>
            UpdateMeAsync(context, request, currentUsers, customers));
    }

    private static async Task<IResult> AddMyDeliveryAddressAsync(
        HttpContext context,
        CreateDeliveryAddressRequest request,
        CurrentUserService currentUsers,
        DeliveryAddressService addresses)
    {
        var (user, error) = await currentUsers.ResolveAsync(context, UserRoles.Customer);
        if (error is not null)
        {
            return error;
        }

        var errors = ValidateDeliveryAddress(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var address = await addresses.CreateForUserAsync(user!.UserId, request);
        return Results.Created("/api/customers/me/addresses", address);
    }

    private static async Task<IResult> GetMyDeliveryAddressesAsync(
        HttpContext context,
        CurrentUserService currentUsers,
        DeliveryAddressService addresses)
    {
        var (user, error) = await currentUsers.ResolveAsync(context, UserRoles.Customer);
        if (error is not null)
        {
            return error;
        }

        return Results.Ok(await addresses.GetForUserAsync(user!.UserId));
    }

    private static Dictionary<string, string[]> ValidateDeliveryAddress(
        CreateDeliveryAddressRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        AddRequiredFieldError(errors, "recipientName", "Recipient name", request.RecipientName, 100);
        if (request.RecipientPhone is null
            || !Regex.IsMatch(
                request.RecipientPhone, @"\A[0-9]{10}\z", RegexOptions.CultureInvariant))
        {
            errors["recipientPhone"] = ["Recipient phone must contain exactly 10 digits."];
        }

        AddRequiredFieldError(errors, "addressLine", "Address line", request.AddressLine, 255);
        AddRequiredFieldError(errors, "ward", "Ward", request.Ward, 100);
        AddRequiredFieldError(errors, "district", "District", request.District, 100);
        AddRequiredFieldError(errors, "province", "Province", request.Province, 100);
        return errors;
    }

    private static void AddRequiredFieldError(
        Dictionary<string, string[]> errors,
        string field,
        string label,
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maxLength)
        {
            errors[field] = [$"{label} is required and must be at most {maxLength} characters."];
        }
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
