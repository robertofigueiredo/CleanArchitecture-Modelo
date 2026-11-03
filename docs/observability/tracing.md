# Tracing

**Not implemented.** No distributed tracing exists in this template — no `Activity` sources are
created or listened to anywhere in `src/`, and there is no OpenTelemetry SDK reference (see
[observability/opentelemetry.md](./opentelemetry.md)). Everything below is RECOMMENDED.

## What exists today instead

Request-scoped correlation only, via `RequestContextLoggingMiddleware` pushing a `CorrelationId`
(from a `Correlation-Id` header, or `HttpContext.TraceIdentifier`) onto Serilog's `LogContext` —
see [observability/logging.md](./logging.md#correlation). This gives you "which log lines belong
to the same HTTP request" in Seq; it does not give you span-level timing, parent/child call
relationships, or cross-service trace propagation.

## Adding tracing (RECOMMENDED)

Once `AddOpenTelemetry().WithTracing(...)` is wired up (see
[observability/opentelemetry.md](./opentelemetry.md)):

- **Automatic instrumentation** (`AddAspNetCoreInstrumentation()`,
  `AddEntityFrameworkCoreInstrumentation()`, `AddHttpClientInstrumentation()`) covers HTTP
  request/response spans, EF Core query spans, and outbound HTTP call spans without touching
  handler code.
- **Custom spans** for anything automatic instrumentation can't see (a specific business
  operation you want isolated in a trace view) — start them from within the relevant Application
  handler or Infrastructure integration, not from `Web.Api`, matching where logging already
  happens in `LoggingDecorator`:

```csharp
// RECOMMENDED — not present today
private static readonly ActivitySource ActivitySource = new("CleanArchitecture.Application");

internal sealed class CreateTodoCommandHandler(...) : ICommandHandler<CreateTodoCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateTodoCommand command, CancellationToken cancellationToken)
    {
        using Activity? activity = ActivitySource.StartActivity("CreateTodo");
        // ... existing handler body ...
    }
}
```

- Register the `ActivitySource`'s name with `.AddSource("CleanArchitecture.Application")` in the
  `WithTracing(...)` configuration, or it won't be exported.

## Correlating traces with logs

If tracing is adopted, prefer deriving `RequestContextLoggingMiddleware`'s `CorrelationId` from
`Activity.Current?.TraceId` rather than maintaining a separate ad-hoc correlation id — one
identifier flowing through both logs and traces is easier to query than two unrelated ones. This
is a change to `RequestContextLoggingMiddleware.GetCorrelationId` if you make it, not a change
required just to add tracing (both can coexist unmerged if you prefer to keep them independent).

## Cross-service tracing

If a project built on this template calls other services (HTTP, or a message broker per
[messaging/overview.md](../messaging/overview.md)), propagate the W3C `traceparent` header on
outbound HTTP calls (automatic with `AddHttpClientInstrumentation()` + a properly configured
`HttpClient`) and on published messages (add the trace context to the message contract's headers —
see [messaging/contracts.md](../messaging/contracts.md)), so a single trace spans the HTTP request
and everything it triggers downstream.
