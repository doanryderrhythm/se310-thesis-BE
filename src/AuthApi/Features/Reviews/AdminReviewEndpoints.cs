using System.Text.RegularExpressions;
using AuthApi.Common;
using AuthApi.Features.Products;
using AuthApi.Security;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AuthApi.Features.Reviews;

internal sealed record AdminReviewItem(
    long ReviewId, long ProductId, string? ProductName, long UserId, string? CustomerName, string? CustomerPhone,
    long? OrderItemId, int Rating, string? Comment, string Status, string? ModerationReason, DateTime CreatedAt);

internal sealed record ReviewStatusRequest(string? Status, string? Reason);

internal static class AdminReviewEndpoints
{
    public static void MapAdminReviewEndpoints(
        this IEndpointRouteBuilder app, IMongoDatabase db, IMongoCollection<User> users)
    {
        var reviews = db.GetCollection<ProductReviewDocument>("product_reviews");
        var products = db.GetCollection<ProductDocument>("products");
        var currentUsers = new CurrentUserService(users);

        async Task<AdminReviewItem[]> ToItemsAsync(List<ProductReviewDocument> list)
        {
            var names = (await products.Find(Builders<ProductDocument>.Filter.In(p => p.ProductId, list.Select(r => r.ProductId).Distinct())).ToListAsync())
                .ToDictionary(p => p.ProductId, p => p.Name);
            var owners = (await users.Find(Builders<User>.Filter.In(u => u.UserId, list.Select(r => r.UserId).Distinct())).ToListAsync())
                .ToDictionary(u => u.UserId);
            return list.Select(r => new AdminReviewItem(r.ReviewId, r.ProductId, names.GetValueOrDefault(r.ProductId),
                r.UserId, owners.GetValueOrDefault(r.UserId)?.FullName, owners.GetValueOrDefault(r.UserId)?.Phone,
                r.OrderItemId, r.Rating, r.Comment, r.Status, r.ModerationReason, r.CreatedAt)).ToArray();
        }

        app.MapGet("/api/admin/reviews", async (HttpContext c, string? q, string? status, int? rating, long? productId,
            int? page, int? pageSize) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(c, UserRoles.Admin);
            if (error is not null) return error;
            var errors = new Dictionary<string, string[]>();
            if (status is not (null or "" or "published" or "hidden")) errors["status"] = ["Status must be published or hidden."];
            if (rating is < 1 or > 5) errors["rating"] = ["Rating must be between 1 and 5."];
            var bad = Paging.TryGet(page, pageSize, out var p, out var ps);
            if (bad is not null) return bad;
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var b = Builders<ProductReviewDocument>.Filter;
            var filter = b.Empty;
            if (!string.IsNullOrWhiteSpace(status)) filter &= b.Eq(r => r.Status, status);
            if (rating is { } stars) filter &= b.Eq(r => r.Rating, stars);
            if (productId is { } pid) filter &= b.Eq(r => r.ProductId, pid);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var pattern = new BsonRegularExpression(Regex.Escape(q.Trim()), "i");
                var productIds = await products.Find(Builders<ProductDocument>.Filter.Regex(x => x.Name, pattern)).Project(x => x.ProductId).ToListAsync();
                var userIds = await users.Find(Builders<User>.Filter.Regex(u => u.FullName, pattern) | Builders<User>.Filter.Regex(u => u.Phone, pattern))
                    .Project(u => u.UserId).ToListAsync();
                filter &= b.In(r => r.ProductId, productIds) | b.In(r => r.UserId, userIds);
            }

            var total = await reviews.CountDocumentsAsync(filter);
            var list = await reviews.Find(filter).SortByDescending(r => r.CreatedAt).ThenByDescending(r => r.ReviewId)
                .Skip((p - 1) * ps).Limit(ps).ToListAsync();
            return Results.Ok(new PageResponse<AdminReviewItem>(await ToItemsAsync(list), p, ps, total));
        }).RequireAuthorization();

        // Moderation only: the rating and comment written by the customer are never edited.
        app.MapPatch("/api/admin/reviews/{reviewId:long}/status", async (HttpContext c, long reviewId, ReviewStatusRequest r) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(c, UserRoles.Admin);
            if (error is not null) return error;
            if (r.Status is not ("published" or "hidden"))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Status must be published or hidden."] });
            if (r.Reason is { Length: > 300 })
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["Reason must be at most 300 characters."] });

            var update = Builders<ProductReviewDocument>.Update.Set(x => x.Status, r.Status).Set(x => x.ModeratedAt, DateTime.UtcNow);
            update = string.IsNullOrWhiteSpace(r.Reason) ? update.Unset(x => x.ModerationReason) : update.Set(x => x.ModerationReason, r.Reason.Trim());
            var updated = await reviews.FindOneAndUpdateAsync(x => x.ReviewId == reviewId, update,
                new FindOneAndUpdateOptions<ProductReviewDocument> { ReturnDocument = ReturnDocument.After });
            return updated is null ? Results.NotFound() : Results.Ok((await ToItemsAsync([updated]))[0]);
        }).RequireAuthorization();
    }
}
