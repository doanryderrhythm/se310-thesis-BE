
namespace AuthApi.Features.Customers;
internal sealed record CustomerProfileResponse(
    long UserId, string FullName, string Phone, string Role, DateTime CreatedAt, DateTime UpdatedAt);
