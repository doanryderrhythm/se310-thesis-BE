namespace AuthApi.Features.Catalog;

// ---- Requests ---------------------------------------------------------------------------

internal sealed record CategoryRequest(string? Name, string? Slug, string? Description, int? DisplayOrder, string? Status);
internal sealed record SizeRequest(string? Name, string? Status);
internal sealed record ColorRequest(string? Name, string? HexCode, string? Status);

internal sealed record SizeChartItemInput(
    string? SizeLabel, double? ChestCm, double? WaistCm, double? HipCm, double? LengthCm, string? Notes);
internal sealed record SizeChartRequest(
    string? Name, string? Description, long? CategoryId, SizeChartItemInput[]? Items);

internal sealed record ImageInput(string? Url, string? AltText, long? ColorId, int? SortOrder);
internal sealed record VariantInput(
    string? Sku, long? SizeId, long? ColorId, double? Price, string? Status, int? InitialOnHand, int? LowStockThreshold);

internal sealed record CreateProductRequest(
    string? Name, string? Slug, long? CategoryId, string? Description, string? Material, double? BasePrice,
    long? SizeChartId, string? Status, ImageInput[]? Images, VariantInput[]? Variants);

/// <summary>PATCH: omitted fields stay unchanged; <c>sizeChartId: 0</c> detaches the size chart.</summary>
internal sealed record UpdateProductRequest(
    string? Name, string? Slug, long? CategoryId, string? Description, string? Material, double? BasePrice,
    long? SizeChartId);

internal sealed record ProductStatusRequest(string? Status);
internal sealed record ReplaceImagesRequest(ImageInput[]? Images);
internal sealed record UpdateVariantRequest(string? Sku, double? Price, string? Status);

// ---- Responses --------------------------------------------------------------------------

internal sealed record AdminCategoryResponse(
    long CategoryId, string Name, string? Slug, string? Description, int DisplayOrder, string Status,
    long ProductCount, DateTime? CreatedAt, DateTime? UpdatedAt);

internal sealed record AdminSizeResponse(long SizeId, string Name, string Status, long VariantCount);
internal sealed record AdminColorResponse(long ColorId, string Name, string? HexCode, string Status, long VariantCount);

internal sealed record AdminSizeChartItemResponse(
    long SizeChartItemId, string? SizeLabel, double? ChestCm, double? WaistCm, double? HipCm,
    double? LengthCm, string? Notes);
internal sealed record AdminSizeChartResponse(
    long SizeChartId, string? Name, string? Description, long? CategoryId, string? CategoryName,
    int ItemCount, long ProductCount, DateTime? CreatedAt, AdminSizeChartItemResponse[]? Items);

internal sealed record AdminProductListItem(
    long ProductId, string Name, string? Slug, long CategoryId, string? CategoryName, string Status,
    string? ThumbnailUrl, int VariantCount, double? PriceMin, double? PriceMax, int TotalAvailable,
    DateTime? CreatedAt, DateTime? UpdatedAt);

internal sealed record AdminImageResponse(long ImageId, string? Url, string? AltText, long? ColorId, int SortOrder);

internal sealed record AdminVariantResponse(
    long VariantId, string? Sku, long SizeId, string? Size, long ColorId, string? Color, string? HexCode,
    double? Price, string Status, int OnHand, int Reserved, int Available, int LowStockThreshold, bool HasOrders);

internal sealed record AdminProductDetail(
    long ProductId, string Name, string? Slug, long CategoryId, string? CategoryName, string? Description,
    string? Material, double? BasePrice, string Status, long? SizeChartId,
    AdminImageResponse[] Images, AdminVariantResponse[] Variants, DateTime? CreatedAt, DateTime? UpdatedAt);

internal sealed record UploadResponse(string Url, string FileName, long Size);
