using System.Text.RegularExpressions;

namespace AuthApi.Features.Promotions;

/// <summary>Every field of a campaign + its coupon, as typed by an admin, before validation.</summary>
internal sealed class CampaignDraft
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Trigger { get; set; }
    public double? TriggerMinSpent { get; set; }
    public string? Distribution { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public string? Code { get; set; }
    public string? DiscountType { get; set; }
    public double? DiscountValue { get; set; }
    public double? MaxDiscountAmount { get; set; }
    public double? MinimumOrderAmount { get; set; }
    public string? AppliesTo { get; set; }
    public List<long>? CategoryIds { get; set; }
    public int? UsageLimit { get; set; }
    public int? PerUserLimit { get; set; }
    public int? ValidDaysAfterIssue { get; set; }

    public static CampaignDraft FromRequest(CreateCampaignRequest request)
    {
        var draft = new CampaignDraft
        {
            Name = request.Name,
            Description = request.Description,
            Trigger = request.Trigger,
            TriggerMinSpent = request.TriggerMinSpent,
            Distribution = request.Distribution,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt
        };
        draft.ApplyCoupon(request.Coupon);
        return draft;
    }

    public static CampaignDraft FromExisting(CampaignDocument campaign, CouponDocument coupon) => new()
    {
        Name = campaign.Name,
        Description = campaign.Description,
        Trigger = campaign.Trigger,
        TriggerMinSpent = campaign.TriggerMinSpent,
        Distribution = campaign.Distribution,
        StartsAt = campaign.StartsAt,
        EndsAt = campaign.EndsAt,
        Code = coupon.Code,
        DiscountType = coupon.DiscountType,
        DiscountValue = coupon.DiscountValue,
        MaxDiscountAmount = coupon.MaxDiscountAmount,
        MinimumOrderAmount = coupon.MinimumOrderAmount,
        AppliesTo = coupon.AppliesTo,
        CategoryIds = [.. coupon.CategoryIds],
        UsageLimit = coupon.UsageLimit,
        PerUserLimit = coupon.PerUserLimit,
        ValidDaysAfterIssue = coupon.ValidDaysAfterIssue
    };

    /// <summary>Overlays the non-null coupon fields. 0 means "none" for max discount and usage limit.</summary>
    public void ApplyCoupon(CouponInput? input)
    {
        if (input is null) return;
        if (input.Code is not null) Code = input.Code;
        if (input.DiscountType is not null) DiscountType = input.DiscountType;
        if (input.DiscountValue is not null) DiscountValue = input.DiscountValue;
        if (input.MaxDiscountAmount is not null) MaxDiscountAmount = input.MaxDiscountAmount;
        if (input.MinimumOrderAmount is not null) MinimumOrderAmount = input.MinimumOrderAmount;
        if (input.AppliesTo is not null) AppliesTo = input.AppliesTo;
        if (input.CategoryIds is not null) CategoryIds = [.. input.CategoryIds];
        if (input.UsageLimit is not null) UsageLimit = input.UsageLimit;
        if (input.PerUserLimit is not null) PerUserLimit = input.PerUserLimit;
        if (input.ValidDaysAfterIssue is not null) ValidDaysAfterIssue = input.ValidDaysAfterIssue;
    }
}

internal static partial class CampaignRules
{
    public const double MaxMoney = 100_000_000;
    public const double MaxSpendThreshold = 10_000_000_000;

