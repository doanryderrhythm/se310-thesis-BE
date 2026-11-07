using AuthApi.Security;
using MongoDB.Driver;

namespace AuthApi.Features.Admin;

internal sealed class AdminService(IMongoCollection<User> users)
{
    public async Task<CustomerPageResponse> GetCustomersAsync(int page, int pageSize)
    {
        var filter = Builders<User>.Filter.Eq(user => user.Role, UserRoles.Customer);
        var totalCount = await users.CountDocumentsAsync(filter);
        var customers = await users.Find(filter)
            .Sort(Builders<User>.Sort.Descending(user => user.CreatedAt).Descending(user => user.UserId))
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();
        return new CustomerPageResponse(
            customers.Select(user => new CustomerListItem(user.UserId, user.FullName, user.Phone,
                user.Status, user.CreatedAt)).ToArray(),
            page, pageSize, totalCount);
    }
}
