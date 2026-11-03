# Metrics

**Not implemented.** No metrics are exported anywhere in this template — no OpenTelemetry
Metrics, no `EventCounter`/`Meter` usage, no Prometheus endpoint. The only operational signal
today is the `/health` endpoint (liveness/readiness, not metrics — see below). Everything below is
RECOMMENDED.

## What exists today — health checks, not metrics

```csharp
// Program.cs
app.MapHealthChecks("health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
```

Backed by `AddHealthChecks().AddNpgSql(configuration.GetConnectionString("Database")!)`
(`Infrastructure/DependencyInjection.cs`) — a single binary "is the database reachable" check,
formatted via `AspNetCore.HealthChecks.UI.Client`. This is a health probe for
orchestrators/load balancers, not a metrics pipeline — don't conflate the two when asked for
"observability" in a new project; `/health` answers "is this instance up", metrics answer "how is
this system behaving over time".

## Adding metrics (RECOMMENDED)

Once `AddOpenTelemetry().WithMetrics(...)` is wired up (see
[observability/opentelemetry.md](./opentelemetry.md)):

- **Automatic instrumentation** (`AddAspNetCoreInstrumentation()`, `AddRuntimeInstrumentation()`)
  covers request duration/count, GC, thread pool, and similar runtime metrics with no handler-code
  changes.
- **Custom business metrics** — define a `Meter` alongside a new Infrastructure integration (e.g.
  if [messaging](../messaging/overview.md) is added: published-message count, consumer
  retry count, dead-letter count — see
  [messaging/reliability.md](../messaging/reliability.md#observability)):

```csharp
// RECOMMENDED — not present today
private static readonly Meter Meter = new("CleanArchitecture.Application");
private static readonly Counter<long> TodosCreated = Meter.CreateCounter<long>("todos.created");

// inside CreateTodoCommandHandler.Handle, after a successful create
TodosCreated.Add(1);
```

- Register the `Meter`'s name with `.AddMeter("CleanArchitecture.Application")` in the
  `WithMetrics(...)` configuration.

## What to add metrics for first, in a new project

Given this template's shape (CQRS handlers behind two decorators, one `DbContext`, one health
check today):

1. Command/query success vs. failure counts, per `ErrorType` — `LoggingDecorator` already sees
   this on every call (see [observability/logging.md](./logging.md#handler-level-logging--loggingdecorator));
   emitting a metric from the same place is a natural first addition if you go this route, rather
   than instrumenting each handler individually.
2. EF Core query duration (covered by `AddEntityFrameworkCoreInstrumentation()` automatically).
3. Anything added under [messaging](../messaging/overview.md), once that exists — publish/consume
   counts and retry/dead-letter counts are the minimum bar per
   [messaging/reliability.md](../messaging/reliability.md#observability).
