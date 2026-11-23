using System.Text.RegularExpressions;
using AuthApi.Data;
using AuthApi.Security;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AuthApi.Features.Promotions;

internal sealed class CampaignService(IMongoDatabase db, CounterService counters, VoucherIssuer issuer)
{
    private readonly IMongoCollection<CampaignDocument> campaigns = db.GetCollection<CampaignDocument>("campaigns");
    private readonly IMongoCollection<CouponDocument> coupons = db.GetCollection<CouponDocument>("coupons");
    private readonly IMongoCollection<UserCouponDocument> userCoupons = db.GetCollection<UserCouponDocument>("user_coupons");
    private readonly IMongoCollection<CouponUsageDocument> usages = db.GetCollection<CouponUsageDocument>("coupon_usages");
    private readonly IMongoCollection<User> users = db.GetCollection<User>("users");
    private readonly OrderSpendService spend = new(db);
    private readonly IMongoCollection<CategoryDocument> categories = db.GetCollection<CategoryDocument>("categories");

    private static readonly string[] StatusFilters = ["draft", "scheduled", "active", "paused", "ended"];

    // ---- Create / update -------------------------------------------------

    public async Task<(CampaignResponse? Campaign, IResult? Error)> CreateAsync(
        long adminId, CreateCampaignRequest request)
    {
        var draft = CampaignDraft.FromRequest(request);
        var errors = CampaignRules.Validate(draft);
        await CheckCategoriesAsync(draft, errors);
        if (errors.Count > 0) return (null, Results.ValidationProblem(errors));
        if (request.Draft != true
            && await CheckSingleSignupAsync(draft.Trigger!, draft.StartsAt!.Value, draft.EndsAt!.Value, null) is { } clash)
        {
            return (null, clash);
        }

        var now = DateTime.UtcNow;
        var campaign = new CampaignDocument
        {
            CampaignId = await counters.NextAsync("campaigns"),
            Name = draft.Name!,
            Description = draft.Description,
            Trigger = draft.Trigger!,
            TriggerMinSpent = draft.TriggerMinSpent,
            Distribution = draft.Distribution!,
            Status = request.Draft == true ? CampaignStatuses.Draft : CampaignStatuses.Active,
            StartsAt = draft.StartsAt!.Value,
            EndsAt = draft.EndsAt!.Value,
            CreatedBy = adminId,
            CreatedAt = now,
            UpdatedAt = now
        };
        var coupon = new CouponDocument
        {
            CouponId = await counters.NextAsync("coupons"),
            CampaignId = campaign.CampaignId,
            Code = draft.Code!,
            DiscountType = draft.DiscountType!,
            DiscountValue = draft.DiscountValue!.Value,
            MaxDiscountAmount = draft.MaxDiscountAmount,
            MinimumOrderAmount = draft.MinimumOrderAmount!.Value,
            AppliesTo = draft.AppliesTo!,
            CategoryIds = draft.CategoryIds!,
            UsageLimit = draft.UsageLimit,
            PerUserLimit = draft.PerUserLimit!.Value,
            ValidDaysAfterIssue = draft.ValidDaysAfterIssue,
            StartsAt = campaign.StartsAt,
            ExpiresAt = campaign.EndsAt,
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };

        // No transactions on standalone Mongo: insert the coupon first (its unique code guards
        // duplicates) and remove it again if the campaign insert fails.
        try
        {
            await coupons.InsertOneAsync(coupon);
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Code == 11000)
        {
            return (null, Results.Conflict(new { message = "A coupon with this code already exists." }));
        }

        try
        {
            await campaigns.InsertOneAsync(campaign);
        }
        catch
        {
            await coupons.DeleteOneAsync(c => c.CouponId == coupon.CouponId);
            throw;
        }

        return (await BuildAsync(campaign, coupon), null);
    }

