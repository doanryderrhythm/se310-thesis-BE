using AuthApi.Data;
using AuthApi.Features.Products;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AuthApi.Features.Promotions;

/// <summary>Business rules and constants shared with the future checkout/orders API.</summary>
internal static class CheckoutPricing
{
    public const double ShippingFee = 30_000;
    public const double FreeShippingThreshold = 500_000;
    public const int MaxCartLines = 50;
    public const int MaxOptions = 50;
}

internal sealed class CouponService(IMongoDatabase db, CounterService counters, ProductCatalogService catalog)
{
    private readonly IMongoCollection<CampaignDocument> campaigns = db.GetCollection<CampaignDocument>("campaigns");
    private readonly IMongoCollection<CouponDocument> coupons = db.GetCollection<CouponDocument>("coupons");
    private readonly IMongoCollection<UserCouponDocument> userCoupons = db.GetCollection<UserCouponDocument>("user_coupons");
    private readonly IMongoCollection<CouponUsageDocument> usages = db.GetCollection<CouponUsageDocument>("coupon_usages");

    // ---- Public codes and wallet --------------------------------------------

    public async Task<PublicCouponResponse[]> GetPublicCouponsAsync()
    {
        var now = DateTime.UtcNow;
        var running = await campaigns.Find(c =>
                c.Distribution == Distributions.PublicCode && c.Status == CampaignStatuses.Active
                && c.StartsAt <= now && c.EndsAt > now)
            .ToListAsync();
        var byId = running.ToDictionary(c => c.CampaignId);
        var list = await coupons.Find(Builders<CouponDocument>.Filter.In(c => c.CampaignId, byId.Keys)
                & Builders<CouponDocument>.Filter.Eq(c => c.Status, "active"))
            .SortBy(c => c.ExpiresAt).ToListAsync();
        return list
            .Where(c => c.UsageLimit is null || c.UsageCount < c.UsageLimit)
            .Select(c => new PublicCouponResponse(
                ToSummary(c, byId[c.CampaignId]), c.ExpiresAt,
                c.UsageLimit is { } limit ? limit - c.UsageCount : null))
            .ToArray();
    }

    public async Task<VoucherResponse[]> GetMyVouchersAsync(long userId, string status)
    {
        var now = DateTime.UtcNow;
        var b = Builders<UserCouponDocument>.Filter;
        var filter = b.Eq(u => u.UserId, userId) & status switch
        {
            "available" => b.Eq(u => u.Status, UserCouponDocument.Available) & b.Gt(u => u.ExpiresAt, now),
            "used" => b.Eq(u => u.Status, UserCouponDocument.Used),
            "expired" => b.Eq(u => u.Status, UserCouponDocument.Available) & b.Lte(u => u.ExpiresAt, now),
            _ => b.Ne(u => u.Status, UserCouponDocument.Revoked)
        };
        var vouchers = await userCoupons.Find(filter)
            .SortByDescending(u => u.IssuedAt).ThenByDescending(u => u.UserCouponId).ToListAsync();
        var couponsById = (await coupons.Find(
                Builders<CouponDocument>.Filter.In(c => c.CouponId, vouchers.Select(v => v.CouponId).Distinct()))
            .ToListAsync()).ToDictionary(c => c.CouponId);
        var campaignsById = (await campaigns.Find(
                Builders<CampaignDocument>.Filter.In(c => c.CampaignId, vouchers.Select(v => v.CampaignId).Distinct()))
            .ToListAsync()).ToDictionary(c => c.CampaignId);

        return vouchers
            .Where(v => couponsById.ContainsKey(v.CouponId) && campaignsById.ContainsKey(v.CampaignId))
            .Select(v => new VoucherResponse(
                v.UserCouponId, v.EffectiveStatus(now), v.IssuedAt, v.ExpiresAt, v.UsedAt,
                ToSummary(couponsById[v.CouponId], campaignsById[v.CampaignId])))
            .ToArray();
    }

    // ---- Validation / quote ----------------------------------------------------

    public sealed record QuoteOutcome(CouponQuoteResponse? Quote, string? CouponError, string? ItemsError);

