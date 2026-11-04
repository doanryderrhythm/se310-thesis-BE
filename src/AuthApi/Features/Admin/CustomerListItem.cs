
namespace AuthApi.Features.Admin;
internal sealed record CustomerListItem(
    long UserId, string FullName, string Phone, string Status, DateTime CreatedAt);
