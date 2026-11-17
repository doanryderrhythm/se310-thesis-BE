using MongoDB.Driver;

namespace AuthApi.Features.Products;

internal sealed record ProductDetailVariantResponse(
    long VariantId, string? Sku, string? Size, string? Color, string? HexCode,
    double? Price, string? Status, int QuantityOnHand, int QuantityReserved,
    int AvailableQuantity, bool InStock);

internal sealed record SizeChartItemResponse(
    string? SizeLabel, double? ChestCm, double? WaistCm, double? HipCm, double? LengthCm, string? Notes);

internal sealed record SizeChartResponse(
    long SizeChartId, string? Name, string? Description, SizeChartItemResponse[] Items);

internal sealed record ProductDetailResponse(
    long ProductId, long CategoryId, string? CategoryName, string Name, string? Description,
    string? Material, double? BasePrice, string? Status,
    ProductImageResponse[] Images, ProductDetailVariantResponse[] Variants,
    int TotalAvailableQuantity, SizeChartResponse? SizeChart);

internal static class ProductEndpoints
{
    public static void MapProductEndpoints(this IEndpointRouteBuilder app, IMongoDatabase db)
    {
        var catalog = new ProductCatalogService(db);
        app.MapGet("/api/products/{productId:long}", async (long productId) =>
        {
            var product = await catalog.FindProductAsync(productId);
            if (product is null || product.Status != "active")
            {
                return Results.NotFound(new { message = "Product not found." });
            }

            var bundle = await catalog.LoadForProductsAsync([product]);
            var variants = bundle.VariantsByProduct.GetValueOrDefault(productId, [])
                .Where(v => v.Status == "active")
                .Select(v =>
                {
                    bundle.Colors.TryGetValue(v.ColorId, out var color);
                    bundle.Inventory.TryGetValue(v.VariantId, out var stock);
                    var available = bundle.AvailableFor(v.VariantId);
                    return new ProductDetailVariantResponse(
                        v.VariantId, v.Sku, bundle.SizeNames.GetValueOrDefault(v.SizeId),
                        color?.Name, color?.HexCode, v.Price ?? product.BasePrice, v.Status,
                        stock?.QuantityOnHand ?? 0, stock?.QuantityReserved ?? 0,
                        available, available > 0);
                }).ToArray();

            SizeChartResponse? chartResponse = null;
            var chart = await catalog.LoadSizeChartAsync(product.SizeChartId);
            if (chart is { } found)
            {
                chartResponse = new SizeChartResponse(
                    found.Chart.SizeChartId, found.Chart.Name, found.Chart.Description,
                    found.Items.Select(i => new SizeChartItemResponse(
                        i.SizeLabel, i.ChestCm, i.WaistCm, i.HipCm, i.LengthCm, i.Notes)).ToArray());
            }

            return Results.Ok(new ProductDetailResponse(
                product.ProductId, product.CategoryId, bundle.CategoryNames.GetValueOrDefault(product.CategoryId),
                product.Name, product.Description, product.Material, product.BasePrice, product.Status,
                bundle.ImagesByProduct.GetValueOrDefault(productId, [])
                    .Select(i => new ProductImageResponse(i.ImageUrl, i.AltText)).ToArray(),
                variants, variants.Sum(v => v.AvailableQuantity), chartResponse));
        });
    }
}
