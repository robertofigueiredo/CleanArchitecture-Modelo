# OpenTelemetry

**Not implemented.** No `OpenTelemetry.*` package reference exists in `Directory.Packages.props`
or any `.csproj`, no `AddOpenTelemetry()` call exists anywhere in `src/`, and no OTLP
exporter/collector is provisioned in `docker-compose.yml`. Observability in this template today is
Serilog + Seq only — see [observability/logging.md](./logging.md).

Everything below is a **RECOMMENDED extension**.

## Why you might add it

Serilog/Seq covers structured logs well but this template has no distributed tracing and no
metrics today. If a new project built on this template needs either (multi-service tracing, DB
call timing, external HTTP call instrumentation, custom business metrics), OpenTelemetry via the
.NET OpenTelemetry SDK is the standard way to add it — see this repository's
`configuring-opentelemetry-dotnet` skill for the general setup pattern; this page covers only how
it fits this template's specific structure.

## Where it would be wired up

`Infrastructure/DependencyInjection.cs`, as a new private extension method
(`AddObservability(this IServiceCollection, IConfiguration)`) chained onto `AddInfrastructure`,
matching the existing composition style (`AddServices`, `AddDatabase`, `AddHealthChecks`,
`AddAuthenticationInternal`, `AddAuthorizationInternal`):

```csharp
// RECOMMENDED — not present today
private static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
{
    services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("Web.Api"))
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter())
        .WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()
            .AddRuntimeInstrumentation()
            .AddOtlpExporter());

    return services;
}
```

`AddEntityFrameworkCoreInstrumentation()` traces EF Core queries against `ApplicationDbContext` —
useful given this template has no repository layer, so every DB call site is already visible in
`Application` handler code; tracing adds the timing/query-text visibility logging alone doesn't.

## Local collector (RECOMMENDED)

Add an OpenTelemetry Collector (or point `AddOtlpExporter()` directly at a local Jaeger/Prometheus
stack) to `docker-compose.yml`, alongside `postgres`/`seq`, the same way messaging's local broker
would be added (see [messaging/rabbitmq.md](../messaging/rabbitmq.md)).

## Relationship to existing correlation

`RequestContextLoggingMiddleware`'s `CorrelationId` (see
[observability/logging.md](./logging.md#correlation)) and OpenTelemetry's trace/span ids serve
overlapping but distinct purposes. If OpenTelemetry is adopted, prefer deriving Seq's
`CorrelationId` from the active `Activity.TraceId` rather than maintaining two independent
correlation mechanisms — see [observability/tracing.md](./tracing.md).
