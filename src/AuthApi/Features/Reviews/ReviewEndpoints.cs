using AuthApi.Data;
using AuthApi.Security;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AuthApi.Features.Reviews;

[BsonIgnoreExtraElements]
internal sealed class ProductReviewDocument
{
    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("review_id")] public long ReviewId { get; set; }
    [BsonElement("product_id")] public long ProductId { get; set; }
    [BsonElement("user_id")] public long UserId { get; set; }
    [BsonElement("order_item_id")] public long? OrderItemId { get; set; }
    [BsonElement("rating")] public int Rating { get; set; }
    [BsonElement("comment")] public string? Comment { get; set; }
    [BsonElement("status")] public string Status { get; set; } = "published";
    [BsonElement("created_at")] public DateTime CreatedAt { get; set; }
}

internal sealed record CreateReviewRequest(int? Rating, string? Comment);
internal sealed record ReviewResponse(
    long ReviewId, long ProductId, string? ReviewerName, int Rating, string? Comment, DateTime CreatedAt);
internal sealed record MyReviewResponse(
    long ReviewId, long ProductId, string? ProductName, int Rating, string? Comment,
    string Status, DateTime CreatedAt);
internal sealed record ReviewPageResponse(
    ReviewResponse[] Items, int Page, int PageSize, long TotalCount, double AverageRating);

internal static class ReviewEndpoints
{
    public static async Task SetupAsync(IMongoDatabase db, CounterService counters)
    {
        var reviews = db.GetCollection<ProductReviewDocument>("product_reviews");
        await reviews.Indexes.CreateOneAsync(new CreateIndexModel<ProductReviewDocument>(
            Builders<ProductReviewDocument>.IndexKeys.Ascending(r => r.UserId).Ascending(r => r.ProductId),
            new CreateIndexOptions { Unique = true, Name = "review_user_product_unique" }));
        var largest = await reviews.Find(Builders<ProductReviewDocument>.Filter.Empty)
            .SortByDescending(r => r.ReviewId).Project(r => r.ReviewId).FirstOrDefaultAsync();
        await counters.InitializeAsync("product_reviews", largest);
    }

    public static void MapReviewEndpoints(
        this IEndpointRouteBuilder app, IMongoDatabase db, IMongoCollection<User> users,
        CounterService counters)
    {
        var reviews = db.GetCollection<ProductReviewDocument>("product_reviews");
        var products = db.GetCollection<ProductDocument>("products");
        var currentUsers = new CurrentUserService(users);

        async Task<bool> ActiveProductExists(long productId) =>
            await products.Find(p => p.ProductId == productId && p.Status == "active").AnyAsync();

        app.MapGet("/api/products/{productId:long}/reviews", async (long productId, int? page, int? pageSize) =>
        {
            var currentPage = page ?? 1;
            var currentPageSize = pageSize ?? 10;
            if (currentPage < 1 || currentPageSize < 1 || currentPageSize > 50 ||
                (long)(currentPage - 1) * currentPageSize > int.MaxValue)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["page"] = ["page must be at least 1 and pageSize must be between 1 and 50."]
                });
            }

            if (!await ActiveProductExists(productId))
                return Results.NotFound(new { message = "Product not found." });

            var filter = Builders<ProductReviewDocument>.Filter.Where(
                r => r.ProductId == productId && r.Status == "published");
            var ratings = await reviews.Find(filter).Project(r => r.Rating).ToListAsync();
            var pageItems = await reviews.Find(filter)
                .Sort(Builders<ProductReviewDocument>.Sort.Descending(r => r.CreatedAt).Descending(r => r.ReviewId))
                .Skip((currentPage - 1) * currentPageSize).Limit(currentPageSize).ToListAsync();
            var userIds = pageItems.Select(r => r.UserId).Distinct().ToArray();
            var names = (await users.Find(Builders<User>.Filter.In(u => u.UserId, userIds)).ToListAsync())
                .ToDictionary(u => u.UserId, u => u.FullName);

            return Results.Ok(new ReviewPageResponse(
                pageItems.Select(r => new ReviewResponse(r.ReviewId, r.ProductId,
                    names.GetValueOrDefault(r.UserId), r.Rating, r.Comment, r.CreatedAt)).ToArray(),
                currentPage, currentPageSize, ratings.Count,
                ratings.Count == 0 ? 0 : Math.Round(ratings.Average(), 2)));
        });

        app.MapPost("/api/products/{productId:long}/reviews",
            async (HttpContext context, long productId, CreateReviewRequest request) =>
        {
            var (user, error) = await currentUsers.ResolveAsync(context, UserRoles.Customer);
            if (error is not null) return error;

            var errors = new Dictionary<string, string[]>();
            if (request.Rating is null or < 1 or > 5)
                errors["rating"] = ["Rating must be between 1 and 5."];
            var comment = request.Comment?.Trim();
            if (comment is null || comment.Length < 10 || comment.Length > 500)
                errors["comment"] = ["Comment must be between 10 and 500 characters."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            if (!await ActiveProductExists(productId))
                return Results.NotFound(new { message = "Product not found." });

            var review = new ProductReviewDocument
            {
                ReviewId = await counters.NextAsync("product_reviews"),
                ProductId = productId,
                UserId = user!.UserId,
                Rating = request.Rating!.Value,
                Comment = comment,
                Status = "published",
                CreatedAt = DateTime.UtcNow
            };
            try
            {
                await reviews.InsertOneAsync(review);
            }
            catch (MongoWriteException exception) when (exception.WriteError?.Code == 11000)
            {
                return Results.Conflict(new { message = "You have already reviewed this product." });
            }

            return Results.Created($"/api/customers/me/reviews/{review.ReviewId}",
                new ReviewResponse(review.ReviewId, productId, user.FullName,
                    review.Rating, review.Comment, review.CreatedAt));
        });

        var mine = app.MapGroup("/api/customers/me/reviews").RequireAuthorization();
        mine.MapGet("", async (HttpContext context) =>
        {
            var (user, error) = await currentUsers.ResolveAsync(context, UserRoles.Customer);
            if (error is not null) return error;
            var own = await reviews.Find(r => r.UserId == user!.UserId)
                .SortByDescending(r => r.CreatedAt).ToListAsync();
            var productIds = own.Select(r => r.ProductId).Distinct().ToArray();
            var names = (await products.Find(Builders<ProductDocument>.Filter.In(p => p.ProductId, productIds))
                .ToListAsync()).ToDictionary(p => p.ProductId, p => p.Name);
            return Results.Ok(own.Select(r => new MyReviewResponse(r.ReviewId, r.ProductId,
                names.GetValueOrDefault(r.ProductId), r.Rating, r.Comment, r.Status, r.CreatedAt)).ToArray());
        });
        mine.MapDelete("/{reviewId:long}", async (HttpContext context, long reviewId) =>
        {
            var (user, error) = await currentUsers.ResolveAsync(context, UserRoles.Customer);
            if (error is not null) return error;
            var result = await reviews.DeleteOneAsync(r => r.ReviewId == reviewId && r.UserId == user!.UserId);
            return result.DeletedCount == 0
                ? Results.NotFound(new { message = "Review not found." })
                : Results.NoContent();
        });

        app.MapDelete("/api/admin/reviews/{reviewId:long}", async (HttpContext context, long reviewId) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(context, UserRoles.Admin);
            if (error is not null) return error;
            var result = await reviews.DeleteOneAsync(r => r.ReviewId == reviewId);
            return result.DeletedCount == 0
                ? Results.NotFound(new { message = "Review not found." })
                : Results.NoContent();
        }).RequireAuthorization();
    }
}
