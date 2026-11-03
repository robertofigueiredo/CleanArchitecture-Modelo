# Infrastructure Layer

`src/Infrastructure`. References `Application` and `SharedKernel`. Owns every concrete
implementation of an `Application`-declared abstraction, plus EF Core, JWT auth, and
permission-based authorization.

## Structure

```
Infrastructure/
├── Authentication/     — TokenProvider, PasswordHasher, UserContext, ClaimsPrincipalExtensions
├── Authorization/       — HasPermissionAttribute, PermissionAuthorizationHandler,
│                          PermissionAuthorizationPolicyProvider, PermissionProvider, PermissionRequirement
├── Database/
│   ├── ApplicationDbContext.cs
│   ├── Schemas.cs
│   └── Migrations/
├── DomainEvents/        — IDomainEventsDispatcher, DomainEventsDispatcher
├── Time/                — DateTimeProvider
├── Todos/ Users/         — IEntityTypeConfiguration<T> per aggregate
└── DependencyInjection.cs — AddInfrastructure()
```

## Composition root — `AddInfrastructure`

`Infrastructure/DependencyInjection.cs` composes four focused private extensions rather than one
large method:

```csharp
public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration) =>
    services
        .AddServices()               // IDateTimeProvider, IDomainEventsDispatcher
        .AddDatabase(configuration)  // ApplicationDbContext, IApplicationDbContext, Npgsql, snake_case
        .AddHealthChecks(configuration)
        .AddAuthenticationInternal(configuration) // JWT bearer, IUserContext, IPasswordHasher, ITokenProvider
        .AddAuthorizationInternal();  // PermissionProvider, PermissionAuthorizationHandler, PermissionAuthorizationPolicyProvider
```

Follow this pattern — one private extension per concern — when a new project adds a new
Infrastructure concern (a cache, a message broker, a third-party HTTP client): a new
`AddXyz(this IServiceCollection)` private method chained onto `AddInfrastructure`, not one growing
method body.

## Persistence

`ApplicationDbContext : DbContext, IApplicationDbContext` is the only EF Core `DbContext` in the
template. Full detail — entity configurations, migrations, naming conventions, tracking,
transactions — is in [persistence/ef-core.md](../persistence/ef-core.md) and
[persistence/database.md](../persistence/database.md).

## Domain event dispatch

`DomainEventsDispatcher` (`Infrastructure/DomainEvents/DomainEventsDispatcher.cs`) receives the
events extracted by `ApplicationDbContext.SaveChangesAsync` after commit and reflectively invokes
every registered `IDomainEventHandler<T>` for each event's runtime type. Full mechanics in
[patterns/domain-events.md](../patterns/domain-events.md).

## Authentication

JWT bearer, configured in `AddAuthenticationInternal`:

- `TokenProvider.Create(User user)` issues a signed JWT (`sub` = user id, `email` claim) using
  `Jwt:Secret`/`Jwt:Issuer`/`Jwt:Audience`/`Jwt:ExpirationInMinutes` from configuration.
- `UserContext` (implements `IUserContext`) reads the current user's id from
  `IHttpContextAccessor.HttpContext.User` via `ClaimsPrincipalExtensions.GetUserId()`.
- `PasswordHasher` implements `IPasswordHasher` (hash/verify).

Full JWT + permission flow, including how to add a new permission, in
[api/authorization.md](../api/authorization.md).

## Authorization — permission-based, not role-based

A custom claims/policy scheme layered on ASP.NET Core policy auth:

- `[HasPermissionAttribute("permission:name")]` / `.HasPermission("permission:name")` — wraps
  `RequireAuthorization(permission)`.
- `PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>` — checks the
  requirement.
- `PermissionAuthorizationPolicyProvider : DefaultAuthorizationPolicyProvider` — resolves a
  permission string to a dynamically-built policy.
- `PermissionProvider.GetForUserIdAsync(Guid)` — where permissions would be loaded from storage.

**EXISTING — known template placeholder, not a bug to silently work around**:
`PermissionProvider.GetForUserIdAsync` always returns an empty `HashSet<string>`, and
`PermissionAuthorizationHandler` unconditionally calls `context.Succeed(requirement)` for any
authenticated user (both marked `// TODO` in the source). **No endpoint in this template is
actually permission-gated today** — `.HasPermission(...)` currently behaves identically to
`.RequireAuthorization()`. Before relying on permission checks in a real project, implement
`PermissionProvider.GetForUserIdAsync` against real storage and remove the unconditional
`context.Succeed(requirement)` short-circuit. See [api/authorization.md](../api/authorization.md).

## What Infrastructure must not do

- Reference `Web.Api` — enforced by [dependency-rules.md](./dependency-rules.md).
- Contain business rules — Infrastructure implements abstractions and does mechanical translation
  (EF Core query, JWT issuance, permission lookup); the decision logic lives in `Application`
  handlers and `Domain`.
- Be referenced by `Application` — the dependency only ever points `Infrastructure → Application`.
