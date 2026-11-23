
namespace AuthApi.Features.Admin;
internal sealed record CustomerListItem(
    long UserId, string FullName, string Phone, string? Email, string Status, DateTime CreatedAt,
    int OrderCount = 0, double TotalSpent = 0);
