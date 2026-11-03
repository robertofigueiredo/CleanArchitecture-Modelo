using SharedKernel;

namespace Application.Abstractions.Pagination;

/// <summary>
/// Validated page/page-size pair for a paginated query. Application-layer concept only —
/// carries no HTTP/query-string knowledge; Web.Api is responsible for parsing the raw
/// "page"/"page-size" query parameters and calling <see cref="Create"/>.
/// </summary>
public sealed class PaginationRequest
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 1000;

    private PaginationRequest(int page, int pageSize)
    {
        Page = page;
        PageSize = pageSize;
    }

    public int Page { get; }

    public int PageSize { get; }

    public int Skip => (Page - 1) * PageSize;

    public static Result<PaginationRequest> Create(int? page, int? pageSize)
    {
        int resolvedPage = page ?? DefaultPage;
        int resolvedPageSize = pageSize ?? DefaultPageSize;

        if (resolvedPage < 1)
        {
            return Result.Failure<PaginationRequest>(PaginationErrors.InvalidPage(resolvedPage));
        }

        if (resolvedPageSize < 1)
        {
            return Result.Failure<PaginationRequest>(PaginationErrors.InvalidPageSize(resolvedPageSize));
        }

        if (resolvedPageSize > MaxPageSize)
        {
            return Result.Failure<PaginationRequest>(PaginationErrors.PageSizeTooLarge(resolvedPageSize));
        }

        return new PaginationRequest(resolvedPage, resolvedPageSize);
    }
}
