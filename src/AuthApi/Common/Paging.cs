namespace AuthApi.Common;

internal static class Paging
{
    /// <summary>
    /// Validates page/pageSize query values. Returns a 400 result (key "page") when invalid,
    /// otherwise null and the effective values.
    /// </summary>
    public static IResult? TryGet(
        int? page, int? pageSize, out int currentPage, out int currentPageSize,
        int defaultSize = 20, int maxSize = 100)
    {
        currentPage = page ?? 1;
        currentPageSize = pageSize ?? defaultSize;
        if (currentPage < 1 || currentPageSize < 1 || currentPageSize > maxSize ||
            (long)(currentPage - 1) * currentPageSize > int.MaxValue)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["page"] = [$"page must be at least 1 and pageSize must be between 1 and {maxSize}."]
            });
        }

        return null;
    }
}

internal sealed record PageResponse<T>(T[] Items, int Page, int PageSize, long TotalCount);
