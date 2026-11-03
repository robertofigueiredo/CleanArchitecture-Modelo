# Minimal API Guidelines

Status: **Minimal APIs are the official, sole HTTP presentation pattern for this project.**
Controllers (`ControllerBase`, `[ApiController]`, MVC action methods) are **not** used and must
not be introduced. `app.MapControllers()` remains wired in `Program.cs` only as a template
remark/escape hatch; no controller currently exists in the codebase.

This document describes the pattern as it exists today in `src/Web.Api` (source of truth:
`CLAUDE.md` + the code under `src/Web.Api/Endpoints`), and sets the rules new endpoints must
follow. It documents; it does not change any code.

---

## EXISTING — the pattern as implemented today

### `IEndpoint`

`src/Web.Api/Endpoints/IEndpoint.cs`:

```csharp
public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
```

One method, one job: register one route (or a small family of related routes) on the builder
it's given.

### Discovery & registration — `EndpointExtensions`

`src/Web.Api/Extensions/EndpointExtensions.cs`:

- `AddEndpoints(this IServiceCollection, Assembly assembly)` reflection-scans the assembly for
  concrete, non-abstract, non-interface types assignable to `IEndpoint`, and registers each as a
  `Transient` `IEndpoint` via `TryAddEnumerable`. Called once from `Program.cs`:
  `builder.Services.AddEndpoints(Assembly.GetExecutingAssembly());`
- `MapEndpoints(this WebApplication, RouteGroupBuilder? routeGroupBuilder = null)` resolves
  `IEnumerable<IEndpoint>` from DI and calls `MapEndpoint` on each, against either the app itself
  or a supplied route group. Called once from `Program.cs`: `app.MapEndpoints();`
- `HasPermission(this RouteHandlerBuilder, string permission)` — thin wrapper over
  `RequireAuthorization(permission)`, used for permission-gated endpoints (see Authorization below).

**There is no manual endpoint registry to maintain.** Adding a new `IEndpoint` class anywhere in
`Web.Api` is sufficient for it to be picked up and mapped at startup.

### `MapEndpoint` — one class per endpoint, one file

Every endpoint is an `internal sealed class : IEndpoint` living under
`Endpoints/<Aggregate>/<Verb-ish-name>.cs`, e.g. `Endpoints/Todos/Create.cs`,
`Endpoints/Users/Login.cs`. The class name is the file name and describes the operation, not the
HTTP verb (`Create`, `Complete`, `Copy`, `Delete`, `Get`, `GetById`, `Login`, `Register`).

Body shape, consistently across every existing endpoint:

```csharp
internal sealed class Create : IEndpoint
{
    public sealed class Request { ... }          // only if the route needs a body/DTO

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("todos", async (
            Request request,
            ICommandHandler<CreateTodoCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new CreateTodoCommand { ... };          // map Request -> Command/Query

            Result<Guid> result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem); // map Result -> IResult
        })
        .WithTags(Tags.Todos)
        .RequireAuthorization();   // or .HasPermission(...) — see Authorization
    }
}
```

### Routes

- Route templates are **lower-case, no leading slash**, e.g. `"todos"`, `"todos/{id:guid}"`,
  `"todos/{todoId}/copy"`, `"users/login"`, `"users/register"`, `"users/{userId}"`.
- Route parameters that are `Guid` route the value with the `:guid` constraint where the
  parameter is the primary resource id (`{id:guid}`); parameters used to build a request body
  are sometimes left unconstrained (`{userId}` on `Users/GetById`, `{todoId}` on `Copy` — not
  fully consistent today, see FORBIDDEN/RECOMMENDED for the rule going forward).
- No versioning prefix or route groups are currently used; `MapEndpoints` accepts an optional
  `RouteGroupBuilder` for this purpose but nothing in the codebase currently supplies one.

### HTTP verbs

Mapped 1:1 to CQRS intent:

