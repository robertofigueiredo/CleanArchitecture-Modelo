using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Primitives;

namespace Web.Api.Infrastructure;

// Opt-in idempotency for a POST endpoint. When a request carries an
// Idempotency-Key header, the first request for that key is processed and the
// key is remembered; a repeat with the same key returns 409 Conflict. Requests
// without the header are processed normally. For full "replay the original
// response" idempotency you would additionally store and return the first result.
internal sealed class IdempotencyFilter(HybridCache cache) : IEndpointFilter
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromHours(1)
    };

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue(IdempotencyKeyHeader, out StringValues values) ||
            values.FirstOrDefault() is not { Length: > 0 } idempotencyKey)
        {
            // No idempotency key supplied - process the request normally.
            return await next(context);
        }

        bool isFirstRequest = false;

        await cache.GetOrCreateAsync(
            $"idempotency:{idempotencyKey}",
            _ =>
            {
                isFirstRequest = true;
                return ValueTask.FromResult(true);
            },
            CacheOptions);

        if (!isFirstRequest)
        {
            return Results.Problem(
                title: "Duplicate request",
                detail: "A request with this idempotency key has already been processed.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return await next(context);
    }
}
