using AuthApi.Data;
using MongoDB.Driver;

namespace AuthApi.Features.Promotions;

internal static class PromotionSetup
{
    /// <summary>Creates the indexes the promotion logic relies on and syncs the id counters.</summary>
    public static async Task SetupAsync(IMongoDatabase db, CounterService counters)
    {
        var campaigns = db.GetCollection<CampaignDocument>("campaigns");
        var coupons = db.GetCollection<CouponDocument>("coupons");
        var userCoupons = db.GetCollection<UserCouponDocument>("user_coupons");
        var usages = db.GetCollection<CouponUsageDocument>("coupon_usages");

        // Names match mongo-init/01-schema.js (`uniqueIndexes`) so both creators agree.
        await coupons.Indexes.CreateOneAsync(new CreateIndexModel<CouponDocument>(
            Builders<CouponDocument>.IndexKeys.Ascending(c => c.Code),
            new CreateIndexOptions { Unique = true, Name = "code_unique" }));
        await coupons.Indexes.CreateOneAsync(new CreateIndexModel<CouponDocument>(
            Builders<CouponDocument>.IndexKeys.Ascending(c => c.CampaignId),
            new CreateIndexOptions { Unique = true, Name = "campaign_id_unique" }));
        await userCoupons.Indexes.CreateOneAsync(new CreateIndexModel<UserCouponDocument>(
            Builders<UserCouponDocument>.IndexKeys.Ascending(u => u.CouponId).Ascending(u => u.UserId),
            new CreateIndexOptions { Unique = true, Name = "coupon_user_unique" }));
        await userCoupons.Indexes.CreateOneAsync(new CreateIndexModel<UserCouponDocument>(
            Builders<UserCouponDocument>.IndexKeys.Ascending(u => u.UserId).Ascending(u => u.Status),
            new CreateIndexOptions { Name = "user_status" }));
        await userCoupons.Indexes.CreateOneAsync(new CreateIndexModel<UserCouponDocument>(
            Builders<UserCouponDocument>.IndexKeys.Ascending(u => u.CampaignId),
            new CreateIndexOptions { Name = "campaign_id_1" }));
        await campaigns.Indexes.CreateOneAsync(new CreateIndexModel<CampaignDocument>(
            Builders<CampaignDocument>.IndexKeys.Ascending(c => c.Trigger).Ascending(c => c.Status),
            new CreateIndexOptions { Name = "trigger_status" }));
        await usages.Indexes.CreateOneAsync(new CreateIndexModel<CouponUsageDocument>(
            Builders<CouponUsageDocument>.IndexKeys.Ascending(u => u.CouponId).Ascending(u => u.UserId),
            new CreateIndexOptions { Name = "coupon_user" }));

        await counters.InitializeAsync("campaigns", await LargestAsync(campaigns, "campaign_id"));
        await counters.InitializeAsync("coupons", await LargestAsync(coupons, "coupon_id"));
        await counters.InitializeAsync("user_coupons", await LargestAsync(userCoupons, "user_coupon_id"));
        await counters.InitializeAsync("coupon_usages", await LargestAsync(usages, "coupon_usage_id"));
    }

    private static async Task<long> LargestAsync<T>(IMongoCollection<T> collection, string idField)
    {
        var top = await collection.Find(Builders<T>.Filter.Empty)
            .Sort(Builders<T>.Sort.Descending(idField))
            .Project(Builders<T>.Projection.Include(idField))
            .Limit(1)
            .FirstOrDefaultAsync();
        return top is null ? 0 : top[idField].ToInt64();
    }
}
