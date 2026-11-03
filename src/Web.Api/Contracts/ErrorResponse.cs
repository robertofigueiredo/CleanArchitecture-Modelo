namespace Web.Api.Contracts;

/// <summary>
/// The response envelope for every failed response: <c>{ "errors": [ { code, title, detail } ] }</c>.
/// Always a collection, even for a single error, so validation failures (which carry many
/// <c>Error</c>s via <c>ValidationError</c>) and single business-rule failures share one shape.
/// Built exclusively by <c>Web.Api.Infrastructure.CustomResults.Problem</c> (business/expected
/// failures) or <c>Web.Api.Infrastructure.GlobalExceptionHandler</c> (unexpected exceptions) —
/// the single mechanism that translates a failed <c>Result</c>/unhandled exception to HTTP.
/// </summary>
public sealed class ErrorResponse
{
    public required IReadOnlyCollection<ApiError> Errors { get; init; }
}