| Verb          | Used for                              | Examples |
|---------------|----------------------------------------|----------|
| `MapGet`      | Queries (`IQueryHandler`)              | `Todos/Get`, `Todos/GetById`, `Users/GetById` |
| `MapPost`     | Commands that create or don't map to PUT/DELETE semantics | `Todos/Create`, `Todos/Copy`, `Users/Login`, `Users/Register` |
| `MapPut`      | Commands that mutate/transition existing state | `Todos/Complete` |
| `MapDelete`   | Commands that delete                   | `Todos/Delete` |
| `MapPatch`    | *(not currently used)* | — |

### Request DTO

- A nested type on the endpoint class named `Request`, either a mutable `class` with
  `{ get; set; }` properties (`Todos/Create.Request`, `Todos/Copy.Request`) or an immutable
  `sealed record` (`Users/Login.Request`, `Users/Register.Request`). Both forms exist today;
  see RECOMMENDED for the direction to prefer.
- Only declared when the route needs a body. Route-parameter-only endpoints (`Complete`,
  `Delete`, `GetById`) take the id straight from a bound route parameter with no `Request` type.
- The handler lambda maps `Request` (+ any route parameters) into the Application-layer
  Command/Query by hand, field by field. There is no auto-mapping library.

### Response

- Success: `Results.Ok(value)` for queries and value-returning commands, `Results.NoContent()`
  for commands with no return value (`Complete`, `Delete`).
- Failure: always `CustomResults.Problem`, never a bespoke error result built inline in the
  endpoint.
- No endpoint returns a domain/EF entity directly — queries return dedicated `*Response` DTOs
  defined in `Application/<Aggregate>/<UseCase>/*Response.cs` (e.g. `TodoResponse`,
  `UserResponse`), commands return primitive ids (`Guid`) or nothing.

### `Result` / `Result<T>`

Defined in `src/SharedKernel/Result.cs`. Handlers never throw for expected/business failure —
they return `Result` (no value) or `Result<TValue>` (value on success). `Result<TValue>.Value`
throws `InvalidOperationException` if accessed on failure, so endpoints must go through
`Match`/`CustomResults.Problem`, never touch `.Value` directly on a possibly-failed result.

`ResultExtensions.Match` (`src/Web.Api/Extensions/ResultExtensions.cs`) is the only sanctioned
way an endpoint turns a `Result`/`Result<T>` into an `IResult`:

```csharp
result.Match(Results.Ok, CustomResults.Problem);
result.Match(Results.NoContent, CustomResults.Problem);
```

### `CustomResults.Problem`

`src/Web.Api/Infrastructure/CustomResults.cs` maps a failed `Result` to `Results.Problem(...)`:

- `ErrorType.Validation` / `ErrorType.Problem` / `ErrorType.NotFound` / `ErrorType.Conflict` →
  400 / 400 / 404 / 409 respectively, with `title`/`detail` taken from `Error.Code`/`Error.Description`.
- Anything else (`ErrorType.Failure` or unmapped) → 500, generic "Server failure" title/detail
  (the underlying error code/description is intentionally not leaked for unexpected errors).
- If the `Result`'s error is a `ValidationError` (multiple `Error`s), the individual errors are
  attached under an `errors` extension key on the `ProblemDetails` response.
- Throws `InvalidOperationException` if called with a successful `Result` — it is a programming
  error to call it outside the failure branch of `Match`.

### `GlobalExceptionHandler`

`src/Web.Api/Infrastructure/GlobalExceptionHandler.cs`, registered via `app.UseExceptionHandler()`
in `Program.cs`. Catches anything that escapes as an unhandled `Exception` (i.e. truly
exceptional/unexpected failures, not business failures — those never throw), logs it, and writes
a generic 500 `ProblemDetails` with no details from the exception. Endpoints and handlers should
not add their own try/catch translating exceptions to HTTP responses — that's this handler's job.

### Authorization

- Default: `.RequireAuthorization()` with no policy name — requires an authenticated JWT bearer
  principal only (used by all `Todos` endpoints).
- Permission-gated: `.HasPermission("permission:name")` (wraps `RequireAuthorization(permission)`)
  — used by `Users/GetById`. Permission strings are declared as `internal const string` on a
  per-area `Permissions` static class (`Web.Api/Endpoints/Users/Permissions.cs`), backed by
  `[HasPermissionAttribute]` / `PermissionAuthorizationHandler` / `PermissionProvider` in
  `Infrastructure/Authorization`. This is a custom claims/policy scheme, not ASP.NET Core role
  membership.
