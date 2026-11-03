# Presentation Layer — Web.Api

`src/Web.Api`. References `Application` and `SharedKernel` only — never `Domain` directly, never
EF Core, never `Infrastructure` concrete types.

Minimal APIs (`IEndpoint`) are the **sole** HTTP presentation pattern in this template.
Controllers are not part of the pattern — see [api/minimal-api.md](../api/minimal-api.md) for the
full contract, and [api/endpoints.md](../api/endpoints.md) for the endpoint-authoring guide.

## Structure

```
Web.Api/
├── Endpoints/
│   ├── IEndpoint.cs
│   ├── Tags.cs
│   ├── Todos/  — Create, Complete, Copy, Delete, Get, GetById
│   └── Users/  — GetById, Login, Permissions, Register
├── Extensions/
│   ├── EndpointExtensions.cs      — AddEndpoints/MapEndpoints/HasPermission
│   ├── ResultExtensions.cs        — Result.Match
│   ├── ApplicationBuilderExtensions.cs
│   ├── MiddlewareExtensions.cs
│   ├── MigrationExtensions.cs
│   └── ServiceCollectionExtensions.cs
├── Infrastructure/
│   ├── CustomResults.cs           — Error -> ProblemDetails
│   └── GlobalExceptionHandler.cs  — unhandled exception -> 500
├── Middleware/
│   └── RequestContextLoggingMiddleware.cs
├── DependencyInjection.cs         — AddPresentation()
└── Program.cs
```

## Composition (`Program.cs`)

```csharp
builder.Host.UseSerilog((context, loggerConfig) => loggerConfig.ReadFrom.Configuration(context.Configuration));
builder.Services.AddSwaggerGenWithAuth();
builder.Services.AddApplication().AddPresentation().AddInfrastructure(builder.Configuration);
builder.Services.AddEndpoints(Assembly.GetExecutingAssembly());

WebApplication app = builder.Build();
app.MapEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerWithUi();
    app.ApplyMigrations();
}

app.MapHealthChecks("health", ...);
app.UseRequestContextLogging();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers(); // REMARK in source: kept only as an escape hatch; no controller exists
await app.RunAsync();

namespace Web.Api { public partial class Program; } // required for WebApplicationFactory<Program>
```

There is no manual endpoint registry — any `IEndpoint` implementation anywhere in the assembly is
discovered by `AddEndpoints(Assembly.GetExecutingAssembly())` and mapped by `MapEndpoints()`.
`app.MapControllers()` is left in only as a documented escape hatch (see the `REMARK` comment in
`Program.cs`); introducing a controller is against this template's convention — see
[api/minimal-api.md](../api/minimal-api.md#forbidden).

## Request → Response translation

`Web.Api` is the only layer that turns a `Result`/`Result<T>` into an HTTP response, and it does
so through exactly two mechanisms:

- `ResultExtensions.Match` (`Web.Api/Extensions/ResultExtensions.cs`) — every endpoint calls
  `result.Match(<success mapper>, CustomResults.Problem)`.
- `CustomResults.Problem` (`Web.Api/Infrastructure/CustomResults.cs`) — maps `Error`/`ErrorType`
  to a `ProblemDetails` response with the correct status code.

Anything that escapes as an unhandled exception (a bug, not a business failure) is caught once,
centrally, by `GlobalExceptionHandler` (`Web.Api/Infrastructure/GlobalExceptionHandler.cs`),
registered via `app.UseExceptionHandler()`. Full mapping table in
[patterns/error-handling.md](../patterns/error-handling.md).

## Cross-cutting middleware

- `RequestContextLoggingMiddleware` (`Web.Api/Middleware/RequestContextLoggingMiddleware.cs`) —
  pushes a `CorrelationId` (from a `Correlation-Id` request header, falling back to
  `HttpContext.TraceIdentifier`) onto the Serilog `LogContext` for the duration of the request.
  See [observability/logging.md](../observability/logging.md).
- `app.UseSerilogRequestLogging()` — one structured log line per HTTP request (method, path,
  status code, elapsed time).
- Swagger/OpenAPI is enabled only in `Development` (`app.UseSwaggerWithUi()` inside
  `if (app.Environment.IsDevelopment())`). See [api/openapi.md](../api/openapi.md).
- Migrations are applied automatically only in `Development` (`app.ApplyMigrations()`), never in
  other environments. See [persistence/migrations.md](../persistence/migrations.md).

## What Web.Api must not do

- Reference `Domain` types, `ApplicationDbContext`, `DbSet<T>`, or any other EF Core/Infrastructure
  type directly — enforced by [dependency-rules.md](./dependency-rules.md).
- Contain business logic — an endpoint's job is: bind `Request` → build a Command/Query → call a
  handler → `Match` the `Result`.
- Introduce `ControllerBase`/`[ApiController]`/MVC routing.
- Hand-build `ProblemDetails` outside `CustomResults.Problem`, or do validation with inline `if`
  checks instead of a FluentValidation validator in `Application`.
