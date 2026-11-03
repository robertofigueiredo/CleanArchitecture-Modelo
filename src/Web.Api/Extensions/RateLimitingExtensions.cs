using System.Threading.RateLimiting;
using Web.Api.Infrastructure;

namespace Web.Api.Extensions;

internal static class RateLimitingExtensions
{
    internal static IServiceCollection AddRateLimitingInternal(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        int globalPermitLimit = configuration.GetValue<int?>("RateLimiting:Global:PermitLimit") ?? 100;
        int globalWindowSeconds = configuration.GetValue<int?>("RateLimiting:Global:WindowInSeconds") ?? 60;
        int authPermitLimit = configuration.GetValue<int?>("RateLimiting:Authentication:PermitLimit") ?? 10;
        int authWindowSeconds = configuration.GetValue<int?>("RateLimiting:Authentication:WindowInSeconds") ?? 60;

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // A global fixed-window limiter, partitioned by authenticated user or client IP.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = globalPermitLimit,
                        Window = TimeSpan.FromSeconds(globalWindowSeconds)
                    }));

            // A stricter policy for authentication endpoints to slow down brute-force attempts.
            options.AddPolicy(RateLimitingPolicies.Authentication, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = authPermitLimit,
                        Window = TimeSpan.FromSeconds(authWindowSeconds)
                    }));
        });

        return services;
    }

    private static string GetPartitionKey(HttpContext httpContext)
    {
        return httpContext.User.Identity?.Name
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";
    }
}
