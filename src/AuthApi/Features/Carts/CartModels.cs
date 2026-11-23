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
    /// <summary>Lines the customer ticked for checkout; a missing value counts as selected.</summary>
    [BsonElement("selected")] [BsonIgnoreIfNull] public bool? Selected { get; set; }
    /// <summary>Unit price when the line was added, to warn the customer when the price changed.</summary>
    [BsonElement("price_at_add")] [BsonIgnoreIfNull] public double? PriceAtAdd { get; set; }
}

internal sealed record AddCartItemRequest(long? VariantId, int? Quantity);

internal sealed record UpdateCartItemRequest(int? Quantity, bool? Selected);

internal sealed record MergeCartItem(long? VariantId, int? Quantity);
internal sealed record MergeCartRequest(MergeCartItem[]? Items);

internal sealed record CartItemResponse(
    long VariantId, long ProductId, string ProductName, string? ImageUrl, string? Sku,
    string? Size, string? Color, string? HexCode, double? UnitPrice, int Quantity,
    int AvailableQuantity, double LineTotal, bool IsPurchasable,
    bool Selected = true, double? PriceAtAdd = null, bool PriceChanged = false);

/// <summary><c>Subtotal</c> counts every purchasable line, <c>SelectedSubtotal</c> only the ticked ones (what checkout uses).</summary>
internal sealed record CartResponse(
    long? CartId, CartItemResponse[] Items, int TotalQuantity, double Subtotal,
    int SelectedQuantity = 0, double SelectedSubtotal = 0);

internal sealed record MergeLineResult(long VariantId, int Requested, int Added, int Quantity, string Status);

internal sealed record MergeCartResponse(CartResponse Cart, MergeLineResult[] Results);