    [GeneratedRegex(@"\A[A-Z0-9_-]{4,30}\z", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    public static DateTime ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

    /// <summary>
    /// Normalizes <paramref name="draft"/> in place (trimmed, upper-cased code, defaults filled in)
    /// and returns validation errors keyed by the camelCase request field. After an empty result
    /// every field the services read is non-null.
    /// </summary>
    public static Dictionary<string, string[]> Validate(CampaignDraft draft)
    {
        var errors = new Dictionary<string, string[]>();

        draft.Name = draft.Name?.Trim();
        if (string.IsNullOrEmpty(draft.Name) || draft.Name.Length > 100)
            errors["name"] = ["Name is required and must be at most 100 characters."];

        draft.Description = string.IsNullOrWhiteSpace(draft.Description) ? null : draft.Description.Trim();
        if (draft.Description is { Length: > 500 })
            errors["description"] = ["Description must be at most 500 characters."];

        if (draft.Distribution is not (Distributions.PublicCode or Distributions.Issued))
            errors["distribution"] = ["Distribution must be 'public_code' or 'issued'."];

        draft.Trigger ??= draft.Distribution == Distributions.Issued ? Triggers.Manual : Triggers.None;
        if (draft.Trigger is not (Triggers.None or Triggers.Signup or Triggers.Manual or Triggers.Spend))
            errors["trigger"] = ["Trigger must be 'none', 'signup', 'manual' or 'spend'."];
        else if (!errors.ContainsKey("distribution"))
        {
            var ok = draft.Distribution == Distributions.PublicCode
                ? draft.Trigger == Triggers.None
                : draft.Trigger != Triggers.None;
            if (!ok)
                errors["trigger"] = ["Public codes use trigger 'none'; issued vouchers use 'signup', 'manual' or 'spend'."];
        }

        if (draft.Trigger == Triggers.Spend)
        {
            if (draft.TriggerMinSpent is not > 0 or > MaxSpendThreshold)
                errors["triggerMinSpent"] = ["Spend threshold must be greater than 0 and at most 10,000,000,000."];
        }
        else draft.TriggerMinSpent = null;

        if (draft.StartsAt is null) errors["startsAt"] = ["Start date is required."];
        else draft.StartsAt = ToUtc(draft.StartsAt.Value);
        if (draft.EndsAt is null) errors["endsAt"] = ["End date is required."];
        else draft.EndsAt = ToUtc(draft.EndsAt.Value);
        if (draft.StartsAt is not null && draft.EndsAt is not null && draft.EndsAt <= draft.StartsAt)
            errors["endsAt"] = ["End date must be after the start date."];

        draft.Code = draft.Code?.Trim().ToUpperInvariant();
        if (draft.Code is null || !CodePattern().IsMatch(draft.Code))
            errors["code"] = ["Code must be 4-30 characters: letters, digits, '-' or '_'."];

        switch (draft.DiscountType)
        {
            case DiscountTypes.Percent:
                if (draft.DiscountValue is not > 0 or > 100)
                    errors["discountValue"] = ["Percent discount must be greater than 0 and at most 100."];
                break;
            case DiscountTypes.Fixed:
                if (draft.DiscountValue is not > 0 or > MaxMoney)
                    errors["discountValue"] = [$"Fixed discount must be greater than 0 and at most {MaxMoney:0}."];
                break;
            case DiscountTypes.FreeShipping:
                draft.DiscountValue = 0;
                break;
            default:
                errors["discountType"] = ["Discount type must be 'percent', 'fixed' or 'free_shipping'."];
                break;
        }

        if (draft.DiscountType == DiscountTypes.Percent)
        {
            if (draft.MaxDiscountAmount is < 0 or > MaxMoney)
                errors["maxDiscountAmount"] = ["Max discount must be between 0 and 100,000,000."];
            else if (draft.MaxDiscountAmount == 0) draft.MaxDiscountAmount = null;
        }
        else draft.MaxDiscountAmount = null;

        draft.MinimumOrderAmount ??= 0;
        if (draft.MinimumOrderAmount is < 0 or > MaxMoney)
            errors["minimumOrderAmount"] = ["Minimum order amount must be between 0 and 100,000,000."];

        draft.AppliesTo ??= "all";
        if (draft.AppliesTo is not ("all" or "categories"))
            errors["appliesTo"] = ["Applies-to must be 'all' or 'categories'."];
        draft.CategoryIds = draft.CategoryIds?.Distinct().ToList() ?? [];
        if (draft.AppliesTo == "categories" && draft.CategoryIds.Count is 0 or > 50)
            errors["categoryIds"] = ["Provide between 1 and 50 category ids when applying to categories."];
        if (draft.AppliesTo == "all") draft.CategoryIds = [];

        if (draft.UsageLimit == 0) draft.UsageLimit = null;
        if (draft.UsageLimit is < 0 or > 1_000_000)
            errors["usageLimit"] = ["Usage limit must be between 1 and 1,000,000 (0 or empty means unlimited)."];

        if (draft.Distribution == Distributions.Issued)
        {
            draft.PerUserLimit = 1;
            draft.ValidDaysAfterIssue ??= 30;
            if (draft.ValidDaysAfterIssue is < 1 or > 365)
                errors["validDaysAfterIssue"] = ["Valid days after issue must be between 1 and 365."];
        }
        else
        {
            draft.ValidDaysAfterIssue = null;
            draft.PerUserLimit ??= 1;
            if (draft.PerUserLimit is < 1 or > 100)
                errors["perUserLimit"] = ["Per-user limit must be between 1 and 100."];
        }

        return errors;
    }
}
