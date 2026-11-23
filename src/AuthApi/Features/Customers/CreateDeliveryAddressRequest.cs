namespace AuthApi.Features.Customers;

/// <summary>
/// Send <c>provinceCode</c>, <c>districtCode</c>, <c>wardCode</c> from GET /api/locations/*.
/// For older clients the names (<c>province</c>, <c>district</c>, <c>ward</c>) are still accepted
/// and matched against the catalog; the stored names are always the catalog's canonical ones.
/// </summary>
internal sealed record CreateDeliveryAddressRequest(
    string? RecipientName,
    string? RecipientPhone,
    string? AddressLine,
    string? Ward,
    string? District,
    string? Province,
    bool? IsDefault,
    int? ProvinceCode = null,
    int? DistrictCode = null,
    int? WardCode = null);

/// <summary>A validated, normalized address ready to store.</summary>
internal sealed record NormalizedAddress(
    string RecipientName, string RecipientPhone, string AddressLine,
    AuthApi.Features.Locations.ResolvedLocation Location, bool IsDefault);