    public async Task<(CampaignResponse? Campaign, IResult? Error)> UpdateAsync(
        long campaignId, UpdateCampaignRequest request)
    {
        var (campaign, coupon) = await FindAsync(campaignId);
        if (campaign is null || coupon is null) return (null, Results.NotFound());
        if (campaign.Status == CampaignStatuses.Ended)
            return (null, Results.Conflict(new { message = "An ended campaign cannot be edited." }));

        var draft = CampaignDraft.FromExisting(campaign, coupon);
        if (request.Name is not null) draft.Name = request.Name;
        if (request.Description is not null) draft.Description = request.Description;
        if (request.TriggerMinSpent is not null) draft.TriggerMinSpent = request.TriggerMinSpent;
        if (request.StartsAt is not null) draft.StartsAt = request.StartsAt;
        if (request.EndsAt is not null) draft.EndsAt = request.EndsAt;
        draft.ApplyCoupon(request.Coupon);

        var errors = CampaignRules.Validate(draft);
        await CheckCategoriesAsync(draft, errors);
        if (errors.Count > 0) return (null, Results.ValidationProblem(errors));

        if (campaign.Status == CampaignStatuses.Active
            && await CheckSingleSignupAsync(campaign.Trigger, draft.StartsAt!.Value, draft.EndsAt!.Value, campaignId) is { } clash)
        {
            return (null, clash);
        }

        var hasActivity = coupon.UsageCount > 0
            || await userCoupons.Find(u => u.CampaignId == campaignId).AnyAsync();
        if (hasActivity)
        {
            // Customers already hold or used this coupon, so its rules are frozen. Only the
            // name/description, a later end date and a higher usage limit may change.
            var locked = new List<string>();
            if (draft.Code != coupon.Code) locked.Add("code");
            if (draft.DiscountType != coupon.DiscountType) locked.Add("discountType");
            if (draft.DiscountValue != coupon.DiscountValue) locked.Add("discountValue");
            if (draft.MaxDiscountAmount != coupon.MaxDiscountAmount) locked.Add("maxDiscountAmount");
            if (draft.MinimumOrderAmount != coupon.MinimumOrderAmount) locked.Add("minimumOrderAmount");
            if (draft.AppliesTo != coupon.AppliesTo
                || !draft.CategoryIds!.OrderBy(i => i).SequenceEqual(coupon.CategoryIds.OrderBy(i => i)))
            {
                locked.Add("appliesTo");
            }
            if (draft.TriggerMinSpent != campaign.TriggerMinSpent) locked.Add("triggerMinSpent");
            if (draft.PerUserLimit != coupon.PerUserLimit) locked.Add("perUserLimit");
            if (draft.ValidDaysAfterIssue != coupon.ValidDaysAfterIssue) locked.Add("validDaysAfterIssue");
            if (draft.StartsAt != campaign.StartsAt) locked.Add("startsAt");
            if (draft.EndsAt < campaign.EndsAt) locked.Add("endsAt");
            if (draft.UsageLimit is not null && draft.UsageLimit < coupon.UsageCount) locked.Add("usageLimit");
            if (locked.Count > 0)
            {
                return (null, Results.Conflict(new
                {
                    message = "This coupon has already been issued or used; these fields cannot change.",
                    lockedFields = locked
                }));
            }
        }

        var now = DateTime.UtcNow;
        await campaigns.UpdateOneAsync(c => c.CampaignId == campaignId,
            Builders<CampaignDocument>.Update
                .Set(c => c.Name, draft.Name!)
                .Set(c => c.Description, draft.Description)
                .Set(c => c.TriggerMinSpent, draft.TriggerMinSpent)
                .Set(c => c.StartsAt, draft.StartsAt!.Value)
                .Set(c => c.EndsAt, draft.EndsAt!.Value)
                .Set(c => c.UpdatedAt, now));
        try
        {
            await coupons.UpdateOneAsync(c => c.CouponId == coupon.CouponId,
                Builders<CouponDocument>.Update
                    .Set(c => c.Code, draft.Code!)
                    .Set(c => c.DiscountType, draft.DiscountType!)
                    .Set(c => c.DiscountValue, draft.DiscountValue!.Value)
                    .Set(c => c.MaxDiscountAmount, draft.MaxDiscountAmount)
                    .Set(c => c.MinimumOrderAmount, draft.MinimumOrderAmount!.Value)
                    .Set(c => c.AppliesTo, draft.AppliesTo!)
                    .Set(c => c.CategoryIds, draft.CategoryIds!)
                    .Set(c => c.UsageLimit, draft.UsageLimit)
                    .Set(c => c.PerUserLimit, draft.PerUserLimit!.Value)
                    .Set(c => c.ValidDaysAfterIssue, draft.ValidDaysAfterIssue)
                    .Set(c => c.StartsAt, draft.StartsAt!.Value)
                    .Set(c => c.ExpiresAt, draft.EndsAt!.Value)
                    .Set(c => c.UpdatedAt, now));
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Code == 11000)
        {
            return (null, Results.Conflict(new { message = "A coupon with this code already exists." }));
        }

        var (updated, updatedCoupon) = await FindAsync(campaignId);
        return (await BuildAsync(updated!, updatedCoupon!), null);
    }

