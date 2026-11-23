using AuthApi.Features.Customers;
using AuthApi.Features.Orders;
using AuthApi.Features.Promotions;
using AuthApi.Features.Reviews;
using AuthApi.Security;
using MongoDB.Driver;

namespace AuthApi.Features.Admin;

internal sealed class AdminCustomerService(IMongoDatabase db, OrderSpendService spend)
{
    private readonly IMongoCollection<User> users = db.GetCollection<User>("users");
    private readonly IMongoCollection<DeliveryAddressDocument> addresses = db.GetCollection<DeliveryAddressDocument>("addresses");
    private readonly IMongoCollection<OrderDocument> orders = db.GetCollection<OrderDocument>("orders");
    private readonly IMongoCollection<UserCouponDocument> vouchers = db.GetCollection<UserCouponDocument>("user_coupons");
    private readonly IMongoCollection<ProductReviewDocument> reviews = db.GetCollection<ProductReviewDocument>("product_reviews");

    public async Task<AdminCustomerDetail?> GetAsync(long userId)
    {
        var user = await users.Find(u => u.UserId == userId && u.Role == UserRoles.Customer).FirstOrDefaultAsync();
        if (user is null) return null;

        var now = DateTime.UtcNow;
        var addressList = await addresses.Find(a => a.UserId == userId)
            .SortByDescending(a => a.IsDefault).ThenBy(a => a.AddressId).ToListAsync();
        var stats = (await spend.StatsForUsersAsync([userId])).GetValueOrDefault(userId) ?? new UserOrderStats(0, 0, 0, null);
        var recent = await orders.Find(o => o.UserId == userId).SortByDescending(o => o.PlacedAt).Limit(5).ToListAsync();
        var available = await vouchers.CountDocumentsAsync(v => v.UserId == userId && v.Status == UserCouponDocument.Available && v.ExpiresAt > now);
        var used = await vouchers.CountDocumentsAsync(v => v.UserId == userId && v.Status == UserCouponDocument.Used);
        var expired = await vouchers.CountDocumentsAsync(v => v.UserId == userId && v.Status == UserCouponDocument.Available && v.ExpiresAt <= now);

        return new AdminCustomerDetail(
            user.UserId, user.FullName, user.Phone, user.Email, user.Status, user.StatusReason, user.StatusChangedAt,
            user.CreatedAt, user.UpdatedAt,
            addressList.Select(a => new DeliveryAddressResponse(a.AddressId, a.RecipientName, a.RecipientPhone,
                a.AddressLine, a.Ward, a.District, a.Province, a.IsDefault, a.CreatedAt,
                a.ProvinceCode, a.DistrictCode, a.WardCode)).ToArray(),
            new CustomerStatsResponse(stats.OrderCount, stats.DeliveredCount, stats.TotalSpent, stats.LastOrderAt),
            recent.Select(o => new CustomerRecentOrder(o.OrderCode, o.PlacedAt, o.TotalAmount, o.Status)).ToArray(),
            new CustomerVoucherCounts(available, used, expired),
            await reviews.CountDocumentsAsync(r => r.UserId == userId));
    }

    /// <summary>Locking keeps the order history but blocks login and invalidates the current token on its next use.</summary>
    public async Task<(AdminCustomerDetail? Detail, IResult? Error)> SetLockedAsync(long userId, bool locked, string? reason)
    {
        var trimmed = reason?.Trim();
        if (locked && (trimmed is null || trimmed.Length is < 10 or > 300))
        {
            return (null, Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["reason"] = ["Reason must be between 10 and 300 characters."]
            }));
        }

        var now = DateTime.UtcNow;
        var from = locked ? "active" : "locked";
        var update = Builders<User>.Update.Set(u => u.Status, locked ? "locked" : "active")
            .Set(u => u.StatusChangedAt, now).Set(u => u.UpdatedAt, now);
        update = locked ? update.Set(u => u.StatusReason, trimmed) : update.Unset(u => u.StatusReason);
        var changed = await users.UpdateOneAsync(
            u => u.UserId == userId && u.Role == UserRoles.Customer && u.Status == from, update);
        if (changed.ModifiedCount == 0)
        {
            return await users.Find(u => u.UserId == userId && u.Role == UserRoles.Customer).AnyAsync()
                ? (null, Results.Conflict(new { message = locked ? "The account is already locked." : "The account is not locked." }))
                : (null, Results.NotFound());
        }

        return (await GetAsync(userId), null);
    }
}