    private sealed record PricedCart(double Subtotal, List<(long CategoryId, double Amount)> Lines);

    /// <summary>Prices the cart from the database (never from client prices).</summary>
    private async Task<(PricedCart? Cart, string? ItemsError)> PriceCartAsync(IReadOnlyList<CouponItemRequest> items)
    {
        var requested = items
            .GroupBy(i => i.VariantId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity!.Value));
        var bundle = await catalog.LoadForVariantsAsync(requested.Keys.ToArray());
        double subtotal = 0;
        var lines = new List<(long CategoryId, double Amount)>();
        foreach (var (variantId, quantity) in requested)
        {
            if (!bundle.VariantsById.TryGetValue(variantId, out var variant) || variant.Status != "active"
                || bundle.Products.GetValueOrDefault(variant.ProductId) is not { Status: "active" } product)
            {
                return (null, $"Variant {variantId} is not available.");
            }

            var amount = (bundle.PriceFor(variant) ?? 0) * quantity;
            subtotal += amount;
            lines.Add((product.CategoryId, amount));
        }

        return (new PricedCart(subtotal, lines), null);
    }

    /// <summary>
    /// Prices the cart and applies one coupon, given either as a code or as a wallet voucher id.
    /// Coupon failures are reported as COUPON_* codes.
    /// </summary>
    public async Task<QuoteOutcome> QuoteAsync(
        long userId, string? code, long? userCouponId, IReadOnlyList<CouponItemRequest> items)
    {
        var (cart, itemsError) = await PriceCartAsync(items);
        return cart is null
            ? new QuoteOutcome(null, null, itemsError)
            : await QuoteCartAsync(userId, code, userCouponId, cart);
    }

    /// <summary>
    /// Everything the customer can pick at checkout: wallet vouchers and running public codes,
    /// each evaluated against this cart. No "claim" step exists; eligible options come first,
    /// best saving first.
    /// </summary>
    public async Task<(CouponOptionResponse[]? Options, string? ItemsError)> GetApplicableAsync(
        long userId, IReadOnlyList<CouponItemRequest> items)
    {
        var (cart, itemsError) = await PriceCartAsync(items);
        if (cart is null) return (null, itemsError);

        var candidates = new List<(string Source, long? VoucherId, CouponSummary Coupon, DateTime ExpiresAt)>();
        foreach (var voucher in await GetMyVouchersAsync(userId, "available"))
            candidates.Add(("wallet", voucher.UserCouponId, voucher.Coupon, voucher.ExpiresAt));
        foreach (var pub in await GetPublicCouponsAsync())
            candidates.Add(("public", null, pub.Coupon, pub.ExpiresAt));

        var options = new List<CouponOptionResponse>();
        foreach (var (source, voucherId, coupon, expiresAt) in candidates.Take(CheckoutPricing.MaxOptions))
        {
            var outcome = await QuoteCartAsync(
                userId, voucherId is null ? coupon.Code : null, voucherId, cart);
            options.Add(new CouponOptionResponse(
                source, voucherId, coupon, expiresAt, outcome.Quote is not null, outcome.CouponError,
                outcome.Quote?.DiscountAmount ?? 0, outcome.Quote?.ShippingDiscount ?? 0));
        }

        return (options
            .OrderByDescending(o => o.Eligible)
            .ThenByDescending(o => o.DiscountAmount + o.ShippingDiscount)
            .ThenBy(o => o.ExpiresAt)
            .ToArray(), null);
    }

    private async Task<QuoteOutcome> QuoteCartAsync(
        long userId, string? code, long? userCouponId, PricedCart cart)
    {
        var (subtotal, lines) = (cart.Subtotal, cart.Lines);
        var now = DateTime.UtcNow;
        var resolved = await ResolveAsync(userId, code, userCouponId, now);
        if (resolved.Error is not null) return new QuoteOutcome(null, resolved.Error, null);
        var (coupon, campaign, voucher) = (resolved.Coupon!, resolved.Campaign!, resolved.Voucher);

        var eligibleSubtotal = coupon.AppliesTo == "categories"
            ? lines.Where(l => coupon.CategoryIds.Contains(l.CategoryId)).Sum(l => l.Amount)
            : subtotal;
        if (eligibleSubtotal <= 0) return new QuoteOutcome(null, "COUPON_NOT_ELIGIBLE", null);
        if (eligibleSubtotal < coupon.MinimumOrderAmount) return new QuoteOutcome(null, "COUPON_MIN", null);

        var discount = coupon.DiscountType switch
        {
            DiscountTypes.Percent => Math.Min(
                Math.Round(eligibleSubtotal * coupon.DiscountValue / 100, MidpointRounding.AwayFromZero),
                coupon.MaxDiscountAmount ?? double.MaxValue),
            DiscountTypes.Fixed => Math.Min(coupon.DiscountValue, eligibleSubtotal),
            _ => 0
        };
        var shippingFee = subtotal - discount >= CheckoutPricing.FreeShippingThreshold
            ? 0 : CheckoutPricing.ShippingFee;
        var shippingDiscount = coupon.DiscountType == DiscountTypes.FreeShipping ? shippingFee : 0;

        return new QuoteOutcome(new CouponQuoteResponse(
            ToSummary(coupon, campaign), voucher?.UserCouponId, subtotal, eligibleSubtotal, discount,
            shippingFee, shippingDiscount, subtotal - discount + shippingFee - shippingDiscount), null, null);
    }

    private sealed record Resolution(
        CouponDocument? Coupon, CampaignDocument? Campaign, UserCouponDocument? Voucher, string? Error);

    private async Task<Resolution> ResolveAsync(long userId, string? code, long? userCouponId, DateTime now)
    {
        const string Invalid = "COUPON_INVALID";
        UserCouponDocument? voucher = null;
        CouponDocument? coupon;
        if (userCouponId is { } voucherId)
        {
            voucher = await userCoupons.Find(u => u.UserCouponId == voucherId && u.UserId == userId)
                .FirstOrDefaultAsync();
            if (voucher is null) return new Resolution(null, null, null, Invalid);
            coupon = await coupons.Find(c => c.CouponId == voucher.CouponId).FirstOrDefaultAsync();
        }
        else
        {
            var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
            coupon = await coupons.Find(c => c.Code == normalized).FirstOrDefaultAsync();
        }

        if (coupon is null || coupon.Status != "active") return new Resolution(null, null, null, Invalid);
        var campaign = await campaigns.Find(c => c.CampaignId == coupon.CampaignId).FirstOrDefaultAsync();
        if (campaign is null) return new Resolution(null, null, null, Invalid);

        string? error = null;
        if (campaign.Distribution == Distributions.Issued)
        {
            // A typed code only works if that voucher is in the customer's wallet.
            voucher ??= await userCoupons.Find(u => u.CouponId == coupon.CouponId && u.UserId == userId)
                .FirstOrDefaultAsync();
            if (voucher is null || voucher.Status == UserCouponDocument.Revoked) error = Invalid;
            else if (voucher.Status == UserCouponDocument.Used) error = "COUPON_USED";
            else if (voucher.ExpiresAt <= now) error = "COUPON_EXPIRED";
            // Ending a campaign keeps already-issued vouchers usable; pausing blocks them.
            else if (campaign.Status is not (CampaignStatuses.Active or CampaignStatuses.Ended)) error = Invalid;
        }
        else
        {
            if (campaign.Status != CampaignStatuses.Active) error = Invalid;
            else if (now < campaign.StartsAt) error = "COUPON_NOT_STARTED";
            else if (now >= campaign.EndsAt) error = "COUPON_EXPIRED";
        }

        if (error is null && coupon.UsageLimit is { } limit && coupon.UsageCount >= limit) error = "COUPON_LIMIT";
        if (error is null && campaign.Distribution == Distributions.PublicCode)
        {
            var timesUsed = await usages.CountDocumentsAsync(u =>
                u.CouponId == coupon.CouponId && u.UserId == userId && u.Status == CouponUsageDocument.Applied);
            if (timesUsed >= coupon.PerUserLimit) error = "COUPON_USED";
        }

        return error is null
            ? new Resolution(coupon, campaign, voucher, null)
            : new Resolution(null, null, null, error);
    }

    // ---- Redeem / release (called by the orders API when it exists) --------------

    /// <summary>
    /// Consumes one use of the coupon for an order. The usage counter is bumped with a conditional
    /// update so concurrent orders cannot exceed the limit. Returns false if the coupon is no
    /// longer usable (limit reached or voucher already spent).
    /// </summary>
    public async Task<bool> RedeemAsync(
        long userId, long couponId, long? userCouponId, long orderId, double discountAmount)
    {
        var b = Builders<CouponDocument>.Filter;
        var bumped = await coupons.FindOneAndUpdateAsync(
            b.Eq(c => c.CouponId, couponId) & b.Eq(c => c.Status, "active")
                & (b.Eq(c => c.UsageLimit, null)
                    | new BsonDocumentFilterDefinition<CouponDocument>(new BsonDocument("$expr",
                        new BsonDocument("$lt", new BsonArray { "$usage_count", "$usage_limit" })))),
            Builders<CouponDocument>.Update.Inc(c => c.UsageCount, 1));
        if (bumped is null) return false;

        var now = DateTime.UtcNow;
        if (userCouponId is { } voucherId)
        {
            var spent = await userCoupons.UpdateOneAsync(
                u => u.UserCouponId == voucherId && u.UserId == userId
                    && u.Status == UserCouponDocument.Available && u.ExpiresAt > now,
                Builders<UserCouponDocument>.Update
                    .Set(u => u.Status, UserCouponDocument.Used)
                    .Set(u => u.UsedOrderId, orderId)
                    .Set(u => u.UsedAt, now));
            if (spent.ModifiedCount == 0)
            {
                await coupons.UpdateOneAsync(c => c.CouponId == couponId,
                    Builders<CouponDocument>.Update.Inc(c => c.UsageCount, -1));
                return false;
            }
        }

        await usages.InsertOneAsync(new CouponUsageDocument
        {
            CouponUsageId = await counters.NextAsync("coupon_usages"),
            CouponId = couponId,
            UserCouponId = userCouponId,
            OrderId = orderId,
            UserId = userId,
            DiscountAmount = discountAmount,
            UsedAt = now,
            Status = CouponUsageDocument.Applied
        });
        return true;
    }

    /// <summary>Gives the use back when an order is cancelled before delivery.</summary>
    public async Task ReleaseAsync(long orderId)
    {
        var applied = await usages.Find(u => u.OrderId == orderId && u.Status == CouponUsageDocument.Applied)
            .ToListAsync();
        foreach (var usage in applied)
        {
            var flipped = await usages.UpdateOneAsync(
                u => u.CouponUsageId == usage.CouponUsageId && u.Status == CouponUsageDocument.Applied,
                Builders<CouponUsageDocument>.Update
                    .Set(u => u.Status, CouponUsageDocument.Released)
                    .Set(u => u.ReleasedAt, DateTime.UtcNow));
            if (flipped.ModifiedCount == 0) continue;

            await coupons.UpdateOneAsync(c => c.CouponId == usage.CouponId && c.UsageCount > 0,
                Builders<CouponDocument>.Update.Inc(c => c.UsageCount, -1));
            if (usage.UserCouponId is { } voucherId)
            {
                await userCoupons.UpdateOneAsync(
                    u => u.UserCouponId == voucherId && u.Status == UserCouponDocument.Used,
                    Builders<UserCouponDocument>.Update
                        .Set(u => u.Status, UserCouponDocument.Available)
                        .Unset(u => u.UsedOrderId)
                        .Unset(u => u.UsedAt));
            }
        }
    }

    private static CouponSummary ToSummary(CouponDocument c, CampaignDocument campaign) =>
        new(c.CouponId, c.Code, campaign.Name, campaign.Description, c.DiscountType, c.DiscountValue,
            c.MaxDiscountAmount, c.MinimumOrderAmount, c.AppliesTo, [.. c.CategoryIds],
            VoucherText.Summary(c));
}
