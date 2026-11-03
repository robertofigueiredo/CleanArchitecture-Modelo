# Dependency Injection

Three composition roots, one per layer that has one, each a static `AddX(this IServiceCollection)`
extension called from `Program.cs`:

```csharp
// Program.cs
builder.Services
    .AddApplication()      // Application/DependencyInjection.cs
    .AddPresentation()      // Web.Api/DependencyInjection.cs
    .AddInfrastructure(builder.Configuration); // Infrastructure/DependencyInjection.cs
```

`Domain` and `SharedKernel` register nothing — they contain no services, only types.

## `AddApplication()`

```csharp
// Application/DependencyInjection.cs
services.Scan(scan => scan.FromAssembliesOf(typeof(DependencyInjection))
    .AddClasses(classes => classes.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false)
        .AsImplementedInterfaces().WithScopedLifetime()
    .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<>)), publicOnly: false)
        .AsImplementedInterfaces().WithScopedLifetime()
    .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<,>)), publicOnly: false)
        .AsImplementedInterfaces().WithScopedLifetime());

services.Decorate(typeof(ICommandHandler<,>), typeof(ValidationDecorator.CommandHandler<,>));
services.Decorate(typeof(ICommandHandler<>), typeof(ValidationDecorator.CommandBaseHandler<>));
services.Decorate(typeof(IQueryHandler<,>), typeof(LoggingDecorator.QueryHandler<,>));
services.Decorate(typeof(ICommandHandler<,>), typeof(LoggingDecorator.CommandHandler<,>));
services.Decorate(typeof(ICommandHandler<>), typeof(LoggingDecorator.CommandBaseHandler<>));

services.Scan(scan => scan.FromAssembliesOf(typeof(DependencyInjection))
    .AddClasses(classes => classes.AssignableTo(typeof(IDomainEventHandler<>)), publicOnly: false)
    .AsImplementedInterfaces().WithScopedLifetime());

services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
```

Uses **Scrutor** (`services.Scan(...)`) to auto-register every `IQueryHandler<,>`,
`ICommandHandler<>`/`ICommandHandler<,>`, and `IDomainEventHandler<>` implementation in the
`Application` assembly — `Scoped` lifetime, `publicOnly: false` (so `internal sealed` handlers,
the default per [coding-standards.md](./coding-standards.md), are picked up). **You never manually
register a new handler** — implement the interface anywhere in `Application`, and it's found.

`services.Decorate(...)` (also Scrutor) wraps the registrations with `ValidationDecorator` then
`LoggingDecorator` — order matters, see [patterns/cqrs.md](../patterns/cqrs.md#decorators--registration-and-execution-order)
for the resulting runtime call order.

`AddValidatorsFromAssembly(..., includeInternalTypes: true)` (FluentValidation) registers every
`AbstractValidator<T>` in the assembly, `internal` included — same "just implement it, no manual
registration" story as handlers.

## `AddInfrastructure()`

```csharp
// Infrastructure/DependencyInjection.cs
services
    .AddServices()               // IDateTimeProvider (Singleton), IDomainEventsDispatcher (Transient)
    .AddDatabase(configuration)  // ApplicationDbContext (Scoped, via AddDbContext), IApplicationDbContext (Scoped)
    .AddHealthChecks(configuration)
    .AddAuthenticationInternal(configuration) // JWT bearer, IUserContext (Scoped), IPasswordHasher (Singleton), ITokenProvider (Singleton)
    .AddAuthorizationInternal();  // PermissionProvider (Scoped), PermissionAuthorizationHandler (Transient), PermissionAuthorizationPolicyProvider (Transient)
```

Composed as four/five focused private extension methods rather than one large method body — copy
this shape (`AddX(this IServiceCollection ...)` chained onto `AddInfrastructure`) when a new
Infrastructure concern is added (a message broker — see
[messaging/overview.md](../messaging/overview.md); OpenTelemetry — see
[observability/opentelemetry.md](../observability/opentelemetry.md)) rather than growing an
existing method or inlining registrations directly into `AddInfrastructure`.

## `AddPresentation()`

```csharp
// Web.Api/DependencyInjection.cs
services.AddEndpointsApiExplorer();
services.AddSwaggerGen();
services.AddControllers();  // REMARK: only needed if you want Controllers — kept as an escape hatch
services.AddExceptionHandler<GlobalExceptionHandler>();
services.AddProblemDetails();
```

Plus, from `Program.cs` directly (not inside `AddPresentation()`):
`builder.Services.AddSwaggerGenWithAuth()` (JWT-aware Swagger, see [api/openapi.md](../api/openapi.md))
and `builder.Services.AddEndpoints(Assembly.GetExecutingAssembly())` (endpoint discovery, see
[api/minimal-api.md](../api/minimal-api.md)).

## Choosing a lifetime for a new service

| Lifetime | Use for | Existing examples |
|---|---|---|
| `Scoped` | Anything touching the request/DB context, or depending on something scoped | every handler, `ApplicationDbContext`, `IApplicationDbContext`, `IUserContext`, `PermissionProvider` |
| `Singleton` | Stateless, thread-safe, no per-request dependency | `IDateTimeProvider`, `IPasswordHasher`, `ITokenProvider` |
| `Transient` | Cheap to construct, no shared state, needs a fresh instance every resolution | `IDomainEventsDispatcher`, `IAuthorizationHandler`/`IAuthorizationPolicyProvider` registrations, every `IEndpoint` |

Default to `Scoped` for anything that (directly or transitively) depends on
`IApplicationDbContext`/`ApplicationDbContext` — matching every handler and every Infrastructure
service that touches the database today. Don't register a new EF Core-dependent service as
`Singleton` — `DbContext` is not thread-safe across concurrent requests.

## Manual registrations vs. assembly scanning

Scrutor scanning covers `IQueryHandler`, `ICommandHandler`, `IDomainEventHandler`, FluentValidation
validators, and `IEndpoint` implementations — for these four categories, **implementing the
interface is the entire registration step**, don't also add a manual
`services.AddScoped<IFoo, Foo>()` line for them. Everything else (new `Abstractions/*`
interfaces and their Infrastructure implementations, new Infrastructure services) is registered
manually in the relevant `AddX` extension, following the existing one-line-per-service style in
`Infrastructure/DependencyInjection.cs`.