- Anonymous: endpoints with neither call are unauthenticated — currently `Users/Login` and
  `Users/Register` (there is no explicit `.AllowAnonymous()` call anywhere; omission is how
  anonymous access is expressed).

### Validation / FluentValidation

- Validation is **not** done in the endpoint (no `Request` validators, no manual `if` checks in
  the lambda). It happens one layer down, in Application, via `Application.Abstractions.Behaviors.ValidationDecorator`
  (`src/Application/Abstractions/Behaviors/ValidationDecorator.cs`), which wraps every
  `ICommandHandler`/`ICommandHandler<,>` registered through `AddApplication()`.
- Validators are `FluentValidation.AbstractValidator<TCommand>` classes co-located with the
  command, e.g. `Application/Todos/Create/CreateTodoCommandValidator.cs`, and are
  auto-registered by `services.AddValidatorsFromAssembly(...)` in `Application/DependencyInjection.cs`.
- Not every command has a validator (`Todos/Update`, `Users/Login` currently have none) — the
  decorator no-ops (`if (!validators.Any()) return [];`) when none are registered for a given
  command type.
- Validation failures short-circuit the inner handler and come back as `Result.Failure` with a
  `ValidationError` (`ErrorType.Validation`), which `CustomResults.Problem` turns into 400 with
  an `errors` collection — so from the endpoint's point of view, validation failure looks
  identical to any other business-rule failure: it flows through the same `Match`/`Problem` path.
- Queries currently have no validation layer (`ValidationDecorator` only wraps `ICommandHandler`
  types, not `IQueryHandler`).

### Swagger / OpenAPI

- `builder.Services.AddSwaggerGenWithAuth()` (`Web.Api/Extensions/ServiceCollectionExtensions.cs`)
  registers Swashbuckle with a JWT bearer `SecurityScheme`/`SecurityRequirement` applied
  globally, plus `CustomSchemaIds` to flatten nested type names (e.g. `Login+Request` →
  `Login-Request`) so nested `Request` types don't collide/break schema generation.
- `app.UseSwaggerWithUi()` (`Web.Api/Extensions/ApplicationBuilderExtensions.cs`) enables
  `UseSwagger()` + `UseSwaggerUI()`, called only `if (app.Environment.IsDevelopment())` in
  `Program.cs` — Swagger UI is not exposed outside Development today.
- No endpoint currently calls `.Produces<T>()`, `.ProducesProblem(...)`, `.WithName(...)`, or
  `.WithSummary(...)` — response typing in Swagger comes from the minimal API's inferred return
  type only. See RECOMMENDED.

### Tags

`Web.Api/Endpoints/Tags.cs` — a flat `static class` of `const string` values (`Tags.Todos`,
`Tags.Users`), applied via `.WithTags(Tags.X)` on every endpoint. One tag per aggregate; no
sub-tagging.

### Dependency injection

Endpoint handlers/services are **not** injected into the `IEndpoint` class via constructor —
`IEndpoint` implementations are stateless (registered `Transient`, no constructor dependencies
in any existing endpoint). Instead, dependencies (`ICommandHandler<...>`, `IQueryHandler<...>`)
are parameters of the route handler delegate itself and are resolved per-request by the minimal
API model-binding/DI pipeline (`[FromServices]`-equivalent inference — any parameter type
registered in DI and not otherwise bindable from route/query/body is treated as a service).

### `CancellationToken`

Every route handler delegate takes a trailing `CancellationToken cancellationToken` parameter
(bound automatically from `HttpContext.RequestAborted` by the minimal API pipeline) and threads
it through to `handler.Handle(command, cancellationToken)`. No endpoint constructs its own token
or omits it.

---

## REQUIRED

New endpoints **must**:

1. Be an `internal sealed class : IEndpoint` under `src/Web.Api/Endpoints/<Aggregate>/`, one
   class per file, file name = class name = operation name (not the HTTP verb).
