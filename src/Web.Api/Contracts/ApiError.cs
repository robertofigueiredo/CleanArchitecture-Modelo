namespace Web.Api.Contracts;

/// <summary>
/// One error item. <see cref="Code"/> is the stable, machine-readable business/API error code
/// (e.g. <c>"TodoItems.NotFound"</c> — the same <c>Error.Code</c> already produced by
/// <c>Domain.*Errors</c> factories); it is deliberately independent of the HTTP status the
/// response was returned with. <see cref="Title"/> is a short, generic category label derived
/// from the error's <c>ErrorType</c>; <see cref="Detail"/> is the specific, human-readable
/// message from <c>Error.Description</c>.
/// </summary>
public sealed class ApiError
{
    public required string Code { get; init; }

    public required string Title { get; init; }

    public required string Detail { get; init; }
}