    public Task<(CampaignResponse? Campaign, IResult? Error)> DuplicateAsync(
        long adminId, long campaignId, DuplicateCampaignRequest request) =>
        DuplicateCoreAsync(adminId, campaignId, request);

    private async Task<(CampaignResponse? Campaign, IResult? Error)> DuplicateCoreAsync(
        long adminId, long campaignId, DuplicateCampaignRequest request)
    {
        var (campaign, coupon) = await FindAsync(campaignId);
        if (campaign is null || coupon is null) return (null, Results.NotFound());

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Code)) errors["code"] = ["A new code is required."];
        if (request.StartsAt is null) errors["startsAt"] = ["Start date is required."];
        if (request.EndsAt is null) errors["endsAt"] = ["End date is required."];
        if (errors.Count > 0) return (null, Results.ValidationProblem(errors));

        return await CreateAsync(adminId, new CreateCampaignRequest(
            request.Name ?? $"{campaign.Name} (sao chép)", campaign.Description,
            campaign.Trigger, campaign.Distribution, request.StartsAt, request.EndsAt, true,
            new CouponInput(request.Code, coupon.DiscountType, coupon.DiscountValue,
                coupon.MaxDiscountAmount, coupon.MinimumOrderAmount, coupon.AppliesTo,
                [.. coupon.CategoryIds], coupon.UsageLimit, coupon.PerUserLimit,
                coupon.ValidDaysAfterIssue), campaign.TriggerMinSpent));
    }

    // ---- Status transitions ----------------------------------------------

    public async Task<(CampaignResponse? Campaign, IResult? Error)> TransitionAsync(long campaignId, string action)
    {
        var (campaign, coupon) = await FindAsync(campaignId);
        if (campaign is null || coupon is null) return (null, Results.NotFound());

        var (allowedFrom, target) = action switch
        {
            "pause" => (new[] { CampaignStatuses.Active }, CampaignStatuses.Paused),
            "resume" => (new[] { CampaignStatuses.Paused, CampaignStatuses.Draft }, CampaignStatuses.Active),
            _ => (new[] { CampaignStatuses.Draft, CampaignStatuses.Active, CampaignStatuses.Paused },
                CampaignStatuses.Ended)
        };
        var now = DateTime.UtcNow;
        if (action == "resume" && campaign.EndsAt <= now)
        {
            return (null, Results.Conflict(new { message = "The end date has passed; extend it before resuming." }));
        }

        if (action == "resume"
            && await CheckSingleSignupAsync(campaign.Trigger, campaign.StartsAt, campaign.EndsAt, campaignId) is { } clash)
        {
            return (null, clash);
        }

        var updated = await campaigns.FindOneAndUpdateAsync(
            c => c.CampaignId == campaignId && allowedFrom.Contains(c.Status),
            Builders<CampaignDocument>.Update.Set(c => c.Status, target).Set(c => c.UpdatedAt, now),
            new FindOneAndUpdateOptions<CampaignDocument> { ReturnDocument = ReturnDocument.After });
        if (updated is null)
        {
            return (null, Results.Conflict(new
            {
                message = $"A campaign that is '{CampaignStatuses.Effective(campaign, now)}' cannot be {action}d."
            }));
        }

        return (await BuildAsync(updated, coupon), null);
    }

    // ---- Reads ------------------------------------------------------------

    public async Task<CampaignPageResponse?> ListAsync(
        string? status, string? trigger, string? q, int page, int pageSize)
    {
        var now = DateTime.UtcNow;
        var filter = Builders<CampaignDocument>.Filter.Empty;
        if (!string.IsNullOrEmpty(status))
        {
            filter &= status switch
            {
                "draft" => Builders<CampaignDocument>.Filter.Eq(c => c.Status, CampaignStatuses.Draft),
                "paused" => Builders<CampaignDocument>.Filter.Eq(c => c.Status, CampaignStatuses.Paused),
                "scheduled" => Builders<CampaignDocument>.Filter.Eq(c => c.Status, CampaignStatuses.Active)
                    & Builders<CampaignDocument>.Filter.Gt(c => c.StartsAt, now),
                "active" => Builders<CampaignDocument>.Filter.Eq(c => c.Status, CampaignStatuses.Active)
                    & Builders<CampaignDocument>.Filter.Lte(c => c.StartsAt, now)
                    & Builders<CampaignDocument>.Filter.Gt(c => c.EndsAt, now),
                "ended" => Builders<CampaignDocument>.Filter.Eq(c => c.Status, CampaignStatuses.Ended)
                    | (Builders<CampaignDocument>.Filter.Eq(c => c.Status, CampaignStatuses.Active)
                        & Builders<CampaignDocument>.Filter.Lte(c => c.EndsAt, now)),
                _ => throw new ArgumentException("status")
            };
        }
        if (!string.IsNullOrEmpty(trigger))
            filter &= Builders<CampaignDocument>.Filter.Eq(c => c.Trigger, trigger);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = new BsonRegularExpression(Regex.Escape(q.Trim()), "i");
            var codeMatches = await coupons.Find(Builders<CouponDocument>.Filter.Regex(c => c.Code, pattern))
                .Project(c => c.CampaignId).ToListAsync();
            filter &= Builders<CampaignDocument>.Filter.Regex(c => c.Name, pattern)
                | Builders<CampaignDocument>.Filter.In(c => c.CampaignId, codeMatches);
        }

        var total = await campaigns.CountDocumentsAsync(filter);
        var pageItems = await campaigns.Find(filter)
            .SortByDescending(c => c.CreatedAt).ThenByDescending(c => c.CampaignId)
            .Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync();
        var ids = pageItems.Select(c => c.CampaignId).ToArray();
        var pageCoupons = await coupons.Find(Builders<CouponDocument>.Filter.In(c => c.CampaignId, ids)).ToListAsync();
        var couponsByCampaign = pageCoupons.ToDictionary(c => c.CampaignId);
        var stats = await LoadStatsAsync(pageCoupons);

        return new CampaignPageResponse(
            pageItems.Where(c => couponsByCampaign.ContainsKey(c.CampaignId))
                .Select(c => ToResponse(c, couponsByCampaign[c.CampaignId], stats[c.CampaignId], now))
                .ToArray(),
            page, pageSize, total);
    }

    public static bool IsValidStatusFilter(string? status) =>
        string.IsNullOrEmpty(status) || StatusFilters.Contains(status);

    public async Task<CampaignResponse?> GetAsync(long campaignId)
    {
        var (campaign, coupon) = await FindAsync(campaignId);
        return campaign is null || coupon is null ? null : await BuildAsync(campaign, coupon);
    }

    public async Task<CampaignStatsResponse?> GetStatsAsync(long campaignId)
    {
        var (campaign, coupon) = await FindAsync(campaignId);
        return campaign is null || coupon is null ? null : (await LoadStatsAsync([coupon]))[campaignId];
    }

    public async Task<UsagePageResponse?> GetUsagesAsync(long campaignId, int page, int pageSize)
    {
        var (campaign, coupon) = await FindAsync(campaignId);
        if (campaign is null || coupon is null) return null;

        var filter = Builders<CouponUsageDocument>.Filter.Eq(u => u.CouponId, coupon.CouponId);
        var total = await usages.CountDocumentsAsync(filter);
        var items = await usages.Find(filter)
            .SortByDescending(u => u.UsedAt).ThenByDescending(u => u.CouponUsageId)
            .Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync();
        var names = await UserNamesAsync(items.Select(u => u.UserId));
        return new UsagePageResponse(
            items.Select(u => new UsageResponse(u.CouponUsageId, u.UserId, names.GetValueOrDefault(u.UserId),
                u.OrderId, u.DiscountAmount, u.UsedAt, u.Status)).ToArray(),
            page, pageSize, total);
    }

    public async Task<RecipientPageResponse?> GetRecipientsAsync(
        long campaignId, string? status, int page, int pageSize)
    {
        var (campaign, _) = await FindAsync(campaignId);
        if (campaign is null) return null;

        var now = DateTime.UtcNow;
        var b = Builders<UserCouponDocument>.Filter;
        var filter = b.Eq(u => u.CampaignId, campaignId);
        filter &= status switch
        {
            "available" => b.Eq(u => u.Status, UserCouponDocument.Available) & b.Gt(u => u.ExpiresAt, now),
            "expired" => b.Eq(u => u.Status, UserCouponDocument.Available) & b.Lte(u => u.ExpiresAt, now),
            "used" => b.Eq(u => u.Status, UserCouponDocument.Used),
            "revoked" => b.Eq(u => u.Status, UserCouponDocument.Revoked),
            _ => b.Empty
        };
        var total = await userCoupons.CountDocumentsAsync(filter);
        var items = await userCoupons.Find(filter)
            .SortByDescending(u => u.IssuedAt).ThenByDescending(u => u.UserCouponId)
            .Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync();
        var owners = await users.Find(Builders<User>.Filter.In(u => u.UserId, items.Select(u => u.UserId)))
            .ToListAsync();
        var ownersById = owners.ToDictionary(u => u.UserId);
        return new RecipientPageResponse(
            items.Select(u => new RecipientResponse(
                u.UserCouponId, u.UserId, ownersById.GetValueOrDefault(u.UserId)?.FullName,
                ownersById.GetValueOrDefault(u.UserId)?.Phone, u.EffectiveStatus(now),
                u.IssuedAt, u.ExpiresAt, u.UsedAt)).ToArray(),
            page, pageSize, total);
    }

    public static bool IsValidRecipientStatus(string? status) =>
        string.IsNullOrEmpty(status) || status is "available" or "used" or "expired" or "revoked";

    // ---- Manual issue -----------------------------------------------------

    public async Task<(IssueVouchersResponse? Result, IResult? Error)> IssueAsync(
        long campaignId, IssueVouchersRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        FilterDefinition<User>? segment = null;
        var b = Builders<User>.Filter;
        var customers = b.Eq(u => u.Role, UserRoles.Customer) & b.Eq(u => u.Status, "active");
        switch (request.Segment)
        {
            case "all_customers":
                segment = customers;
                break;
            case "min_spent":
                if (request.MinSpent is not > 0 or > CampaignRules.MaxSpendThreshold)
                    errors["minSpent"] = ["minSpent must be greater than 0 and at most 10,000,000,000."];
                else
                    segment = customers & b.In(u => u.UserId, await spend.UserIdsWithSpendAtLeastAsync(request.MinSpent.Value));
                break;
            default:
                errors["segment"] = ["Segment must be 'all_customers' or 'min_spent'."];
                break;
        }
        if (errors.Count > 0) return (null, Results.ValidationProblem(errors));

        var (campaign, coupon) = await FindAsync(campaignId);
        if (campaign is null || coupon is null) return (null, Results.NotFound());
        // Spend campaigns are issued automatically; hand issue is how an admin backfills customers
        // who had already crossed the threshold before the campaign started.
        if (campaign.Distribution != Distributions.Issued
            || campaign.Trigger is not (Triggers.Manual or Triggers.Spend))
        {
            return (null, Results.Conflict(new
            {
                message = "Only issued campaigns with trigger 'manual' or 'spend' can be issued by hand."
            }));
        }
        if (!CampaignStatuses.IsRunning(campaign, DateTime.UtcNow) || coupon.Status != "active")
        {
            return (null, Results.Conflict(new { message = "The campaign is not running." }));
        }

        return (await issuer.IssueAsync(campaign, coupon, segment!), null);
    }

    // ---- Helpers ------------------------------------------------------------

    /// <summary>
    /// New customers receive only one welcome voucher, so two signup campaigns may not be
    /// active over overlapping periods. Returns a 409 result on a clash.
    /// </summary>
    private async Task<IResult?> CheckSingleSignupAsync(
        string trigger, DateTime startsAt, DateTime endsAt, long? excludeCampaignId)
    {
        if (trigger != Triggers.Signup) return null;
        var exclude = excludeCampaignId ?? 0;
        var clash = await campaigns.Find(c =>
                c.Trigger == Triggers.Signup && c.Status == CampaignStatuses.Active
                && c.CampaignId != exclude && c.StartsAt < endsAt && c.EndsAt > startsAt)
            .FirstOrDefaultAsync();
        return clash is null
            ? null
            : Results.Conflict(new
            {
                message = "Another signup campaign is active in this period; new customers receive only one "
                    + "welcome voucher. End or pause it first.",
                conflictingCampaignId = clash.CampaignId
            });
    }

    private async Task<(CampaignDocument? Campaign, CouponDocument? Coupon)> FindAsync(long campaignId)
    {
        var campaign = await campaigns.Find(c => c.CampaignId == campaignId).FirstOrDefaultAsync();
        if (campaign is null) return (null, null);
        var coupon = await coupons.Find(c => c.CampaignId == campaignId).FirstOrDefaultAsync();
        return (campaign, coupon);
    }

    private async Task CheckCategoriesAsync(CampaignDraft draft, Dictionary<string, string[]> errors)
    {
        if (draft.AppliesTo != "categories" || draft.CategoryIds is not { Count: > 0 } ids
            || errors.ContainsKey("categoryIds")) return;
        var found = await categories.CountDocumentsAsync(Builders<CategoryDocument>.Filter.In(c => c.CategoryId, ids));
        if (found != ids.Count) errors["categoryIds"] = ["One or more category ids do not exist."];
    }

    private async Task<Dictionary<long, string>> UserNamesAsync(IEnumerable<long> userIds)
    {
        var owners = await users.Find(Builders<User>.Filter.In(u => u.UserId, userIds.Distinct())).ToListAsync();
        return owners.ToDictionary(u => u.UserId, u => u.FullName);
    }

    private async Task<CampaignResponse> BuildAsync(CampaignDocument campaign, CouponDocument coupon)
    {
        var stats = await LoadStatsAsync([coupon]);
        return ToResponse(campaign, coupon, stats[campaign.CampaignId], DateTime.UtcNow);
    }

    private static CampaignResponse ToResponse(
        CampaignDocument c, CouponDocument coupon, CampaignStatsResponse stats, DateTime now) =>
        new(c.CampaignId, c.Name, c.Description, c.Trigger, c.TriggerMinSpent, c.Distribution,
            CampaignStatuses.Effective(c, now), c.StartsAt, c.EndsAt, c.CreatedAt, c.UpdatedAt,
            new CouponResponse(coupon.CouponId, coupon.Code, coupon.DiscountType, coupon.DiscountValue,
                coupon.MaxDiscountAmount, coupon.MinimumOrderAmount, coupon.AppliesTo,
                [.. coupon.CategoryIds], coupon.UsageLimit, coupon.UsageCount, coupon.PerUserLimit,
                coupon.ValidDaysAfterIssue, coupon.Status),
            stats);

    /// <summary>Wallet counts per voucher status and total discount given, keyed by campaign id.</summary>
    private async Task<Dictionary<long, CampaignStatsResponse>> LoadStatsAsync(IReadOnlyCollection<CouponDocument> couponList)
    {
        var now = DateTime.UtcNow;
        var campaignIds = couponList.Select(c => c.CampaignId).ToArray();
        var couponIds = couponList.Select(c => c.CouponId).ToArray();

        var walletCounts = await userCoupons.Aggregate()
            .Match(Builders<UserCouponDocument>.Filter.In(u => u.CampaignId, campaignIds))
            .Group<BsonDocument>(new BsonDocument
            {
                { "_id", new BsonDocument
                    {
                        { "campaign", "$campaign_id" },
                        { "status", "$status" },
                        { "expired", new BsonDocument("$lte", new BsonArray { "$expires_at", new BsonDateTime(now) }) }
                    } },
                { "count", new BsonDocument("$sum", 1) }
            }).ToListAsync();
        var discounts = await usages.Aggregate()
            .Match(Builders<CouponUsageDocument>.Filter.In(u => u.CouponId, couponIds)
                & Builders<CouponUsageDocument>.Filter.Eq(u => u.Status, CouponUsageDocument.Applied))
            .Group<BsonDocument>(new BsonDocument
            {
                { "_id", "$coupon_id" },
                { "total", new BsonDocument("$sum", "$discount_amount") }
            }).ToListAsync();
        var discountByCoupon = discounts.ToDictionary(d => d["_id"].ToInt64(), d => d["total"].ToDouble());

        var result = new Dictionary<long, CampaignStatsResponse>();
        foreach (var coupon in couponList)
        {
            long available = 0, used = 0, expired = 0, revoked = 0;
            foreach (var row in walletCounts.Where(r => r["_id"]["campaign"].ToInt64() == coupon.CampaignId))
            {
                var count = row["count"].ToInt64();
                var isExpired = row["_id"]["expired"].AsBoolean;
                switch (row["_id"]["status"].AsString)
                {
                    case UserCouponDocument.Available when isExpired: expired += count; break;
                    case UserCouponDocument.Available: available += count; break;
                    case UserCouponDocument.Used: used += count; break;
                    case UserCouponDocument.Revoked: revoked += count; break;
                }
            }

            var issued = available + used + expired + revoked;
            result[coupon.CampaignId] = new CampaignStatsResponse(
                issued, available, used, expired, revoked, coupon.UsageCount, coupon.UsageLimit,
                discountByCoupon.GetValueOrDefault(coupon.CouponId),
                issued > 0 ? Math.Round((double)coupon.UsageCount / issued, 4) : null);
        }

        return result;
    }
}