2. Implement `MapEndpoint(IEndpointRouteBuilder app)` and register the route inside it — no
   route registration outside an `IEndpoint.MapEndpoint` method, no manual entries added to
   `EndpointExtensions` or `Program.cs`.
3. Use a lower-case route template with no leading slash, consistent with existing routes for
   the same aggregate (e.g. `"todos"`, `"todos/{id:guid}"`).
4. Take `CancellationToken cancellationToken` as the last delegate parameter and pass it through
   to every awaited call.
5. Depend only on `ICommandHandler<,>` / `ICommandHandler<>` / `IQueryHandler<,>` (never a
   `DbContext`, repository, or other Infrastructure/Domain type directly) as delegate
   parameters — Web.Api must not violate the `LayerTests` dependency rules documented in
   `CLAUDE.md`.
6. Convert every `Result`/`Result<T>` returned by a handler to an `IResult` via
   `result.Match(<success mapper>, CustomResults.Problem)`. Never inspect `.IsSuccess`/`.Value`
   manually in an endpoint and branch by hand.
7. Call `.WithTags(Tags.<Aggregate>)`, adding a new `const string` to `Tags.cs` if the aggregate
   doesn't have one yet.
8. Explicitly set the authorization posture: `.RequireAuthorization()` for any-authenticated-user
   endpoints, `.HasPermission("area:action")` for permission-gated endpoints (adding the
   permission constant to that area's `Permissions` class and to `PermissionProvider` in
   Infrastructure), or leave both off only when the endpoint is intentionally public
   (login/register-style). Do not invent a new authorization mechanism per endpoint.
9. Put request validation in a `FluentValidation.AbstractValidator<TCommand>` next to the
   command in Application, not in the endpoint delegate or the `Request` DTO.
10. Return dedicated `*Response` DTOs (queries) or primitive/`Guid`/nothing (commands) — never
    a Domain entity or EF entity.

## RECOMMENDED

- Prefer `sealed record Request(...)` over a mutable class with settable properties for new
  `Request` DTOs — matches `Users/Login`/`Users/Register` and is more consistent with the
  project's "prefer record types for immutable data" convention; the mutable-class form on
  `Todos/Create`/`Todos/Copy` is legacy, not the model to copy.
- Apply the `:guid` route constraint consistently to every `Guid` route parameter (`{id:guid}`,
  `{userId:guid}`, `{todoId:guid}`), including in cases like `Users/GetById` and `Todos/Copy`
  where it's currently omitted — catches malformed ids as a 400 route-mismatch before the
  handler runs, instead of a confusing downstream failure.
- Add a validator even for "obviously simple" commands once they gain more than one field, so
  behavior stays predictable rather than depending on whether someone remembered to write one.
- Consider `.Produces<TResponse>()` / `.ProducesProblem(...)` annotations on new endpoints for
  more accurate Swagger schemas, if/when the team decides to invest in richer OpenAPI output —
  not required to match current style, but doesn't conflict with it.
- When a resource genuinely needs multiple related routes under a shared prefix/policy, use the
  `RouteGroupBuilder` parameter already supported by `MapEndpoints(app, routeGroupBuilder)`
  rather than repeating the prefix/auth on every individual endpoint.

## FORBIDDEN

- Introducing `Controller`/`ControllerBase`/`[ApiController]` classes, or any MVC action-based
  routing. Minimal APIs via `IEndpoint` are the only presentation mechanism.
- Registering routes anywhere other than inside an `IEndpoint.MapEndpoint` implementation (e.g.
  directly in `Program.cs`, in a filter, in a startup extension).
- Throwing exceptions from an endpoint or handler to signal an expected/business failure. Use
  `Result.Failure(...)` and let `CustomResults.Problem` translate it. Exceptions are reserved
  for truly unexpected failures handled by `GlobalExceptionHandler`.
- Building `ProblemDetails`/`Results.Problem(...)` responses by hand inside an endpoint instead
  of going through `CustomResults.Problem`.
- Accessing `Result<T>.Value` without first checking `IsSuccess` (in practice: without going
  through `Match`).
