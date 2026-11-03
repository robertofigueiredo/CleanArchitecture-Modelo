using SharedKernel;

namespace Application.Abstractions.Pagination;

public static class PaginationErrors
{
    public static Error InvalidPage(int page) => Error.Unprocessable(
        "Pagination.InvalidPage",
        $"The page = '{page}' is invalid. Page must be greater than or equal to 1.");

    public static Error InvalidPageSize(int pageSize) => Error.Unprocessable(
        "Pagination.InvalidPageSize",
        $"The page-size = '{pageSize}' is invalid. Page size must be greater than or equal to 1.");

    public static Error PageSizeTooLarge(int pageSize) => Error.Unprocessable(
        "Pagination.PageSizeTooLarge",
        $"The page-size = '{pageSize}' exceeds the maximum allowed value of {PaginationRequest.MaxPageSize}.");
}
