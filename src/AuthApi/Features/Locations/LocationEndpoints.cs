using MongoDB.Driver;

namespace AuthApi.Features.Locations;

/// <summary>Public dropdown data for address forms: province → district → ward.</summary>
internal static class LocationEndpoints
{
    public static void MapLocationEndpoints(this IEndpointRouteBuilder app, LocationService locations)
    {
        var group = app.MapGroup("/api/locations");
        group.MapGet("/provinces", async (HttpContext c) =>
        {
            c.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.Ok(await locations.ProvincesAsync());
        });
        group.MapGet("/provinces/{provinceCode:int}/districts", async (HttpContext c, int provinceCode) =>
        {
            var list = await locations.DistrictsAsync(provinceCode);
            if (list is null) return Results.NotFound();
            c.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.Ok(list);
        });
        group.MapGet("/districts/{districtCode:int}/wards", async (HttpContext c, int districtCode) =>
        {
            var list = await locations.WardsAsync(districtCode);
            if (list is null) return Results.NotFound();
            c.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.Ok(list);
        });
    }
}
