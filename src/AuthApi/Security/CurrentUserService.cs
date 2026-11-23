using System.Security.Claims;
using MongoDB.Driver;

namespace AuthApi.Security;

internal sealed class CurrentUserService(IMongoCollection<User> users)
{
    public async Task<(User? User, IResult? Error)> ResolveAsync(
        HttpContext context, string requiredRole)
    {
        var userIdClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(userIdClaim, out var userId))
        {
            return (null, Results.Unauthorized());
        }

        var user = await users.Find(candidate => candidate.UserId == userId)
            .FirstOrDefaultAsync();
        if (user is null || user.Status != "active")
        {
            return (null, Results.Unauthorized());
        }

        // A password change ends every session that started before it (tokens carry their issue time).
        if (user.PasswordChangedAt is { } changedAt
            && long.TryParse(context.User.FindFirstValue("iat"), out var issuedAt)
            && issuedAt < new DateTimeOffset(changedAt, TimeSpan.Zero).ToUnixTimeSeconds())
        {
            return (null, Results.Unauthorized());
        }

        if (!context.User.IsInRole(requiredRole) || user.Role != requiredRole)
        {
            return (null, Results.Forbid());
        }

        return (user, null);
    }
}
