using Application.Abstractions.Pagination;
using Web.Api.Contracts;

namespace Web.Api.Infrastructure;

/// <summary>
/// The single place a successful <see cref="SharedKernel.Result{TValue}.Value"/> is wrapped into
/// the <c>{ data, links, meta }</c> response envelope. Endpoints call this from the success
/// branch of <c>result.Match(...)</c> instead of building <see cref="SuccessResponse{TData}"/> by
/// hand. See docs/engineering/API_RESPONSE_GUIDELINES.md.
/// </summary>
public static class ApiResponseFactory
{
    /// <summary>Non-paginated success response: <c>{ "data": ..., "links": { "self": "..." } }</c>.</summary>
    public static IResult Ok<TData>(TData data, HttpRequest request) =>
        Results.Ok(new SuccessResponse<TData>
        {
            Data = data,
            Links = new Links { Self = PaginationLinkBuilder.BuildSelfUrl(request) }
        });

    /// <summary>
    /// Paginated collection success response: <c>data</c> is the page's items, <c>meta</c> carries
    /// totalRecords/totalPages, and <c>links</c> includes first/prev/next/last per
    /// <see cref="PaginationLinkBuilder"/>.
    /// </summary>
    public static IResult Ok<TItem>(PagedResult<TItem> pagedResult, HttpRequest request) =>
        Results.Ok(new SuccessResponse<IReadOnlyList<TItem>>
        {
            Data = pagedResult.Items,
            Links = PaginationLinkBuilder.Build(request, pagedResult),
            Meta = new Meta
            {
                TotalRecords = pagedResult.TotalRecords,
                TotalPages = pagedResult.TotalPages
            }
        });
}
