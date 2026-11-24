using AuthApi.Data;
using AuthApi.Features.Orders;
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
    [BsonElement("moderation_reason")] [BsonIgnoreIfNull] public string? ModerationReason { get; set; }
    [BsonElement("moderated_at")] [BsonIgnoreIfNull] public DateTime? ModeratedAt { get; set; }
}

internal sealed record CreateReviewRequest(int? Rating, string? Comment);
internal sealed record ReviewResponse(
    long ReviewId, long ProductId, string? ReviewerName, int Rating, string? Comment, DateTime CreatedAt);
internal sealed record MyReviewResponse(
    long ReviewId, long ProductId, string? ProductName, int Rating, string? Comment,
    string Status, DateTime CreatedAt, string? ImageUrl = null);
internal sealed record ReviewableItemResponse(
    string OrderCode, long OrderItemId, long ProductId, string? ProductName, string? ImageUrl, string? Sku,
    string? Size, string? Color, DateTime? DeliveredAt);
internal sealed record ReviewPageResponse(
    ReviewResponse[] Items, int Page, int PageSize, long TotalCount, double AverageRating,
    long ReviewCount, Dictionary<int, int> RatingCounts, long WithCommentCount);

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
        var orders = db.GetCollection<OrderDocument>("orders");
        var orderItems = db.GetCollection<OrderItemDocument>("order_items");
        var productImages = db.GetCollection<ProductImageDocument>("product_images");

        // The customer's delivered purchases of a product, newest first.
        async Task<List<(OrderDocument Order, OrderItemDocument Line)>> DeliveredPurchasesAsync(long userId, IEnumerable<long>? productIds = null)
        {
            var delivered = await orders.Find(o => o.UserId == userId && o.Status == OrderStatuses.Delivered)
                .SortByDescending(o => o.DeliveredAt).ToListAsync();
            if (delivered.Count == 0) return [];
            var filter = Builders<OrderItemDocument>.Filter.In(i => i.OrderId, delivered.Select(o => o.OrderId));
            if (productIds is not null) filter &= Builders<OrderItemDocument>.Filter.In(i => i.ProductId, productIds.Select(id => (long?)id));
            var lines = await orderItems.Find(filter).ToListAsync();
            var byOrder = delivered.ToDictionary(o => o.OrderId);
            return lines.Where(l => l.ProductId is not null).Select(l => (byOrder[l.OrderId], l))
                .OrderByDescending(x => x.Item1.DeliveredAt).ThenByDescending(x => x.l.OrderItemId).ToList();
        }

        async Task<bool> ActiveProductExists(long productId) =>
            await products.Find(p => p.ProductId == productId && p.Status == "active").AnyAsync();

        app.MapGet("/api/products/{productId:long}/reviews", async (long productId, int? page, int? pageSize, int? rating, bool? hasComment) =>
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

            if (rating is < 1 or > 5) return Results.ValidationProblem(new Dictionary<string, string[]> { ["rating"] = ["Rating must be between 1 and 5."] });
            if (!await ActiveProductExists(productId))
                return Results.NotFound(new { message = "Product not found." });

            var filter = Builders<ProductReviewDocument>.Filter.Where(
                r => r.ProductId == productId && r.Status == "published");
            var ratings = await reviews.Find(filter).Project(r => r.Rating).ToListAsync();
            var withComment = Builders<ProductReviewDocument>.Filter.Ne(r => r.Comment, null) & Builders<ProductReviewDocument>.Filter.Ne(r => r.Comment, "");
            var withCommentCount = await reviews.CountDocumentsAsync(filter & withComment);
            if (rating is { } stars) filter &= Builders<ProductReviewDocument>.Filter.Eq(r => r.Rating, stars);
            if (hasComment == true) filter &= withComment;
            var filteredCount = await reviews.CountDocumentsAsync(filter);
            var pageItems = await reviews.Find(filter)
                .Sort(Builders<ProductReviewDocument>.Sort.Descending(r => r.CreatedAt).Descending(r => r.ReviewId))
                .Skip((currentPage - 1) * currentPageSize).Limit(currentPageSize).ToListAsync();
            var userIds = pageItems.Select(r => r.UserId).Distinct().ToArray();
            var names = (await users.Find(Builders<User>.Filter.In(u => u.UserId, userIds)).ToListAsync())
                .ToDictionary(u => u.UserId, u => u.FullName);

            return Results.Ok(new ReviewPageResponse(
                pageItems.Select(r => new ReviewResponse(r.ReviewId, r.ProductId,
                    names.GetValueOrDefault(r.UserId), r.Rating, r.Comment, r.CreatedAt)).ToArray(),
                currentPage, currentPageSize, filteredCount,
                ratings.Count == 0 ? 0 : Math.Round(ratings.Average(), 2), ratings.Count,
                Enumerable.Range(1, 5).ToDictionary(star => star, star => ratings.Count(value => value == star)), withCommentCount));
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

            var purchases = await DeliveredPurchasesAsync(user!.UserId, [productId]);
            if (purchases.Count == 0)
            {
                return Results.Conflict(new
                {
                    message = "You can only review a product from an order that was delivered to you.",
                    code = "REVIEW_NOT_PURCHASED"
                });
            }

            var review = new ProductReviewDocument
            {
                ReviewId = await counters.NextAsync("product_reviews"),
                ProductId = productId,
                OrderItemId = purchases[0].Line.OrderItemId,
                UserId = user.UserId,
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

        app.MapGet("/api/customers/me/reviewable-items", async (HttpContext context) =>
        {
            var (user, error) = await currentUsers.ResolveAsync(context, UserRoles.Customer);
            if (error is not null) return error;
            var purchases = await DeliveredPurchasesAsync(user!.UserId);
            var productIds = purchases.Select(p => p.Line.ProductId!.Value).Distinct().ToArray();
            var reviewed = (await reviews.Find(r => r.UserId == user.UserId && productIds.Contains(r.ProductId))
                .Project(r => r.ProductId).ToListAsync()).ToHashSet();
            var active = (await products.Find(Builders<ProductDocument>.Filter.In(p => p.ProductId, productIds)
                & Builders<ProductDocument>.Filter.Eq(p => p.Status, "active")).Project(p => p.ProductId).ToListAsync()).ToHashSet();
            var primaryImage = (await productImages.Find(Builders<ProductImageDocument>.Filter.In(i => i.ProductId, productIds)).ToListAsync())
                .GroupBy(i => i.ProductId).ToDictionary(g => g.Key,
                    g => g.OrderBy(i => i.SortOrder ?? int.MaxValue).ThenBy(i => i.ImageId).First().ImageUrl);

            // One review per product, so list each product once, from its latest delivered order.
            var seen = new HashSet<long>();
            var result = new List<ReviewableItemResponse>();
            foreach (var (order, line) in purchases)
            {
                var productId = line.ProductId!.Value;
                if (reviewed.Contains(productId) || !active.Contains(productId) || !seen.Add(productId)) continue;
                result.Add(new ReviewableItemResponse(order.OrderCode, line.OrderItemId, productId, line.ProductNameSnapshot,
                    line.ImageUrlSnapshot ?? primaryImage.GetValueOrDefault(productId), line.SkuSnapshot,
                    line.SizeSnapshot, line.ColorSnapshot, order.DeliveredAt));
            }

            return Results.Ok(result);
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
            var ownImages = (await productImages.Find(Builders<ProductImageDocument>.Filter.In(i => i.ProductId, productIds)).ToListAsync())
                .GroupBy(i => i.ProductId).ToDictionary(g => g.Key, g => g.OrderBy(i => i.SortOrder ?? int.MaxValue).ThenBy(i => i.ImageId).First().ImageUrl);
            return Results.Ok(own.Select(r => new MyReviewResponse(r.ReviewId, r.ProductId,
                names.GetValueOrDefault(r.ProductId), r.Rating, r.Comment, r.Status, r.CreatedAt, ownImages.GetValueOrDefault(r.ProductId))).ToArray());
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
