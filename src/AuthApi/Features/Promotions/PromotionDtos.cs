namespace AuthApi.Features.Promotions;

// ---- Admin requests -------------------------------------------------------

/// <summary>Coupon rules inside a campaign request. On PATCH, omitted fields stay unchanged.</summary>
internal sealed record CouponInput(
    string? Code, string? DiscountType, double? DiscountValue, double? MaxDiscountAmount,
    double? MinimumOrderAmount, string? AppliesTo, long[]? CategoryIds, int? UsageLimit,
    int? PerUserLimit, int? ValidDaysAfterIssue);

internal sealed record CreateCampaignRequest(
    string? Name, string? Description, string? Trigger, string? Distribution,
    DateTime? StartsAt, DateTime? EndsAt, bool? Draft, CouponInput? Coupon, double? TriggerMinSpent = null);

internal sealed record UpdateCampaignRequest(
    string? Name, string? Description, DateTime? StartsAt, DateTime? EndsAt, CouponInput? Coupon,
    double? TriggerMinSpent = null);

internal sealed record DuplicateCampaignRequest(
    string? Code, string? Name, DateTime? StartsAt, DateTime? EndsAt);

/// <summary>Segment is "all_customers" or "min_spent" (then MinSpent is the lifetime spend in VND).</summary>
internal sealed record IssueVouchersRequest(string? Segment, double? MinSpent);

// ---- Admin responses ------------------------------------------------------

internal sealed record CouponResponse(
    long CouponId, string Code, string DiscountType, double DiscountValue, double? MaxDiscountAmount,
    double MinimumOrderAmount, string AppliesTo, long[] CategoryIds, int? UsageLimit, int UsageCount,
    int PerUserLimit, int? ValidDaysAfterIssue, string Status);

internal sealed record CampaignStatsResponse(
    long Issued, long Available, long Used, long Expired, long Revoked,
    int UsageCount, int? UsageLimit, double TotalDiscount, double? RedemptionRate);

internal sealed record CampaignResponse(
    long CampaignId, string Name, string? Description, string Trigger, double? TriggerMinSpent,
    string Distribution, string Status, DateTime StartsAt, DateTime EndsAt, DateTime CreatedAt, DateTime UpdatedAt,
    CouponResponse Coupon, CampaignStatsResponse Stats);

internal sealed record CampaignPageResponse(CampaignResponse[] Items, int Page, int PageSize, long TotalCount);

internal sealed record IssueVouchersResponse(int Matched, int Issued, int Skipped);

internal sealed record UsageResponse(
    long CouponUsageId, long UserId, string? FullName, long OrderId, double DiscountAmount,
    DateTime UsedAt, string Status);

internal sealed record UsagePageResponse(UsageResponse[] Items, int Page, int PageSize, long TotalCount);

internal sealed record RecipientResponse(
    long UserCouponId, long UserId, string? FullName, string? Phone, string Status,
    DateTime IssuedAt, DateTime ExpiresAt, DateTime? UsedAt);

internal sealed record RecipientPageResponse(RecipientResponse[] Items, int Page, int PageSize, long TotalCount);

// ---- Customer / public ----------------------------------------------------

/// <summary>What a customer needs to show a coupon: rule fields plus a ready-made Vietnamese summary.</summary>
internal sealed record CouponSummary(
    long CouponId, string Code, string Name, string? Description, string DiscountType,
    double DiscountValue, double? MaxDiscountAmount, double MinimumOrderAmount,
    string AppliesTo, long[] CategoryIds, string Summary);

internal sealed record PublicCouponResponse(CouponSummary Coupon, DateTime ExpiresAt, int? RemainingUses);

internal sealed record VoucherResponse(
    long UserCouponId, string Status, DateTime IssuedAt, DateTime ExpiresAt, DateTime? UsedAt,
    CouponSummary Coupon);

internal sealed record CouponItemRequest(long? VariantId, int? Quantity);

internal sealed record ValidateCouponRequest(string? Code, long? UserCouponId, CouponItemRequest[]? Items);

internal sealed record ApplicableCouponsRequest(CouponItemRequest[]? Items);

/// <summary>
/// One selectable coupon at checkout. Source "wallet" is applied with its UserCouponId,
/// source "public" is applied with its code. Reason is a COUPON_* code when not eligible.
/// </summary>
internal sealed record CouponOptionResponse(
    string Source, long? UserCouponId, CouponSummary Coupon, DateTime ExpiresAt,
    bool Eligible, string? Reason, double DiscountAmount, double ShippingDiscount);

internal sealed record CouponQuoteResponse(
    CouponSummary Coupon, long? UserCouponId, double Subtotal, double EligibleSubtotal,
    double DiscountAmount, double ShippingFee, double ShippingDiscount, double Total);
