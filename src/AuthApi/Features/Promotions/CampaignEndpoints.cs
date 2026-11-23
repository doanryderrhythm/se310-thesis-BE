using AuthApi.Security;
using MongoDB.Driver;

namespace AuthApi.Features.Promotions;

internal static class CampaignEndpoints
{
    public static void MapCampaignEndpoints(
        this IEndpointRouteBuilder app, IMongoCollection<User> users, CampaignService campaigns)
    {
        var currentUsers = new CurrentUserService(users);
        var group = app.MapGroup("/api/admin/campaigns").RequireAuthorization();

        group.MapGet("", async (HttpContext context, string? status, string? trigger, string? q,
            int? page, int? pageSize) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(context, UserRoles.Admin);
            if (error is not null) return error;
            var errors = new Dictionary<string, string[]>();
            if (!CampaignService.IsValidStatusFilter(status))
                errors["status"] = ["Status must be draft, scheduled, active, paused or ended."];
            if (trigger is not (null or "" or Triggers.None or Triggers.Signup or Triggers.Manual or Triggers.Spend))
                errors["trigger"] = ["Trigger must be none, signup, manual or spend."];
            var paging = ValidatePaging(page, pageSize, errors);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            return Results.Ok(await campaigns.ListAsync(status, trigger, q, paging.Page, paging.PageSize));
        });

        group.MapPost("", async (HttpContext context, CreateCampaignRequest request) =>
        {
            var (admin, error) = await currentUsers.ResolveAsync(context, UserRoles.Admin);
            if (error is not null) return error;
            var (campaign, createError) = await campaigns.CreateAsync(admin!.UserId, request);
            return createError ?? Results.Created($"/api/admin/campaigns/{campaign!.CampaignId}", campaign);
        });

        group.MapGet("/{campaignId:long}", async (HttpContext context, long campaignId) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(context, UserRoles.Admin);
            if (error is not null) return error;
            var campaign = await campaigns.GetAsync(campaignId);
            return campaign is null ? Results.NotFound() : Results.Ok(campaign);
        });

        group.MapPatch("/{campaignId:long}", async (HttpContext context, long campaignId, UpdateCampaignRequest request) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(context, UserRoles.Admin);
            if (error is not null) return error;
            var (campaign, updateError) = await campaigns.UpdateAsync(campaignId, request);
            return updateError ?? Results.Ok(campaign);
        });

        foreach (var action in new[] { "pause", "resume", "end" })
        {
            group.MapPost($"/{{campaignId:long}}/{action}", async (HttpContext context, long campaignId) =>
            {
                var (_, error) = await currentUsers.ResolveAsync(context, UserRoles.Admin);
                if (error is not null) return error;
                var (campaign, transitionError) = await campaigns.TransitionAsync(campaignId, action);
                return transitionError ?? Results.Ok(campaign);
            });
        }

        group.MapPost("/{campaignId:long}/duplicate", async (HttpContext context, long campaignId,
            DuplicateCampaignRequest request) =>
        {
            var (admin, error) = await currentUsers.ResolveAsync(context, UserRoles.Admin);
            if (error is not null) return error;
            var (campaign, duplicateError) = await campaigns.DuplicateAsync(admin!.UserId, campaignId, request);
            return duplicateError ?? Results.Created($"/api/admin/campaigns/{campaign!.CampaignId}", campaign);
        });

        group.MapPost("/{campaignId:long}/issue", async (HttpContext context, long campaignId,
            IssueVouchersRequest request) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(context, UserRoles.Admin);
            if (error is not null) return error;
            var (result, issueError) = await campaigns.IssueAsync(campaignId, request);
            return issueError ?? Results.Ok(result);
        });

        group.MapGet("/{campaignId:long}/stats", async (HttpContext context, long campaignId) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(context, UserRoles.Admin);
            if (error is not null) return error;
            var stats = await campaigns.GetStatsAsync(campaignId);
            return stats is null ? Results.NotFound() : Results.Ok(stats);
        });

        group.MapGet("/{campaignId:long}/usages", async (HttpContext context, long campaignId,
            int? page, int? pageSize) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(context, UserRoles.Admin);
            if (error is not null) return error;
            var errors = new Dictionary<string, string[]>();
            var paging = ValidatePaging(page, pageSize, errors);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var usages = await campaigns.GetUsagesAsync(campaignId, paging.Page, paging.PageSize);
            return usages is null ? Results.NotFound() : Results.Ok(usages);
        });

        group.MapGet("/{campaignId:long}/recipients", async (HttpContext context, long campaignId,
            string? status, int? page, int? pageSize) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(context, UserRoles.Admin);
            if (error is not null) return error;
            var errors = new Dictionary<string, string[]>();
            if (!CampaignService.IsValidRecipientStatus(status))
                errors["status"] = ["Status must be available, used, expired or revoked."];
            var paging = ValidatePaging(page, pageSize, errors);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var recipients = await campaigns.GetRecipientsAsync(campaignId, status, paging.Page, paging.PageSize);
            return recipients is null ? Results.NotFound() : Results.Ok(recipients);
        });
    }

    private static (int Page, int PageSize) ValidatePaging(
        int? page, int? pageSize, Dictionary<string, string[]> errors)
    {
        var currentPage = page ?? 1;
        var currentPageSize = pageSize ?? 20;
        if (currentPage < 1 || currentPageSize < 1 || currentPageSize > 100 ||
            (long)(currentPage - 1) * currentPageSize > int.MaxValue)
        {
            errors["page"] = ["page must be at least 1 and pageSize must be between 1 and 100."];
        }

        return (currentPage, currentPageSize);
    }
}
