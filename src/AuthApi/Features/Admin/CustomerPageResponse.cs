
namespace AuthApi.Features.Admin;
internal sealed record CustomerPageResponse(
    CustomerListItem[] Items, int Page, int PageSize, long TotalCount);
