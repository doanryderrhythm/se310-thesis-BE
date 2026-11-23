using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace AuthApi.Features.Notifications;

[BsonIgnoreExtraElements]
internal sealed class NotificationDocument
{
    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("notification_id")] public long NotificationId { get; set; }
    [BsonElement("user_id")] public long UserId { get; set; }
    [BsonElement("type")] public string Type { get; set; } = string.Empty;
    [BsonElement("title")] public string Title { get; set; } = string.Empty;
    [BsonElement("body")] public string Body { get; set; } = string.Empty;
    [BsonElement("data")] public Dictionary<string, long>? Data { get; set; }
    [BsonElement("read_at")] public DateTime? ReadAt { get; set; }
    [BsonElement("created_at")] public DateTime CreatedAt { get; set; }
}

internal sealed record NotificationResponse(
    long NotificationId, string Type, string Title, string Body,
    Dictionary<string, long>? Data, bool IsRead, DateTime? ReadAt, DateTime CreatedAt);

internal sealed record NotificationPageResponse(
    NotificationResponse[] Items, int Page, int PageSize, long TotalCount, long UnreadCount);

internal sealed record UnreadCountResponse(long UnreadCount);
