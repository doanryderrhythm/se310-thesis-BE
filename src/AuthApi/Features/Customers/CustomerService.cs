using AuthApi.Security;
using MongoDB.Driver;

namespace AuthApi.Features.Customers;

internal sealed class CustomerService(IMongoCollection<User> users)
{
    public async Task<User?> UpdateFullNameAsync(User user, string fullName)
    {
        if (user.FullName == fullName)
        {
            return user;
        }

        var filter = Builders<User>.Filter.And(
            Builders<User>.Filter.Eq(item => item.UserId, user.UserId),
            Builders<User>.Filter.Eq(item => item.Role, UserRoles.Customer),
            Builders<User>.Filter.Eq(item => item.Status, "active"));
        var update = Builders<User>.Update
            .Set(item => item.FullName, fullName)
            .Set(item => item.UpdatedAt, DateTime.UtcNow);
        return await users.FindOneAndUpdateAsync(filter, update,
            new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After });
    }
}
