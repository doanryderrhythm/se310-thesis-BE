using AuthApi.Common;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AuthApi.Features.Locations;

[BsonIgnoreExtraElements]
internal sealed class ProvinceDocument
{
    [BsonElement("province_code")] public int ProvinceCode { get; set; }
    [BsonElement("name")] public string Name { get; set; } = string.Empty;
}

[BsonIgnoreExtraElements]
internal sealed class DistrictDocument
{
    [BsonElement("district_code")] public int DistrictCode { get; set; }
    [BsonElement("province_code")] public int ProvinceCode { get; set; }
    [BsonElement("name")] public string Name { get; set; } = string.Empty;
}

[BsonIgnoreExtraElements]
internal sealed class WardDocument
{
    [BsonElement("ward_code")] public int WardCode { get; set; }
    [BsonElement("district_code")] public int DistrictCode { get; set; }
    [BsonElement("province_code")] public int ProvinceCode { get; set; }
    [BsonElement("name")] public string Name { get; set; } = string.Empty;
}

internal sealed record LocationOption(int Code, string Name);

/// <summary>A province/district/ward triple that exists in the catalog, with canonical names.</summary>
internal sealed record ResolvedLocation(
    int ProvinceCode, string Province, int DistrictCode, string District, int WardCode, string Ward);

/// <summary>
/// Vietnamese administrative units (3 levels: province → district → ward, before the July 2025
/// reorganisation), seeded by mongo-init/02a-seed-locations.js. The catalog is small and static,
/// so it is cached in memory after the first successful load.
/// </summary>
internal sealed class LocationService(IMongoDatabase db)
{
    private static readonly string[] Prefixes =
        ["thanh pho ", "tp ", "tinh ", "quan ", "huyen ", "thi xa ", "phuong ", "xa ", "thi tran ", "p ", "q "];

    private readonly SemaphoreSlim gate = new(1, 1);
    private Catalog? cache;

    private sealed record Catalog(
        LocationOption[] Provinces,
        Dictionary<int, LocationOption[]> DistrictsByProvince,
        Dictionary<int, LocationOption[]> WardsByDistrict);

    public async Task<LocationOption[]> ProvincesAsync() => (await LoadAsync()).Provinces;

    public async Task<LocationOption[]?> DistrictsAsync(int provinceCode) =>
        (await LoadAsync()).DistrictsByProvince.GetValueOrDefault(provinceCode);

    public async Task<LocationOption[]?> WardsAsync(int districtCode) =>
        (await LoadAsync()).WardsByDistrict.GetValueOrDefault(districtCode);

    /// <summary>
    /// Validates an address location given by codes (preferred) or, for older clients, by names
    /// ("Thành phố Hồ Chí Minh", "Quận 1", "Phường Bến Nghé"; accents, case and the unit prefix
    /// are ignored). Errors use the keys province / district / ward.
    /// </summary>
    public async Task<(ResolvedLocation? Location, Dictionary<string, string[]> Errors)> ResolveAsync(
        int? provinceCode, int? districtCode, int? wardCode, string? province, string? district, string? ward)
    {
        var catalog = await LoadAsync();
        var errors = new Dictionary<string, string[]>();
        if (catalog.Provinces.Length == 0)
        {
            errors["province"] = ["The location catalog is not loaded; run mongo-init/02a-seed-locations.js."];
            return (null, errors);
        }

        var p = provinceCode is { } pc
            ? catalog.Provinces.FirstOrDefault(x => x.Code == pc)
            : Match(catalog.Provinces, province);
        if (p is null)
        {
            errors["province"] = ["Choose a valid province or city."];
            return (null, errors);
        }

        var districts = catalog.DistrictsByProvince.GetValueOrDefault(p.Code, []);
        var d = districtCode is { } dc ? districts.FirstOrDefault(x => x.Code == dc) : Match(districts, district);
        if (d is null)
        {
            errors["district"] = ["Choose a valid district that belongs to the selected province."];
            return (null, errors);
        }

        var wards = catalog.WardsByDistrict.GetValueOrDefault(d.Code, []);
        var w = wardCode is { } wc ? wards.FirstOrDefault(x => x.Code == wc) : Match(wards, ward);
        if (w is null)
        {
            errors["ward"] = ["Choose a valid ward that belongs to the selected district."];
            return (null, errors);
        }

        return (new ResolvedLocation(p.Code, p.Name, d.Code, d.Name, w.Code, w.Name), errors);
    }

    private static LocationOption? Match(LocationOption[] options, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var folded = Text.Fold(name);
        var exact = options.Where(o => Text.Fold(o.Name) == folded).ToArray();
        if (exact.Length == 1) return exact[0];
        var bare = StripPrefix(folded);
        var loose = options.Where(o => StripPrefix(Text.Fold(o.Name)) == bare).ToArray();
        return loose.Length == 1 ? loose[0] : null;
    }

    private static string StripPrefix(string folded)
    {
        foreach (var prefix in Prefixes)
        {
            if (folded.StartsWith(prefix)) return folded[prefix.Length..];
        }

        return folded;
    }

    private async Task<Catalog> LoadAsync()
    {
        if (cache is not null) return cache;
        await gate.WaitAsync();
        try
        {
            if (cache is not null) return cache;
            var provinces = await db.GetCollection<ProvinceDocument>("provinces").Find(Builders<ProvinceDocument>.Filter.Empty)
                .SortBy(p => p.ProvinceCode).ToListAsync();
            var districts = await db.GetCollection<DistrictDocument>("districts").Find(Builders<DistrictDocument>.Filter.Empty)
                .SortBy(d => d.DistrictCode).ToListAsync();
            var wards = await db.GetCollection<WardDocument>("wards").Find(Builders<WardDocument>.Filter.Empty)
                .SortBy(w => w.WardCode).ToListAsync();
            var catalog = new Catalog(
                provinces.Select(p => new LocationOption(p.ProvinceCode, p.Name)).ToArray(),
                districts.GroupBy(d => d.ProvinceCode).ToDictionary(g => g.Key, g => g.Select(d => new LocationOption(d.DistrictCode, d.Name)).ToArray()),
                wards.GroupBy(w => w.DistrictCode).ToDictionary(g => g.Key, g => g.Select(w => new LocationOption(w.WardCode, w.Name)).ToArray()));
            // An empty catalog (seed not run yet) is not cached, so it is picked up once seeded.
            if (catalog.Provinces.Length > 0) cache = catalog;
            return catalog;
        }
        finally
        {
            gate.Release();
        }
    }
}
