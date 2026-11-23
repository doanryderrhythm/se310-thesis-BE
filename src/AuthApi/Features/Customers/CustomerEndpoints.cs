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
        IMongoCollection<Counter> counters,
        AuthApi.Features.Locations.LocationService locations)
    {
        var currentUsers = new CurrentUserService(users);
        var customers = new CustomerService(users);
        var addresses = new DeliveryAddressService(deliveryAddresses, counters);
        var group = app.MapGroup("/api/customers").RequireAuthorization();
        Func<HttpContext, Task<IResult>> getMe = context => GetMeAsync(context, currentUsers);
        Func<HttpContext, Task<IResult>> getMyDeliveryAddresses =
            context => GetMyDeliveryAddressesAsync(context, currentUsers, addresses);
        Func<HttpContext, CreateDeliveryAddressRequest, Task<IResult>> addMyDeliveryAddress =
            (context, request) => AddMyDeliveryAddressAsync(context, request, currentUsers, addresses, locations);
        Func<HttpContext, long, CreateDeliveryAddressRequest, Task<IResult>> updateMyDeliveryAddress =
            (context, addressId, request) =>
                UpdateMyDeliveryAddressAsync(context, addressId, request, currentUsers, addresses, locations);
        Func<HttpContext, long, Task<IResult>> deleteMyDeliveryAddress =
            (context, addressId) =>
                DeleteMyDeliveryAddressAsync(context, addressId, currentUsers, addresses);
        group.MapGet("/me", getMe);
        group.MapGet("/me/addresses", getMyDeliveryAddresses);
        group.MapPost("/me/addresses", addMyDeliveryAddress);
        group.MapPut("/me/addresses/{addressId:long}", updateMyDeliveryAddress);
        group.MapDelete("/me/addresses/{addressId:long}", deleteMyDeliveryAddress);
        group.MapPatch("/me", (HttpContext context, UpdateProfileRequest request) =>
            UpdateMeAsync(context, request, currentUsers, customers));
    }

    private static async Task<IResult> DeleteMyDeliveryAddressAsync(
        HttpContext context,
        long addressId,
        CurrentUserService currentUsers,
        DeliveryAddressService addresses)
    {
        var (user, error) = await currentUsers.ResolveAsync(context, UserRoles.Customer);
        if (error is not null)
        {
            return error;
        }

        if (addressId <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["addressId"] = ["Address ID must be a positive number."]
            });
        }

        var deleted = await addresses.DeleteForUserAsync(user!.UserId, addressId);
        return deleted ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> UpdateMyDeliveryAddressAsync(
        HttpContext context,
        long addressId,
        CreateDeliveryAddressRequest request,
        CurrentUserService currentUsers,
        DeliveryAddressService addresses,
        AuthApi.Features.Locations.LocationService locations)
    {
        var (user, error) = await currentUsers.ResolveAsync(context, UserRoles.Customer);
        if (error is not null)
        {
            return error;
        }

        if (addressId <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["addressId"] = ["Address ID must be a positive number."]
            });
        }

        var (normalized, errors) = await NormalizeAddressAsync(request, locations);
        if (normalized is null)
        {
            return Results.ValidationProblem(errors);
        }

        var address = await addresses.UpdateForUserAsync(user!.UserId, addressId, normalized);
        return address is null ? Results.NotFound() : Results.Ok(address);
    }

    private static async Task<IResult> AddMyDeliveryAddressAsync(
        HttpContext context,
        CreateDeliveryAddressRequest request,
        CurrentUserService currentUsers,
        DeliveryAddressService addresses,
        AuthApi.Features.Locations.LocationService locations)
    {
        var (user, error) = await currentUsers.ResolveAsync(context, UserRoles.Customer);
        if (error is not null)
        {
            return error;
        }

        var (normalized, errors) = await NormalizeAddressAsync(request, locations);
        if (normalized is null)
        {
            return Results.ValidationProblem(errors);
        }

        var address = await addresses.CreateForUserAsync(user!.UserId, normalized);
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

    /// <summary>
    /// Validates and normalizes an address: whitespace collapsed, phone in 0xxxxxxxxx form,
    /// province/district/ward checked against the location catalog (codes or names).
    /// </summary>
    private static async Task<(NormalizedAddress? Address, Dictionary<string, string[]> Errors)> NormalizeAddressAsync(
        CreateDeliveryAddressRequest request, AuthApi.Features.Locations.LocationService locations)
    {
        var errors = new Dictionary<string, string[]>();
        var name = AuthApi.Common.Text.Clean(request.RecipientName);
        if (name is null || name.Length is < 2 or > 100)
        {
            errors["recipientName"] = ["Recipient name is required (2-100 characters)."];
        }

        var phone = AuthApi.Common.PhoneNumbers.Normalize(request.RecipientPhone);
        if (!AuthApi.Common.PhoneNumbers.IsValidMobile(phone))
        {
            errors["recipientPhone"] = [AuthApi.Common.PhoneNumbers.RuleMessage];
        }

        var line = AuthApi.Common.Text.Clean(request.AddressLine);
        if (line is null || line.Length is < 5 or > 255)
        {
            errors["addressLine"] = ["Address line (house number, street) is required (5-255 characters)."];
        }

        var (location, locationErrors) = await locations.ResolveAsync(
            request.ProvinceCode, request.DistrictCode, request.WardCode,
            request.Province, request.District, request.Ward);
        foreach (var (key, value) in locationErrors) errors[key] = value;

        return errors.Count > 0
            ? (null, errors)
            : (new NormalizedAddress(name!, phone!, line!, location!, request.IsDefault ?? false), errors);
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

        // Omitted (null) fields are left unchanged; an empty email clears the stored email.
        var errors = new Dictionary<string, string[]>();
        if (request.FullName is null && request.Email is null)
        {
            errors["fullName"] = ["Provide fullName and/or email to update."];
        }

        if (request.FullName is not null
            && (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length > 100))
        {
            errors["fullName"] = ["Full name is required and must be at most 100 characters."];
        }

        var clearEmail = request.Email is not null && string.IsNullOrWhiteSpace(request.Email);
        var email = AuthApi.Features.Auth.AccountValidator.NormalizeEmail(request.Email, out var emailError);
        if (emailError is not null)
        {
            errors["email"] = [emailError];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var updated = await customers.UpdateProfileAsync(user!, request.FullName?.Trim(), email, clearEmail);
        if (updated is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(ToProfile(updated));
    }

    private static CustomerProfileResponse ToProfile(User user) =>
        new(user.UserId, user.FullName, user.Phone, user.Email, user.Role, user.CreatedAt, user.UpdatedAt);
}
