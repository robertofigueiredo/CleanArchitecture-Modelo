namespace Web.Api.Contracts;

/// <summary>
/// HATEOAS-style navigation links, inspired by the Open Banking Brasil Fase 1 payload
/// convention. <see cref="Self"/> is always present; the rest are populated only for
/// paginated collection responses (see <c>ApiResponseFactory</c>).
/// </summary>
public sealed class Links
{
    public required string Self { get; init; }

    public string? First { get; init; }

    public string? Prev { get; init; }

    public string? Next { get; init; }

    public string? Last { get; init; }
}
