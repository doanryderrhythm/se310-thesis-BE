
namespace AuthApi.Features.Admin;
internal sealed record AdminResponse(
    long UserId, string FullName, string Phone, string Role, string Status, DateTime CreatedAt);
