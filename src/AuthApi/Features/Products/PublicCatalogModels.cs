namespace AuthApi.Features.Products;

/// <summary>Image of a product for the storefront. <c>Srcset</c> keys are w400, w800, w1200 (uploads only).</summary>
internal sealed record PublicImage(
    long ImageId, string? ImageUrl, string? AltText, long? ColorId = null, int SortOrder = 0, bool IsPrimary = false,
    int? Width = null, int? Height = null, double? FocalX = null, double? FocalY = null,
    Dictionary<string, string>? Srcset = null);

internal sealed record PublicRef(long Id, string Name, string Slug);

internal sealed record PublicColor(long ColorId, string? Name, string? HexCode);

internal sealed record ProductListItem(
    long ProductId, string? Slug, string Name, long CategoryId, string? CategoryName,
    double? PriceMin, double? PriceMax, PublicImage? Image, PublicImage? HoverImage,
    PublicColor[] Colors, string[] Sizes, bool InStock, string? Material, PublicRef? MaterialGroup,
    PublicRef[] Occasions, DateTime? CreatedAt);

internal sealed record PublicCategory(
    long CategoryId, string Name, string? Slug, string? Description, int DisplayOrder, long ProductCount);

internal sealed record PublicAttribute(
    long Id, string Name, string Slug, string? Description, int DisplayOrder, long ProductCount);

internal sealed record SizeSuggestionCandidate(string SizeLabel, double Distance);

internal sealed record SizeSuggestionResponse(
    string? SuggestedSize, string MatchType, bool InStock, SizeSuggestionCandidate[] Candidates);

internal sealed record ProductListQuery(
    string? Q, string? Category, string[] Sizes, string[] Materials, string[] Occasions,
    double? MinPrice, double? MaxPrice, bool InStock, string Sort, int Page, int PageSize);
