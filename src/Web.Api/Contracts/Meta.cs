namespace Web.Api.Contracts;

/// <summary>
/// Pagination metadata. Only present on paginated collection responses — never added to a
/// single-resource or non-paginated collection response.
/// </summary>
public sealed class Meta
{
    public required int TotalRecords { get; init; }

    public required int TotalPages { get; init; }
}
