using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace AuthApi.Features.Promotions;

internal static class CampaignStatuses
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Paused = "paused";
    public const string Ended = "ended";

    /// <summary>Status shown to admins: "scheduled"/"ended" are derived from the time window.</summary>
    public static string Effective(CampaignDocument campaign, DateTime now) =>
        campaign.Status == Active
            ? now < campaign.StartsAt ? "scheduled" : now >= campaign.EndsAt ? Ended : Active
            : campaign.Status;

    public static bool IsRunning(CampaignDocument campaign, DateTime now) =>
        campaign.Status == Active && now >= campaign.StartsAt && now < campaign.EndsAt;
}

internal static class DiscountTypes
{
    public const string Percent = "percent";
    public const string Fixed = "fixed";
    public const string FreeShipping = "free_shipping";
}

internal static class Distributions
{
    public const string PublicCode = "public_code";
    public const string Issued = "issued";
}

internal static class Triggers
{
    public const string None = "none";
    public const string Signup = "signup";
    public const string Manual = "manual";

    /// <summary>Issued automatically once a customer's lifetime spend reaches the campaign threshold.</summary>
    public const string Spend = "spend";
}

[BsonIgnoreExtraElements]
internal sealed class CampaignDocument
{
    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("campaign_id")] public long CampaignId { get; set; }
    [BsonElement("name")] public string Name { get; set; } = string.Empty;
    [BsonElement("description")] public string? Description { get; set; }
    [BsonElement("trigger")] public string Trigger { get; set; } = Triggers.None;
    [BsonElement("trigger_min_spent")] public double? TriggerMinSpent { get; set; }
    [BsonElement("distribution")] public string Distribution { get; set; } = Distributions.PublicCode;
    [BsonElement("status")] public string Status { get; set; } = CampaignStatuses.Draft;
    [BsonElement("starts_at")] public DateTime StartsAt { get; set; }
    [BsonElement("ends_at")] public DateTime EndsAt { get; set; }
    [BsonElement("created_by")] public long? CreatedBy { get; set; }
    [BsonElement("created_at")] public DateTime CreatedAt { get; set; }
    [BsonElement("updated_at")] public DateTime UpdatedAt { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class CouponDocument
{
    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("coupon_id")] public long CouponId { get; set; }
    [BsonElement("campaign_id")] public long CampaignId { get; set; }
    [BsonElement("code")] public string Code { get; set; } = string.Empty;
    [BsonElement("discount_type")] public string DiscountType { get; set; } = DiscountTypes.Fixed;
    [BsonElement("discount_value")] public double DiscountValue { get; set; }
    [BsonElement("max_discount_amount")] public double? MaxDiscountAmount { get; set; }
    [BsonElement("minimum_order_amount")] public double MinimumOrderAmount { get; set; }
    [BsonElement("applies_to")] public string AppliesTo { get; set; } = "all";
    [BsonElement("category_ids")] public List<long> CategoryIds { get; set; } = [];
    [BsonElement("usage_limit")] public int? UsageLimit { get; set; }
    [BsonElement("usage_count")] public int UsageCount { get; set; }
    [BsonElement("per_user_limit")] public int PerUserLimit { get; set; } = 1;
    [BsonElement("valid_days_after_issue")] public int? ValidDaysAfterIssue { get; set; }
    [BsonElement("starts_at")] public DateTime StartsAt { get; set; }
    [BsonElement("expires_at")] public DateTime ExpiresAt { get; set; }
    [BsonElement("status")] public string Status { get; set; } = "active";
    [BsonElement("created_at")] public DateTime CreatedAt { get; set; }
    [BsonElement("updated_at")] public DateTime UpdatedAt { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class UserCouponDocument
{
    public const string Available = "available";
    public const string Used = "used";
    public const string Revoked = "revoked";

    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("user_coupon_id")] public long UserCouponId { get; set; }
    [BsonElement("coupon_id")] public long CouponId { get; set; }
    [BsonElement("campaign_id")] public long CampaignId { get; set; }
    [BsonElement("user_id")] public long UserId { get; set; }
    [BsonElement("status")] public string Status { get; set; } = Available;
    [BsonElement("issued_at")] public DateTime IssuedAt { get; set; }
    [BsonElement("expires_at")] public DateTime ExpiresAt { get; set; }
    [BsonElement("used_order_id")] public long? UsedOrderId { get; set; }
    [BsonElement("used_at")] public DateTime? UsedAt { get; set; }

    /// <summary>Status shown to customers: an unused voucher past its expiry is "expired".</summary>
    public string EffectiveStatus(DateTime now) =>
        Status == Available && ExpiresAt <= now ? "expired" : Status;
}

[BsonIgnoreExtraElements]
internal sealed class CouponUsageDocument
{
    public const string Applied = "applied";
    public const string Released = "released";

    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("coupon_usage_id")] public long CouponUsageId { get; set; }
    [BsonElement("coupon_id")] public long CouponId { get; set; }
    [BsonElement("user_coupon_id")] public long? UserCouponId { get; set; }
    [BsonElement("order_id")] public long OrderId { get; set; }
    [BsonElement("user_id")] public long UserId { get; set; }
    [BsonElement("discount_amount")] public double DiscountAmount { get; set; }
    [BsonElement("used_at")] public DateTime UsedAt { get; set; }
    [BsonElement("status")] public string Status { get; set; } = Applied;
    [BsonElement("released_at")] public DateTime? ReleasedAt { get; set; }
}
