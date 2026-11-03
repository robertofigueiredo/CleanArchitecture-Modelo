namespace Application.Abstractions.Pagination;

/// <summary>
/// A page of items plus the metadata needed to describe the page (current position, size,
/// total records, total pages). Pure data — no HTTP, no links. Web.Api turns this into the
/// response envelope's "data"/"meta"/"links" (see <c>Web.Api.Infrastructure.ApiResponseFactory</c>).
/// </summary>
public sealed class PagedResult<TItem>
{
    private PagedResult(IReadOnlyList<TItem> items, int page, int pageSize, int totalRecords)
    {
        Items = items;
        Page = page;
        PageSize = pageSize;
        TotalRecords = totalRecords;
        TotalPages = totalRecords == 0 ? 0 : (int)Math.Ceiling(totalRecords / (double)pageSize);
    }

    public IReadOnlyList<TItem> Items { get; }

    public int Page { get; }

    public int PageSize { get; }

    public int TotalRecords { get; }

    public int TotalPages { get; }

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < TotalPages;

    public static PagedResult<TItem> Create(IReadOnlyList<TItem> items, PaginationRequest pagination, int totalRecords) =>
        new(items, pagination.Page, pagination.PageSize, totalRecords);
}
