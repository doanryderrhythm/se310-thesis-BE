using System.Globalization;

namespace AuthApi.Features.Promotions;

/// <summary>Vietnamese, customer-facing wording for vouchers (summaries, notifications, emails).</summary>
internal static class VoucherText
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public static string Vnd(double amount) =>
        amount.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.') + "đ";

    public static string Date(DateTime utc) => (utc + VietnamOffset).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public static string Summary(CouponDocument coupon)
    {
        var text = coupon.DiscountType switch
        {
            DiscountTypes.Percent => $"Giảm {coupon.DiscountValue.ToString("0.##", CultureInfo.InvariantCulture)}%"
                + (coupon.MaxDiscountAmount > 0 ? $", tối đa {Vnd(coupon.MaxDiscountAmount.Value)}" : ""),
            DiscountTypes.Fixed => $"Giảm {Vnd(coupon.DiscountValue)}",
            _ => "Miễn phí vận chuyển"
        };
        if (coupon.MinimumOrderAmount > 0) text += $" cho đơn từ {Vnd(coupon.MinimumOrderAmount)}";
        if (coupon.AppliesTo == "categories") text += " (áp dụng cho một số danh mục)";
        return text;
    }

    public static (string Title, string Body) VoucherIssued(
        CampaignDocument campaign, CouponDocument coupon, DateTime expiresAt) =>
        ($"Bạn nhận được voucher {coupon.Code}",
         $"{campaign.Name}: {Summary(coupon)}. Hạn dùng đến {Date(expiresAt)}. Voucher đã có sẵn trong ví, chọn khi thanh toán.");
}
