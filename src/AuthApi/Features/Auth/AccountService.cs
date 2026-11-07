using AuthApi.Security;
using MongoDB.Driver;

namespace AuthApi.Features.Auth;

internal sealed class AccountService(IMongoCollection<User> users, IMongoCollection<Counter> counters)
{
    public Task<bool> PhoneExistsAsync(string? phone) =>
        users.Find(user => user.Phone == phone).AnyAsync();

    public async Task<User> CreateAsync(global::RegisterRequest request, string role)
    {
        var now = DateTime.UtcNow;
        var user = new User
        {
            UserId = await NextUserIdAsync(),
            FullName = request.FullName!.Trim(),
            Phone = request.Phone!,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password!, workFactor: 12),
            Role = role,
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };
        await users.InsertOneAsync(user);
        return user;
    }

    private async Task<long> NextUserIdAsync()
    {
        var counter = await counters.FindOneAndUpdateAsync(
            item => item.Id == "users",
            Builders<Counter>.Update.Inc(item => item.SequenceValue, 1),
            new FindOneAndUpdateOptions<Counter>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After
            });
        return counter.SequenceValue;
    }
}
