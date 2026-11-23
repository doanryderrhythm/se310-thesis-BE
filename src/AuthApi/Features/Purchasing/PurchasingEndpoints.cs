using AuthApi.Common;
using AuthApi.Features.Promotions;
using AuthApi.Security;
using MongoDB.Driver;

namespace AuthApi.Features.Purchasing;

internal static class PurchasingEndpoints
{
    public static void MapPurchasingEndpoints(
        this IEndpointRouteBuilder app, IMongoCollection<User> users, SupplierService supplierService,
        GoodsReceiptService receiptService, IConfiguration configuration)
    {
        var currentUsers = new CurrentUserService(users);
        Task<(User? User, IResult? Error)> Admin(HttpContext c) => currentUsers.ResolveAsync(c, UserRoles.Admin);
        var admin = app.MapGroup("/api/admin").RequireAuthorization();

        // ---- Suppliers
        admin.MapGet("/suppliers", async (HttpContext c, string? q, string? status, int? page, int? pageSize) =>
        {
            var (_, error) = await Admin(c);
            if (error is not null) return error;
            if (status is not (null or "" or "active" or "hidden"))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Status must be active or hidden."] });
            return Paging.TryGet(page, pageSize, out var p, out var ps)
                ?? Results.Ok(await supplierService.ListAsync(q, status, p, ps));
        });
        admin.MapGet("/suppliers/{id:long}", async (HttpContext c, long id) =>
        {
            var (_, error) = await Admin(c);
            if (error is not null) return error;
            var supplier = await supplierService.GetAsync(id);
            return supplier is null ? Results.NotFound() : Results.Ok(supplier);
        });
        admin.MapPost("/suppliers", async (HttpContext c, SupplierRequest r) =>
        {
            var (_, error) = await Admin(c);
            if (error is not null) return error;
            var (supplier, createError) = await supplierService.CreateAsync(r);
            return createError ?? Results.Created($"/api/admin/suppliers/{supplier!.SupplierId}", supplier);
        });
        admin.MapPatch("/suppliers/{id:long}", async (HttpContext c, long id, SupplierRequest r) =>
        {
            var (_, error) = await Admin(c);
            if (error is not null) return error;
            var (supplier, updateError) = await supplierService.UpdateAsync(id, r);
            return updateError ?? Results.Ok(supplier);
        });
        admin.MapDelete("/suppliers/{id:long}", async (HttpContext c, long id) =>
        {
            var (_, error) = await Admin(c);
            return error ?? await supplierService.DeleteAsync(id);
        });

        // ---- Goods receipts
        admin.MapGet("/goods-receipts", async (HttpContext c, string? q, string? status, long? supplierId,
            string? from, string? to, int? page, int? pageSize) =>
        {
            var (_, error) = await Admin(c);
            if (error is not null) return error;
            var errors = new Dictionary<string, string[]>();
            if (status is not (null or "") && !ReceiptStatuses.All.Contains(status))
                errors["status"] = [$"Status must be one of: {string.Join(", ", ReceiptStatuses.All)}."];
            DateTime? fromUtc = null, toUtc = null;
            if (!string.IsNullOrWhiteSpace(from))
            {
                if (VnTime.TryDay(from, out var f)) fromUtc = VnTime.StartOfDayUtc(f);
                else errors["from"] = ["from must be a date like 2026-11-23."];
            }
            if (!string.IsNullOrWhiteSpace(to))
            {
                if (VnTime.TryDay(to, out var t)) toUtc = VnTime.StartOfDayUtc(t.AddDays(1));
                else errors["to"] = ["to must be a date like 2026-11-23."];
            }
            var bad = Paging.TryGet(page, pageSize, out var p, out var ps);
            if (bad is not null) return bad;
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            return Results.Ok(await receiptService.ListAsync(q, status, supplierId, fromUtc, toUtc, p, ps));
        });
        admin.MapPost("/goods-receipts", async (HttpContext c, ReceiptRequest r) =>
        {
            var (user, error) = await Admin(c);
            if (error is not null) return error;
            var (receipt, createError) = await receiptService.CreateAsync(user!.UserId, r);
            return createError ?? Results.Created($"/api/admin/goods-receipts/{receipt!.ReceiptCode}", receipt);
        });
        admin.MapGet("/goods-receipts/{code}", async (HttpContext c, string code) =>
        {
            var (_, error) = await Admin(c);
            if (error is not null) return error;
            var receipt = await receiptService.GetAsync(code);
            return receipt is null ? Results.NotFound() : Results.Ok(receipt);
        });
        admin.MapPut("/goods-receipts/{code}", async (HttpContext c, string code, ReceiptRequest r) =>
        {
            var (_, error) = await Admin(c);
            if (error is not null) return error;
            var (receipt, updateError) = await receiptService.UpdateAsync(code, r);
            return updateError ?? Results.Ok(receipt);
        });
        admin.MapPost("/goods-receipts/{code}/complete", async (HttpContext c, string code) =>
        {
            var (user, error) = await Admin(c);
            if (error is not null) return error;
            var (receipt, completeError) = await receiptService.CompleteAsync(user!.UserId, code);
            return completeError ?? Results.Ok(receipt);
        });
        admin.MapPost("/goods-receipts/{code}/cancel", async (HttpContext c, string code, CancelReceiptRequest r) =>
        {
            var (user, error) = await Admin(c);
            if (error is not null) return error;
            var (receipt, cancelError) = await receiptService.CancelAsync(user!.UserId, code, r.Reason);
            return cancelError ?? Results.Ok(receipt);
        });
        admin.MapGet("/goods-receipts/{code}/print", async (HttpContext c, string code) =>
        {
            var (_, error) = await Admin(c);
            if (error is not null) return error;
            var receipt = await receiptService.GetAsync(code);
            if (receipt is null) return Results.NotFound();
            string? Setting(string key) => string.IsNullOrWhiteSpace(configuration[$"Store:{key}"]) ? null : configuration[$"Store:{key}"];
            var html = GoodsReceiptPrinter.Render(receipt, Setting("Name") ?? "Themis Fashion Studio", Setting("Address"), Setting("Phone"));
            return Results.Content(html, "text/html; charset=utf-8");
        });
    }
}
