using System.Net;
using AuthApi.Data;
using AuthApi.Features.Email;
using AuthApi.Features.Notifications;
using MongoDB.Driver;

namespace AuthApi.Features.Promotions;

/// <summary>
/// Puts vouchers into customers' wallets and tells them about it (in-app notification + email).
/// A customer can hold one voucher per coupon; re-issuing is a no-op.
/// </summary>
internal sealed class VoucherIssuer(
    IMongoDatabase db, CounterService counters, NotificationService notifications,
    EmailQueue emails, ILogger logger)
{
    private readonly IMongoCollection<CampaignDocument> campaigns = db.GetCollection<CampaignDocument>("campaigns");
    private readonly IMongoCollection<CouponDocument> coupons = db.GetCollection<CouponDocument>("coupons");
    private readonly IMongoCollection<UserCouponDocument> userCoupons = db.GetCollection<UserCouponDocument>("user_coupons");
    private readonly IMongoCollection<User> users = db.GetCollection<User>("users");
    private readonly OrderSpendService spend = new(db);

    /// <summary>Called after registration: issues the running "signup" campaign to the new user.</summary>
    public async Task IssueSignupVouchersAsync(User user)
    {
        var now = DateTime.UtcNow;
        // A new customer gets exactly one welcome voucher: the newest running signup campaign.
        // (CampaignService already refuses overlapping signup campaigns; this is the safety net.)
        var campaign = await campaigns.Find(c =>
                c.Trigger == Triggers.Signup && c.Status == CampaignStatuses.Active
                && c.StartsAt <= now && c.EndsAt > now)
            .SortByDescending(c => c.CampaignId)
            .FirstOrDefaultAsync();
        if (campaign is null) return;
        var coupon = await coupons.Find(c => c.CampaignId == campaign.CampaignId).FirstOrDefaultAsync();
        if (coupon is null || coupon.Status != "active") return;
        await IssueOneAsync(campaign, coupon, user, now);
    }

    /// <summary>
    /// Call this whenever one of a customer's orders becomes "delivered" (the orders API's job).
    /// Issues every running "spend" campaign whose threshold the customer's lifetime spend now
    /// reaches. Safe to call repeatedly: a customer can only ever hold one voucher per coupon.
    /// </summary>
    public async Task IssueSpendVouchersAsync(long userId)
    {
        var now = DateTime.UtcNow;
        var running = await campaigns.Find(c =>
                c.Trigger == Triggers.Spend && c.Status == CampaignStatuses.Active
                && c.StartsAt <= now && c.EndsAt > now)
            .ToListAsync();
        if (running.Count == 0) return;

        var user = await users.Find(u => u.UserId == userId && u.Role == "customer" && u.Status == "active")
            .FirstOrDefaultAsync();
        if (user is null) return;

        var lifetimeSpend = await spend.LifetimeSpendAsync(userId);
        foreach (var campaign in running.Where(c => lifetimeSpend >= (c.TriggerMinSpent ?? double.MaxValue)))
        {
            var coupon = await coupons.Find(c => c.CampaignId == campaign.CampaignId).FirstOrDefaultAsync();
            if (coupon is null || coupon.Status != "active") continue;
            await IssueOneAsync(campaign, coupon, user, now);
        }
    }

    /// <summary>Issues a campaign's coupon to every customer matched by <paramref name="filter"/>.</summary>
    public async Task<IssueVouchersResponse> IssueAsync(
        CampaignDocument campaign, CouponDocument coupon, FilterDefinition<User> filter)
    {
        var now = DateTime.UtcNow;
        int matched = 0, issued = 0;
        using var cursor = await users.Find(filter).ToCursorAsync();
        while (await cursor.MoveNextAsync())
        {
            foreach (var user in cursor.Current)
            {
                matched++;
                if (await IssueOneAsync(campaign, coupon, user, now)) issued++;
            }
        }

        return new IssueVouchersResponse(matched, issued, matched - issued);
    }

    private async Task<bool> IssueOneAsync(
        CampaignDocument campaign, CouponDocument coupon, User user, DateTime now)
    {
        var expiresAt = coupon.ExpiresAt;
        if (coupon.ValidDaysAfterIssue is { } days && now.AddDays(days) < expiresAt)
            expiresAt = now.AddDays(days);

        var voucher = new UserCouponDocument
        {
            UserCouponId = await counters.NextAsync("user_coupons"),
            CouponId = coupon.CouponId,
            CampaignId = campaign.CampaignId,
            UserId = user.UserId,
            Status = UserCouponDocument.Available,
            IssuedAt = now,
            ExpiresAt = expiresAt
        };
        try
        {
            await userCoupons.InsertOneAsync(voucher);
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Code == 11000)
        {
            return false;
        }

        var (title, body) = VoucherText.VoucherIssued(campaign, coupon, expiresAt);
        try
        {
            await notifications.CreateAsync(user.UserId, "voucher_issued", title, body,
                new Dictionary<string, long>
                {
                    ["userCouponId"] = voucher.UserCouponId,
                    ["campaignId"] = campaign.CampaignId
                });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Creating voucher notification failed for user {UserId}.", user.UserId);
        }

        if (!string.IsNullOrEmpty(user.Email))
        {
            emails.Enqueue(new EmailMessage(user.Email, title, EmailBody(user, campaign, coupon, expiresAt)));
        }

        return true;
    }

    private static string EmailBody(User user, CampaignDocument campaign, CouponDocument coupon, DateTime expiresAt)
    {
        string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
        return $"""
            <div style="font-family:Arial,sans-serif;max-width:480px;margin:auto">
              <h2>Chào {E(user.FullName)},</h2>
              <p>Themis Fashion Studio tặng bạn một voucher trong chương trình <b>{E(campaign.Name)}</b>:</p>
              <p style="font-size:22px;letter-spacing:2px;border:2px dashed #333;padding:12px;text-align:center">
                <b>{E(coupon.Code)}</b></p>
              <p>{E(VoucherText.Summary(coupon))}.</p>
              <p>Hạn dùng đến <b>{VoucherText.Date(expiresAt)}</b>. Voucher đã có sẵn trong ví voucher của bạn — chọn nó ở bước thanh toán, không cần nhập hay nhận mã.</p>
            </div>
            """;
    }
}
