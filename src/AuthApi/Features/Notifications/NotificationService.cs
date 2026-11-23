using AuthApi.Data;
using MongoDB.Driver;

namespace AuthApi.Features.Notifications;

internal sealed class NotificationService(IMongoDatabase db, CounterService counters)
{
    private readonly IMongoCollection<NotificationDocument> notifications =
        db.GetCollection<NotificationDocument>("notifications");

    public static async Task SetupAsync(IMongoDatabase db, CounterService counters)
    {
        var notifications = db.GetCollection<NotificationDocument>("notifications");
        await notifications.Indexes.CreateOneAsync(new CreateIndexModel<NotificationDocument>(
            Builders<NotificationDocument>.IndexKeys.Ascending(n => n.UserId).Descending(n => n.CreatedAt),
            new CreateIndexOptions { Name = "user_created" }));
        await counters.InitializeAsync("notifications", await notifications
            .Find(Builders<NotificationDocument>.Filter.Empty)
            .SortByDescending(n => n.NotificationId).Project(n => n.NotificationId).FirstOrDefaultAsync());
    }

    public async Task CreateAsync(
        long userId, string type, string title, string body, Dictionary<string, long>? data)
    {
        await notifications.InsertOneAsync(new NotificationDocument
        {
            NotificationId = await counters.NextAsync("notifications"),
            UserId = userId,
            Type = type,
            Title = title,
            Body = body,
            Data = data,
            CreatedAt = DateTime.UtcNow
        });
    }

    public async Task<NotificationPageResponse> ListAsync(long userId, bool unreadOnly, int page, int pageSize)
    {
        var filter = Builders<NotificationDocument>.Filter.Eq(n => n.UserId, userId);
        var unread = Builders<NotificationDocument>.Filter.Eq(n => n.ReadAt, null);
        var listFilter = unreadOnly ? filter & unread : filter;
        var total = await notifications.CountDocumentsAsync(listFilter);
        var items = await notifications.Find(listFilter)
            .SortByDescending(n => n.CreatedAt).ThenByDescending(n => n.NotificationId)
            .Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync();
        return new NotificationPageResponse(
            items.Select(ToResponse).ToArray(), page, pageSize, total,
            await notifications.CountDocumentsAsync(filter & unread));
    }

    public Task<long> UnreadCountAsync(long userId) =>
        notifications.CountDocumentsAsync(n => n.UserId == userId && n.ReadAt == null);

    public async Task<bool> MarkReadAsync(long userId, long notificationId)
    {
        var result = await notifications.UpdateOneAsync(
            n => n.UserId == userId && n.NotificationId == notificationId && n.ReadAt == null,
            Builders<NotificationDocument>.Update.Set(n => n.ReadAt, DateTime.UtcNow));
        if (result.ModifiedCount > 0) return true;
        // Already read counts as success; only a missing/foreign notification is a 404.
        return await notifications.Find(n => n.UserId == userId && n.NotificationId == notificationId).AnyAsync();
    }

    public async Task<long> MarkAllReadAsync(long userId) =>
        (await notifications.UpdateManyAsync(
            n => n.UserId == userId && n.ReadAt == null,
            Builders<NotificationDocument>.Update.Set(n => n.ReadAt, DateTime.UtcNow))).ModifiedCount;

    private static NotificationResponse ToResponse(NotificationDocument n) =>
        new(n.NotificationId, n.Type, n.Title, n.Body, n.Data, n.ReadAt is not null, n.ReadAt, n.CreatedAt);
}
