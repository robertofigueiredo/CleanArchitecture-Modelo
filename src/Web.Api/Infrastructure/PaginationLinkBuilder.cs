using System.Globalization;
using Application.Abstractions.Pagination;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Web.Api.Contracts;

namespace Web.Api.Infrastructure;

/// <summary>
/// Centralized construction of the <see cref="Links"/> for a paginated response, from the
/// current request's scheme/host/path/query string — never hardcoded. Respects reverse-proxy
/// forwarded headers because it reads <see cref="HttpRequest.Scheme"/>/<see cref="HttpRequest.Host"/>,
/// which the <c>ForwardedHeadersMiddleware</c> registered in Program.cs rewrites in place.
///
/// Presence rules (see docs/engineering/API_RESPONSE_GUIDELINES.md for the full rationale):
/// self is always present; first/prev are present when the current page is not the first page;
/// next/last are present when the current page is not the last page.
/// </summary>
internal static class PaginationLinkBuilder
{
    private const string PageParameterName = "page";
    private const string PageSizeParameterName = "page-size";

    internal static Links Build<TItem>(HttpRequest request, PagedResult<TItem> pagedResult)
    {
        string baseUrl = $"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}";

        Dictionary<string, StringValues> query = QueryHelpers.ParseQuery(request.QueryString.Value);

        string BuildUrl(int page)
        {
            var parameters = query
                .Where(kvp => kvp.Key is not (PageParameterName or PageSizeParameterName))
                .ToDictionary(kvp => kvp.Key, kvp => (string?)kvp.Value.ToString());

            parameters[PageParameterName] = page.ToString(CultureInfo.InvariantCulture);
            parameters[PageSizeParameterName] = pagedResult.PageSize.ToString(CultureInfo.InvariantCulture);

            return QueryHelpers.AddQueryString(baseUrl, parameters);
        }

        // first/prev mirror "not the first page" (Page > 1); next/last mirror "not the last
        // page" (Page < TotalPages) — matching PagedResult.HasPreviousPage/HasNextPage exactly,
        // so the presence rule is defined in exactly one place.
        return new Links
        {
            Self = BuildUrl(pagedResult.Page),
            First = pagedResult.HasPreviousPage ? BuildUrl(1) : null,
            Prev = pagedResult.HasPreviousPage ? BuildUrl(pagedResult.Page - 1) : null,
            Next = pagedResult.HasNextPage ? BuildUrl(pagedResult.Page + 1) : null,
            Last = pagedResult.HasNextPage ? BuildUrl(pagedResult.TotalPages) : null
        };
    }

    internal static string BuildSelfUrl(HttpRequest request) =>
        $"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}{request.QueryString}";
}
