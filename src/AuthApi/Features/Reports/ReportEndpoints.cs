using AuthApi.Common;
using AuthApi.Security;
using MongoDB.Driver;

namespace AuthApi.Features.Reports;

internal static class ReportEndpoints
{
    public static void MapReportEndpoints(
        this IEndpointRouteBuilder app, IMongoCollection<User> users, ReportService reports)
    {
        var currentUsers = new CurrentUserService(users);
        var group = app.MapGroup("/api/admin").RequireAuthorization();

        group.MapGet("/dashboard", async (HttpContext c, string? from, string? to) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(c, UserRoles.Admin);
            if (error is not null) return error;
            if (!VnTime.TryParseRange(from, to, 30, ReportService.MaxDays, out var f, out var t, out var message))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["range"] = [message!] });
            return Results.Ok(await reports.DashboardAsync(f, t));
        });

        group.MapGet("/reports", async (HttpContext c, string? from, string? to, string? groupBy) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(c, UserRoles.Admin);
            if (error is not null) return error;
            var errors = new Dictionary<string, string[]>();
            if (groupBy is not (null or "" or "day" or "week")) errors["groupBy"] = ["groupBy must be day or week."];
            if (!VnTime.TryParseRange(from, to, 30, ReportService.MaxDays, out var f, out var t, out var message))
                errors["range"] = [message!];
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            return Results.Ok(await reports.ReportAsync(f, t, groupBy == "week"));
        });

        group.MapGet("/reports/export", async (HttpContext c, string? from, string? to, string? groupBy) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(c, UserRoles.Admin);
            if (error is not null) return error;
            var errors = new Dictionary<string, string[]>();
            if (groupBy is not (null or "" or "day" or "week")) errors["groupBy"] = ["groupBy must be day or week."];
            if (!VnTime.TryParseRange(from, to, 30, ReportService.MaxDays, out var f, out var t, out var message))
                errors["range"] = [message!];
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var csv = await reports.ExportCsvAsync(f, t, groupBy == "week");
            return Results.File(csv, "text/csv; charset=utf-8", $"report_{f:yyyy-MM-dd}_{t:yyyy-MM-dd}.csv");
        });
    }
}
