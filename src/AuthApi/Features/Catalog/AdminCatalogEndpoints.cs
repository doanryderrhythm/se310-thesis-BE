using AuthApi.Common;
using AuthApi.Security;
using MongoDB.Driver;

namespace AuthApi.Features.Catalog;

internal static class AdminCatalogEndpoints
{
    public static void MapAdminCatalogEndpoints(
        this IEndpointRouteBuilder app, IMongoCollection<User> users,
        AdminTaxonomyService taxonomy, AdminProductService productService,
        AdminAttributeService materials, AdminAttributeService occasions)
    {
        var currentUsers = new CurrentUserService(users);
        Task<(User? User, IResult? Error)> Admin(HttpContext c) => currentUsers.ResolveAsync(c, UserRoles.Admin);
        var admin = app.MapGroup("/api/admin").RequireAuthorization();

        // ---- Categories (A04)
        admin.MapGet("/categories", async (HttpContext c, string? q, string? status) =>
        {
            var (_, error) = await Admin(c);
            return error ?? Results.Ok(await taxonomy.ListCategoriesAsync(q, status));
        });
        admin.MapGet("/categories/{id:long}", async (HttpContext c, long id) =>
        {
            var (_, error) = await Admin(c);
            if (error is not null) return error;
            var category = await taxonomy.GetCategoryAsync(id);
            return category is null ? Results.NotFound() : Results.Ok(category);
        });
        admin.MapPost("/categories", async (HttpContext c, CategoryRequest r) =>
        {
            var (_, error) = await Admin(c);
            if (error is not null) return error;
            var (category, createError) = await taxonomy.CreateCategoryAsync(r);
            return createError ?? Results.Created($"/api/admin/categories/{category!.CategoryId}", category);
        });
        admin.MapPatch("/categories/{id:long}", async (HttpContext c, long id, CategoryRequest r) =>
        {
            var (_, error) = await Admin(c);
            if (error is not null) return error;
            var (category, updateError) = await taxonomy.UpdateCategoryAsync(id, r);
            return updateError ?? Results.Ok(category);
        });
        admin.MapDelete("/categories/{id:long}", async (HttpContext c, long id) =>
        {
            var (_, error) = await Admin(c);
            return error ?? await taxonomy.DeleteCategoryAsync(id);
        });

        // ---- Sizes and colors
        admin.MapGet("/sizes", async (HttpContext c) =>
        {
            var (_, error) = await Admin(c);
            return error ?? Results.Ok(await taxonomy.ListSizesAsync());
        });
        admin.MapPost("/sizes", async (HttpContext c, SizeRequest r) =>
        {
            var (_, error) = await Admin(c);
            if (error is not null) return error;
            var (size, createError) = await taxonomy.CreateSizeAsync(r);
            return createError ?? Results.Created($"/api/admin/sizes/{size!.SizeId}", size);
        });
        admin.MapPatch("/sizes/{id:long}", async (HttpContext c, long id, SizeRequest r) =>
        {
            var (_, error) = await Admin(c);
            if (error is not null) return error;
            var (size, updateError) = await taxonomy.UpdateSizeAsync(id, r);
            return updateError ?? Results.Ok(size);
        });
        admin.MapGet("/colors", async (HttpContext c) =>
        {
            var (_, error) = await Admin(c);
            return error ?? Results.Ok(await taxonomy.ListColorsAsync());
        });
        admin.MapPost("/colors", async (HttpContext c, ColorRequest r) =>
        {
            var (_, error) = await Admin(c);
            if (error is not null) return error;
            var (color, createError) = await taxonomy.CreateColorAsync(r);
            return createError ?? Results.Created($"/api/admin/colors/{color!.ColorId}", color);
        });
        admin.MapPatch("/colors/{id:long}", async (HttpContext c, long id, ColorRequest r) =>
        {
            var (_, error) = await Admin(c);
            if (error is not null) return error;
            var (color, updateError) = await taxonomy.UpdateColorAsync(id, r);
            return updateError ?? Results.Ok(color);
        });

        admin.MapDelete("/sizes/{id:long}", async (HttpContext c, long id) =>
        {
            var (_, error) = await Admin(c);
            return error ?? await taxonomy.DeleteSizeAsync(id);
        });
        admin.MapDelete("/colors/{id:long}", async (HttpContext c, long id) =>
        {
            var (_, error) = await Admin(c);
            return error ?? await taxonomy.DeleteColorAsync(id);
        });

        // ---- Materials (chất liệu) and occasions (công dụng)
        foreach (var (route, service) in new[] { ("/materials", materials), ("/occasions", occasions) })
        {
            admin.MapGet(route, async (HttpContext c, string? q, string? status) =>
            {
                var (_, error) = await Admin(c);
                if (error is not null) return error;
                if (status is not (null or "" or "active" or "hidden"))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Status must be active or hidden."] });
                return Results.Ok(await service.ListAsync(q, status));
            });
            admin.MapGet(route + "/{id:long}", async (HttpContext c, long id) =>
            {
                var (_, error) = await Admin(c);
                if (error is not null) return error;
                var row = await service.GetAsync(id);
                return row is null ? Results.NotFound() : Results.Ok(row);
            });
            admin.MapPost(route, async (HttpContext c, AttributeRequest r) =>
            {
                var (_, error) = await Admin(c);
                if (error is not null) return error;
                var (row, createError) = await service.CreateAsync(r);
                return createError ?? Results.Created($"/api/admin{route}/{row!.Id}", row);
            });
            admin.MapPatch(route + "/{id:long}", async (HttpContext c, long id, AttributeRequest r) =>
            {
                var (_, error) = await Admin(c);
                if (error is not null) return error;
                var (row, updateError) = await service.UpdateAsync(id, r);
                return updateError ?? Results.Ok(row);
            });
            admin.MapDelete(route + "/{id:long}", async (HttpContext c, long id) =>
            {
                var (_, error) = await Admin(c);
                return error ?? await service.DeleteAsync(id);
            });
        }

        // ---- Size charts (A05)
        admin.MapGet("/size-charts", async (HttpContext c, long? categoryId) =>
        {
            var (_, error) = await Admin(c);
            return error ?? Results.Ok(await taxonomy.ListChartsAsync(categoryId));
        });
        admin.MapGet("/size-charts/{id:long}", async (HttpContext c, long id) =>
        {
            var (_, error) = await Admin(c);
            if (error is not null) return error;
            var chart = await taxonomy.GetChartAsync(id);
            return chart is null ? Results.NotFound() : Results.Ok(chart);
        });
        admin.MapPost("/size-charts", async (HttpContext c, SizeChartRequest r) =>
        {
            var (_, error) = await Admin(c);
            if (error is not null) return error;
            var (chart, saveError) = await taxonomy.SaveChartAsync(null, r);
            return saveError ?? Results.Created($"/api/admin/size-charts/{chart!.SizeChartId}", chart);
        });
        admin.MapPut("/size-charts/{id:long}", async (HttpContext c, long id, SizeChartRequest r) =>
        {
            var (_, error) = await Admin(c);
            if (error is not null) return error;
            var (chart, saveError) = await taxonomy.SaveChartAsync(id, r);
            return saveError ?? Results.Ok(chart);
        });
        admin.MapPost("/size-charts/{id:long}/duplicate", async (HttpContext c, long id, DuplicateSizeChartRequest? r) =>
        {
            var (_, error) = await Admin(c);
            if (error is not null) return error;
            var (chart, saveError) = await taxonomy.DuplicateChartAsync(id, r ?? new DuplicateSizeChartRequest(null));
            return saveError ?? Results.Created($"/api/admin/size-charts/{chart!.SizeChartId}", chart);
        });
        admin.MapDelete("/size-charts/{id:long}", async (HttpContext c, long id) =>
        {
            var (_, error) = await Admin(c);
            return error ?? await taxonomy.DeleteChartAsync(id);
        });

        // ---- Products (A02 / A03)
        admin.MapGet("/products", async (HttpContext c, string? q, long? categoryId, string? status, string? sort,
            int? page, int? pageSize, long? materialId, long? occasionId) =>
        {
            var (_, error) = await Admin(c);
            if (error is not null) return error;
            var errors = new Dictionary<string, string[]>();
            if (status is not (null or "" or "draft" or "active" or "hidden")) errors["status"] = ["Status must be draft, active or hidden."];
            if (sort is not (null or "" or "newest" or "name" or "price")) errors["sort"] = ["Sort must be newest, name or price."];
            var bad = Paging.TryGet(page, pageSize, out var p, out var ps);
            if (bad is not null) return bad;
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            return Results.Ok(await productService.ListAsync(q, categoryId, status, sort, p, ps, materialId, occasionId));
        });
        admin.MapPost("/products", async (HttpContext c, CreateProductRequest r) =>
        {
            var (user, error) = await Admin(c);
            if (error is not null) return error;
            var (product, createError) = await productService.CreateAsync(user!.UserId, r);
            return createError ?? Results.Created($"/api/admin/products/{product!.ProductId}", product);
        });
        admin.MapGet("/products/{id:long}", async (HttpContext c, long id) =>
        {
            var (_, error) = await Admin(c);
            if (error is not null) return error;
            var product = await productService.GetAsync(id);
            return product is null ? Results.NotFound() : Results.Ok(product);
        });
        admin.MapPatch("/products/{id:long}", async (HttpContext c, long id, UpdateProductRequest r) =>
        {
            var (_, error) = await Admin(c);
            if (error is not null) return error;
            var (product, updateError) = await productService.UpdateAsync(id, r);
            return updateError ?? Results.Ok(product);
        });
        admin.MapPatch("/products/{id:long}/status", async (HttpContext c, long id, ProductStatusRequest r) =>
        {
            var (_, error) = await Admin(c);
            if (error is not null) return error;
            var (product, statusError) = await productService.SetStatusAsync(id, r.Status);
            return statusError ?? Results.Ok(product);
        });
        admin.MapPut("/products/{id:long}/images", async (HttpContext c, long id, ReplaceImagesRequest r) =>
        {
            var (_, error) = await Admin(c);
            if (error is not null) return error;
            var (product, imageError) = await productService.ReplaceImagesAsync(id, r.Images);
            return imageError ?? Results.Ok(product);
        });
        admin.MapPost("/products/{id:long}/variants", async (HttpContext c, long id, VariantInput r) =>
        {
            var (user, error) = await Admin(c);
            if (error is not null) return error;
            var (product, variantError) = await productService.AddVariantAsync(user!.UserId, id, r);
            return variantError ?? Results.Created($"/api/admin/products/{id}", product);
        });
        admin.MapPatch("/variants/{variantId:long}", async (HttpContext c, long variantId, UpdateVariantRequest r) =>
        {
            var (_, error) = await Admin(c);
            if (error is not null) return error;
            var (product, variantError) = await productService.UpdateVariantAsync(variantId, r);
            return variantError ?? Results.Ok(product);
        });
        admin.MapDelete("/variants/{variantId:long}", async (HttpContext c, long variantId) =>
        {
            var (_, error) = await Admin(c);
            if (error is not null) return error;
            var (product, deleteError) = await productService.DeleteVariantAsync(variantId);
            return deleteError ?? Results.Ok(product);
        });
    }
}
