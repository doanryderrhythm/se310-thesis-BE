namespace AuthApi.Features.Catalog;

// ---- Requests ---------------------------------------------------------------------------

internal sealed record CategoryRequest(string? Name, string? Slug, string? Description, int? DisplayOrder, string? Status);
internal sealed record SizeRequest(string? Name, string? Status, int? DisplayOrder = null);
internal sealed record ColorRequest(string? Name, string? HexCode, string? Status);

/// <summary>Material (chất liệu) and occasion (công dụng) share the same shape.</summary>
internal sealed record AttributeRequest(string? Name, string? Slug, string? Description, int? DisplayOrder, string? Status);

internal sealed record SizeChartItemInput(
    string? SizeLabel, double? ChestCm, double? WaistCm, double? HipCm, double? LengthCm, string? Notes,
    double? HeightMinCm = null, double? HeightMaxCm = null, double? WeightMinKg = null, double? WeightMaxKg = null,
    int? DisplayOrder = null);
internal sealed record SizeChartRequest(
    string? Name, string? Description, long? CategoryId, SizeChartItemInput[]? Items, bool? IsDefault = null);
internal sealed record DuplicateSizeChartRequest(string? Name);

/// <summary>
/// An image of a product: either an upload (<c>mediaId</c> from POST /api/admin/uploads/images) or a plain
/// <c>url</c> (a path like /images/products/x.jpg or an http(s) URL). <c>focalX/focalY</c> are 0..1.
/// </summary>
internal sealed record ImageInput(
    string? Url, string? AltText, long? ColorId, int? SortOrder,
    long? MediaId = null, bool? IsPrimary = null, double? FocalX = null, double? FocalY = null);

/// <summary>Stock cannot be set here any more: goods come in through goods receipts.</summary>
internal sealed record VariantInput(
    string? Sku, long? SizeId, long? ColorId, double? Price, string? Status, int? InitialOnHand, int? LowStockThreshold);

internal sealed record CreateProductRequest(
    string? Name, string? Slug, long? CategoryId, string? Description, string? Material, double? BasePrice,
    long? SizeChartId, string? Status, ImageInput[]? Images, VariantInput[]? Variants,
    long? MaterialId = null, string? MaterialDetail = null, long[]? OccasionIds = null, string? CareInstructions = null);

/// <summary>
/// PATCH: omitted fields stay unchanged; <c>sizeChartId: 0</c> detaches the size chart and
/// <c>materialId: 0</c> clears the material. <c>occasionIds</c>, when sent, replaces the whole list.
/// <c>materialDetail</c> (alias <c>material</c>) is the free text such as "95% cotton, 5% spandex".
/// </summary>
internal sealed record UpdateProductRequest(
    string? Name, string? Slug, long? CategoryId, string? Description, string? Material, double? BasePrice,
    long? SizeChartId, long? MaterialId = null, string? MaterialDetail = null, long[]? OccasionIds = null,
    string? CareInstructions = null);

internal sealed record ProductStatusRequest(string? Status);
internal sealed record ReplaceImagesRequest(ImageInput[]? Images);
internal sealed record UpdateVariantRequest(string? Sku, double? Price, string? Status);

// ---- Responses --------------------------------------------------------------------------

internal sealed record AdminCategoryResponse(
    long CategoryId, string Name, string? Slug, string? Description, int DisplayOrder, string Status,
    long ProductCount, DateTime? CreatedAt, DateTime? UpdatedAt);

internal sealed record AttributeResponse(
    long Id, string Name, string Slug, string? Description, int DisplayOrder, string Status,
    long ProductCount, DateTime? CreatedAt, DateTime? UpdatedAt);

internal sealed record AdminSizeResponse(long SizeId, string Name, string Status, long VariantCount, int DisplayOrder);
internal sealed record AdminColorResponse(long ColorId, string Name, string? HexCode, string Status, long VariantCount);

internal sealed record AdminSizeChartItemResponse(
    long SizeChartItemId, string? SizeLabel, double? ChestCm, double? WaistCm, double? HipCm,
    double? LengthCm, string? Notes, double? HeightMinCm, double? HeightMaxCm, double? WeightMinKg,
    double? WeightMaxKg, int DisplayOrder);
internal sealed record AdminSizeChartResponse(
    long SizeChartId, string? Name, string? Description, long? CategoryId, string? CategoryName,
    int ItemCount, long ProductCount, DateTime? CreatedAt, AdminSizeChartItemResponse[]? Items, bool IsDefault);

internal sealed record AdminProductListItem(
    long ProductId, string Name, string? Slug, long CategoryId, string? CategoryName, string Status,
    string? ThumbnailUrl, int VariantCount, double? PriceMin, double? PriceMax, int TotalAvailable,
    DateTime? CreatedAt, DateTime? UpdatedAt, long? MaterialId = null, string? MaterialName = null);

internal sealed record AdminImageResponse(
    long ImageId, string? Url, string? AltText, long? ColorId, int SortOrder, bool IsPrimary = false,
    long? MediaId = null, int? Width = null, int? Height = null, double? FocalX = null, double? FocalY = null,
    Dictionary<string, string>? Srcset = null);

internal sealed record AdminVariantResponse(
    long VariantId, string? Sku, long SizeId, string? Size, long ColorId, string? Color, string? HexCode,
    double? Price, string Status, int OnHand, int Reserved, int Available, int LowStockThreshold, bool HasOrders);

internal sealed record NamedRef(long Id, string Name);

internal sealed record AdminProductDetail(
    long ProductId, string Name, string? Slug, long CategoryId, string? CategoryName, string? Description,
    string? Material, double? BasePrice, string Status, long? SizeChartId,
    AdminImageResponse[] Images, AdminVariantResponse[] Variants, DateTime? CreatedAt, DateTime? UpdatedAt,
    long? MaterialId = null, string? MaterialName = null, string? MaterialDetail = null,
    NamedRef[]? Occasions = null, string? CareInstructions = null, string[]? Warnings = null);

internal sealed record UploadResponse(
    string Url, string FileName, long Size, long MediaId, int Width, int Height, double AspectRatio,
    string OriginalFormat, Dictionary<string, string> Srcset, string[] Warnings);
