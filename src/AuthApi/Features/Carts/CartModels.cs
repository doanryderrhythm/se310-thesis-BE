using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace AuthApi.Features.Carts;

[BsonIgnoreExtraElements]
internal sealed class CartDocument
{
    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("cart_id")] public long CartId { get; set; }
    [BsonElement("user_id")] public long UserId { get; set; }
    [BsonElement("status")] public string Status { get; set; } = "active";
    [BsonElement("created_at")] public DateTime CreatedAt { get; set; }
    [BsonElement("updated_at")] public DateTime UpdatedAt { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class CartItemDocument
{
    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("cart_item_id")] public long CartItemId { get; set; }
    [BsonElement("cart_id")] public long CartId { get; set; }
    [BsonElement("variant_id")] public long VariantId { get; set; }
    [BsonElement("quantity")] public int Quantity { get; set; }
    [BsonElement("added_at")] public DateTime AddedAt { get; set; }
}

internal sealed record AddCartItemRequest(long? VariantId, int? Quantity);

internal sealed record CartItemResponse(
    long VariantId, long ProductId, string ProductName, string? ImageUrl, string? Sku,
    string? Size, string? Color, string? HexCode, double? UnitPrice, int Quantity,
    int AvailableQuantity, double LineTotal, bool IsPurchasable);

internal sealed record CartResponse(
    long? CartId, CartItemResponse[] Items, int TotalQuantity, double Subtotal);