- Referencing `Domain`, EF Core (`ApplicationDbContext`, `DbSet<T>`, etc.), or any other
  Infrastructure type from `Web.Api`/`Endpoints` — this would violate the `InfrastructureLayer`/
  `PresentationLayer` isolation asserted by `tests/ArchitectureTests/Layers/LayerTests.cs`.
- Doing input validation with hand-written `if` checks in the endpoint delegate instead of a
  FluentValidation validator in Application.
- Exposing Domain or EF entities directly as a response body.
- Mapping a new authorization policy/scheme outside the existing `HasPermissionAttribute` /
  `PermissionAuthorizationHandler` / `PermissionProvider` pipeline.

## EXAMPLES

**Query with route id and permission check** (pattern: `Users/GetById`):

```csharp
internal sealed class GetById : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("users/{userId:guid}", async (
            Guid userId,
            IQueryHandler<GetUserByIdQuery, UserResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetUserByIdQuery(userId);

            Result<UserResponse> result = await handler.Handle(query, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .HasPermission(Permissions.UsersAccess)
        .WithTags(Tags.Users);
    }
}
```

**Command with request body, record DTO, authenticated-only** (pattern: `Users/Register`,
recommended `Request` style):

```csharp
internal sealed class Register : IEndpoint
{
    public sealed record Request(string Email, string FirstName, string LastName, string Password);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("users/register", async (
            Request request,
            ICommandHandler<RegisterUserCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new RegisterUserCommand(
                request.Email, request.FirstName, request.LastName, request.Password);

            Result<Guid> result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Users);
    }
}
```

**Value-less command, `NoContent` on success** (pattern: `Todos/Complete`, `Todos/Delete`):

```csharp
internal sealed class Complete : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("todos/{id:guid}/complete", async (
            Guid id,
            ICommandHandler<CompleteTodoCommand> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new CompleteTodoCommand(id);

            Result result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Todos)
        .RequireAuthorization();
    }
}
```

## TESTING

The repository currently has **no** endpoint-level (functional/integration) tests — only
`tests/ArchitectureTests`, which asserts layer dependency rules and does not exercise HTTP
behavior. `Program.cs` already carries the
`namespace Web.Api { public partial class Program; }` shim specifically so a future test project
can target it with `WebApplicationFactory<Program>`; this doc records the intended approach for
when that project is added, it does not add it.

Recommended shape when endpoint tests are introduced (do not implement without a separate,
explicit task):

- A `FunctionalTests` (or `IntegrationTests`) project using `WebApplicationFactory<Program>`
  (from `Web.Api`), following the `LayerTests`/`BaseTest` convention of one `BaseTest`/fixture
  base class per test project.
- One test class per `IEndpoint` (mirroring `Endpoints/<Aggregate>/<Name>.cs` →
  `<Aggregate><Name>Tests.cs`), covering:
  - happy path → expected status code (`200`/`201`/`204`) and response shape (`*Response` DTO
    fields, not raw entity fields).
  - validation failure → `400` with a FluentValidation-shaped `errors` extension on the
    `ProblemDetails` body (exercises `ValidationDecorator` + `CustomResults.Problem` together).
  - not-found / conflict business failures → `404`/`409` with `title`/`detail` from the
    relevant `*Errors` static class.
  - authorization: anonymous request to a `.RequireAuthorization()`/`.HasPermission(...)`
    endpoint → `401`; authenticated-but-unpermitted request to a `.HasPermission(...)` endpoint
    → `403`.
- Prefer testing through the real DI/validation/logging decorator pipeline (real
  `ICommandHandler`/`IQueryHandler` registrations via `AddApplication()`), swapping only the
  database (e.g. Testcontainers Postgres or an EF Core in-memory/SQLite provider) and, if
  needed, the clock (`IDateTimeProvider`) — keep `ValidationDecorator`/`LoggingDecorator`/
  `CustomResults.Problem` in the loop rather than mocking `ICommandHandler` directly, since the
  whole point of this pattern is that those cross-cutting behaviors are exercised end-to-end.
- Architecture-level guardrails for Minimal APIs could be added to `ArchitectureTests` (e.g.
  "no type in Web.Api derives from `ControllerBase`", "every `IEndpoint` type is `internal
  sealed`") if/when controller drift becomes a real risk; not currently present.
