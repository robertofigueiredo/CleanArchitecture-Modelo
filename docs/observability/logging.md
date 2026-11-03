# Logging

Serilog, configured from `appsettings.json`/`appsettings.Development.json`, writing to Console and
Seq. This is the only observability mechanism wired up in this template — there is no
OpenTelemetry (see [observability/opentelemetry.md](./opentelemetry.md)).

## Configuration

```csharp
// Program.cs
builder.Host.UseSerilog((context, loggerConfig) => loggerConfig.ReadFrom.Configuration(context.Configuration));
```

```json
// src/Web.Api/appsettings.Development.json
"Serilog": {
  "Using": [ "Serilog.Sinks.Console", "Serilog.Sinks.Seq" ],
  "MinimumLevel": { "Default": "Information", "Override": { "Microsoft": "Information" } },
  "WriteTo": [
    { "Name": "Console" },
    { "Name": "Seq", "Args": { "ServerUrl": "http://seq:5341" } }
  ],
  "Enrich": [ "FromLogContext", "WithMachineName", "WithThreadId" ]
}
```

Seq runs locally via `docker-compose.yml` (`seq` service, UI on `http://localhost:8081`, EULA
accepted via `ACCEPT_EULA=Y`). The API writes to Seq over the internal Docker network hostname
(`http://seq:5341`) when running via `docker compose up`.

## Request-level logging

`app.UseSerilogRequestLogging()` (`Program.cs`) emits one structured log line per HTTP request
(method, path, status code, elapsed time) — don't add a second manual "request completed" log
line; this is already covered.

## Correlation

`RequestContextLoggingMiddleware` (`Web.Api/Middleware/RequestContextLoggingMiddleware.cs`) reads
a `Correlation-Id` request header (falling back to `HttpContext.TraceIdentifier` if absent) and
pushes it onto Serilog's `LogContext` for the duration of the request:

```csharp
public class RequestContextLoggingMiddleware(RequestDelegate next)
{
    public Task Invoke(HttpContext context)
    {
        using (LogContext.PushProperty("CorrelationId", GetCorrelationId(context)))
        {
            return next.Invoke(context);
        }
    }
}
```

Every log line emitted during that request — including the ones from
`LoggingDecorator` below — carries `CorrelationId`, which is how a support engineer traces one
HTTP call through the whole handler pipeline in Seq.

**EXISTING — known gap**: `RequestContextLoggingMiddleware.Invoke` returns `next.Invoke(context)`
from inside the `using (LogContext.PushProperty(...))` block without `await`-ing it first. Because
the `using` block's `Dispose()` runs synchronously as soon as the (non-awaited) call returns
control, `CorrelationId` can be popped from `LogContext` before the downstream pipeline has
actually finished for any request with an async continuation after this middleware — which is
every request. If you touch this file, fix it to `await next.Invoke(context);` inside the `using`
block rather than returning the un-awaited task.

## Handler-level logging — `LoggingDecorator`

`Application/Abstractions/Behaviors/LoggingDecorator.cs` wraps every
`ICommandHandler`/`ICommandHandler<,>`/`IQueryHandler<,>` (see
[patterns/cqrs.md](../patterns/cqrs.md#decorators)):

```csharp
logger.LogInformation("Processing command {Command}", commandName);
Result<TResponse> result = await innerHandler.Handle(command, cancellationToken);
if (result.IsSuccess)
{
    logger.LogInformation("Completed command {Command}", commandName);
}
else
{
    using (LogContext.PushProperty("Error", result.Error, true))
    {
        logger.LogError("Completed command {Command} with error", commandName);
    }
}
```

This is applied uniformly to every handler via the decorator registration in
`Application/DependencyInjection.cs` — **don't add per-handler manual logging calls**
(`logger.LogInformation("Creating todo...")` inside a handler body) that duplicate what the
decorator already emits; if a handler genuinely needs a log line beyond
"processing/completed/completed with error", that's a signal the information belongs in a domain
event or a dedicated log statement for something the decorator can't know (an external call's
result, a computed value), not a restatement of the decorator's own lifecycle logging.

## Adding logging to a new external integration

When wiring a new Infrastructure integration (a new external HTTP call, a message broker
consumer/producer — see [messaging/overview.md](../messaging/overview.md)):

1. Inject `ILogger<T>` via primary constructor, matching `GlobalExceptionHandler`,
   `LoggingDecorator`, `PermissionAuthorizationHandler`, and every other logging Infrastructure
   type in this template.
2. Use structured message templates (`logger.LogInformation("Processing {X}", x)`), not string
   interpolation (`logger.LogInformation($"Processing {x}")`) — structured properties are what
   make Seq queries useful.
3. Log at the boundary of the call (start/success/failure), not every internal step.
4. Let `CorrelationId` propagate naturally via `LogContext` — don't manually pass a correlation id
   as a log parameter if the call happens inside the same async flow as an HTTP request; it's
   already on the context.

## REQUIRED

- Use `ILogger<T>` via DI, never `Console.WriteLine`/`Debug.WriteLine`.
- Structured message templates, not string interpolation, for every log call.
- Don't duplicate what `LoggingDecorator` or `UseSerilogRequestLogging()` already emits.

## FORBIDDEN

- A second logging framework or a second structured-logging convention alongside Serilog.
- Logging secrets (JWT `Jwt:Secret`, connection strings, password hashes) — none of the existing
  code does this; don't introduce it when adding new Infrastructure logging.
