namespace Web.Api.Contracts;

/// <summary>
/// The response envelope for every successful response that has a body:
/// <c>{ "data": ..., "links": { "self": "..." }, "meta": ... }</c>. <see cref="Meta"/> is only
/// populated for paginated collections. Built exclusively via
/// <c>Web.Api.Infrastructure.ApiResponseFactory</c> — never constructed by hand in an endpoint.
/// </summary>
public sealed class SuccessResponse<TData>
{
    public required TData Data { get; init; }

    public required Links Links { get; init; }

    public Meta? Meta { get; init; }
}
