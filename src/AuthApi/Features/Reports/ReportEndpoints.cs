using AuthApi.Common;
using AuthApi.Security;
using MongoDB.Driver;

namespace AuthApi.Features.Reports;

internal static class ReportEndpoints
{
    /// <summary>Validates groupBy (default day) and the from/to range that fits that grouping.</summary>
    private static IResult? ParseQuery(
        string? from, string? to, string? groupBy, out DateOnly f, out DateOnly t, out string group)
    {
        group = string.IsNullOrWhiteSpace(groupBy) ? "day" : groupBy.Trim().ToLowerInvariant();
        f = t = default;
        if (!VnTime.GroupBys.Contains(group))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["groupBy"] = [$"groupBy must be one of: {string.Join(", ", VnTime.GroupBys)}."]
            });
        }

        return VnTime.TryParseRange(from, to, 30, VnTime.MaxDaysFor(group), out f, out t, out var message)
            ? null
            : Results.ValidationProblem(new Dictionary<string, string[]> { ["range"] = [message!] });
    }

    public static void MapReportEndpoints(
        this IEndpointRouteBuilder app, IMongoCollection<User> users, ReportService reports)
    {
        var currentUsers = new CurrentUserService(users);
        var group = app.MapGroup("/api/admin").RequireAuthorization();

        group.MapGet("/dashboard", async (HttpContext c, string? from, string? to, string? groupBy) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(c, UserRoles.Admin);
            if (error is not null) return error;
            return ParseQuery(from, to, groupBy, out var f, out var t, out var g)
                ?? Results.Ok(await reports.DashboardAsync(f, t, g));
        });

        group.MapGet("/reports", async (HttpContext c, string? from, string? to, string? groupBy) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(c, UserRoles.Admin);
            if (error is not null) return error;
            return ParseQuery(from, to, groupBy, out var f, out var t, out var g)
                ?? Results.Ok(await reports.ReportAsync(f, t, g));
        });

        group.MapGet("/reports/export", async (HttpContext c, string? from, string? to, string? groupBy) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(c, UserRoles.Admin);
            if (error is not null) return error;
            var invalid = ParseQuery(from, to, groupBy, out var f, out var t, out var g);
            if (invalid is not null) return invalid;
            var csv = await reports.ExportCsvAsync(f, t, g);
            return Results.File(csv, "text/csv; charset=utf-8", $"report_{g}_{f:yyyy-MM-dd}_{t:yyyy-MM-dd}.csv");
        });
    }
}
