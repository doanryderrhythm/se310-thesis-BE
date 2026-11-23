using AuthApi.Features.Customers;

namespace AuthApi.Features.Admin;

internal sealed record CustomerStatsResponse(int OrderCount, int DeliveredCount, double TotalSpent, DateTime? LastOrderAt);
internal sealed record CustomerRecentOrder(string OrderCode, DateTime PlacedAt, double TotalAmount, string Status);
internal sealed record CustomerVoucherCounts(long Available, long Used, long Expired);

internal sealed record AdminCustomerDetail(
    long UserId, string FullName, string Phone, string? Email, string Status, string? StatusReason,
    DateTime? StatusChangedAt, DateTime CreatedAt, DateTime UpdatedAt,
    DeliveryAddressResponse[] Addresses, CustomerStatsResponse Stats, CustomerRecentOrder[] RecentOrders,
    CustomerVoucherCounts Vouchers, long ReviewCount);

internal sealed record LockCustomerRequest(string? Reason);
