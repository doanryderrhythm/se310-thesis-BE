using AuthApi.Security;
using MongoDB.Driver;

namespace AuthApi.Features.Customers;

internal sealed class CustomerService(IMongoCollection<User> users)
{
    /// <summary>
    /// Updates the supplied profile fields. A null <paramref name="fullName"/> or
    /// <paramref name="email"/> leaves that field unchanged; <paramref name="clearEmail"/> removes the email.
    /// </summary>
    public async Task<User?> UpdateProfileAsync(User user, string? fullName, string? email, bool clearEmail)
    {
        var setName = fullName is not null && fullName != user.FullName;
        var setEmail = email is not null && email != user.Email;
        var unsetEmail = clearEmail && user.Email is not null;
        if (!setName && !setEmail && !unsetEmail)
        {
            return user;
        }

        var filter = Builders<User>.Filter.And(
            Builders<User>.Filter.Eq(item => item.UserId, user.UserId),
            Builders<User>.Filter.Eq(item => item.Role, UserRoles.Customer),
            Builders<User>.Filter.Eq(item => item.Status, "active"));
        var update = Builders<User>.Update.Set(item => item.UpdatedAt, DateTime.UtcNow);
        if (setName) update = update.Set(item => item.FullName, fullName!);
        if (setEmail) update = update.Set(item => item.Email, email);
        if (unsetEmail) update = update.Unset(item => item.Email);
        return await users.FindOneAndUpdateAsync(filter, update,
            new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After });
    }
}
