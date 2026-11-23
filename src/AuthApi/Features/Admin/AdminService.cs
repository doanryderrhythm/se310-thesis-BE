using AuthApi.Security;
using MongoDB.Driver;

namespace AuthApi.Features.Admin;

internal sealed class AdminService(IMongoCollection<User> users, AuthApi.Features.Promotions.OrderSpendService spend)
{
    public async Task<CustomerPageResponse> GetCustomersAsync(int page, int pageSize, string? q = null, string? status = null)
    {
        var filter = Builders<User>.Filter.Eq(user => user.Role, UserRoles.Customer);
        if (!string.IsNullOrWhiteSpace(status)) filter &= Builders<User>.Filter.Eq(user => user.Status, status);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = new MongoDB.Bson.BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(q.Trim()), "i");
            filter &= Builders<User>.Filter.Regex(user => user.FullName, pattern)
                | Builders<User>.Filter.Regex(user => user.Phone, pattern)
                | Builders<User>.Filter.Regex(user => user.Email, pattern);
        }

        var totalCount = await users.CountDocumentsAsync(filter);
        var customers = await users.Find(filter)
            .Sort(Builders<User>.Sort.Descending(user => user.CreatedAt).Descending(user => user.UserId))
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();
        var stats = await spend.StatsForUsersAsync(customers.Select(user => user.UserId));
        return new CustomerPageResponse(
            customers.Select(user => new CustomerListItem(user.UserId, user.FullName, user.Phone, user.Email,
                user.Status, user.CreatedAt,
                stats.GetValueOrDefault(user.UserId)?.OrderCount ?? 0,
                stats.GetValueOrDefault(user.UserId)?.TotalSpent ?? 0)).ToArray(),
            page, pageSize, totalCount);
    }
}
