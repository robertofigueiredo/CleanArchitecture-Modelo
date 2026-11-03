# Minimal API

Full audit with every deviation cataloged:
[`docs/engineering/MINIMAL_API_GUIDELINES.md`](../engineering/MINIMAL_API_GUIDELINES.md). This
page is the prescriptive summary.

**Minimal APIs are the only HTTP presentation pattern in this template. Controllers
(`ControllerBase`, `[ApiController]`, MVC action methods) are not used and must not be
introduced.** `app.MapControllers()` remains wired in `Program.cs` only as a documented escape
hatch (`// REMARK: If you want to use Controllers, you'll need this.`); no controller exists in
the codebase, and adding one is against the template's convention.

## `IEndpoint`

```csharp
// src/Web.Api/Endpoints/IEndpoint.cs
public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
```

One method: register one route (or a small family of related routes) on the builder it's given.

## Discovery — no manual registry

```csharp
// Program.cs
builder.Services.AddEndpoints(Assembly.GetExecutingAssembly());
...
app.MapEndpoints();
```

`AddEndpoints` (`Web.Api/Extensions/EndpointExtensions.cs`) reflection-scans the assembly for
every non-abstract type assignable to `IEndpoint` and registers it `Transient`. `MapEndpoints`
resolves all of them from DI and calls `MapEndpoint` on each. **Adding a new `IEndpoint` class
anywhere in `Web.Api` is sufficient** for it to be picked up and mapped at startup — no entry to
add anywhere else.

## Endpoint shape

One class per file, `Endpoints/<Aggregate>/<OperationName>.cs` — the class/file name is the
operation, not the HTTP verb (`Create`, `Complete`, `Copy`, `Delete`, `Get`, `GetById`, `Login`,
`Register`):

```csharp
internal sealed class Create : IEndpoint
{
    public sealed class Request { ... }   // only if the route needs a body

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("todos", async (
            Request request,
            ICommandHandler<CreateTodoCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new CreateTodoCommand { /* map Request fields */ };
            Result<Guid> result = await handler.Handle(command, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Todos)
        .RequireAuthorization();
    }
}
```

- Dependencies (`ICommandHandler<...>`, `IQueryHandler<...>`) are parameters of the route handler
  delegate itself, resolved per-request by the minimal API DI pipeline — not injected into the
  `IEndpoint` class via constructor. Every existing `IEndpoint` is stateless.
- `CancellationToken cancellationToken` is always the last delegate parameter, threaded to every
  awaited call — bound automatically from `HttpContext.RequestAborted`.

## Routes & verbs

- Lower-case route templates, no leading slash: `"todos"`, `"todos/{id:guid}"`, `"users/login"`.
- HTTP verb follows CQRS intent:

| Verb | Used for |
|---|---|
| `MapGet` | Queries (`IQueryHandler`) |
| `MapPost` | Commands that create, or don't map to PUT/DELETE semantics |
| `MapPut` | Commands that mutate/transition existing state |
| `MapDelete` | Commands that delete |
| `MapPatch` | not currently used |

## Request → Command/Query mapping

A nested `Request` type on the endpoint class, only when the route needs a body. Prefer `sealed
record Request(...)` for new endpoints (matches `Users/Login`/`Users/Register`) over a mutable
class with settable properties (`Todos/Create`'s style is legacy, not the model to copy). The
handler lambda maps `Request` + any route parameters into the Command/Query by hand — there is no
auto-mapping library in this template; don't add one for a single-field mapping.

## Response

- Success: `Results.Ok(value)` (queries, value-returning commands) or `Results.NoContent()`
  (commands with no return value).
- Failure: always `CustomResults.Problem` via `result.Match(...)` — see
  [patterns/error-handling.md](../patterns/error-handling.md).
- Never a `Domain`/EF entity directly — queries return dedicated `*Response` DTOs from
  `Application/<Aggregate>/<UseCase>/*Response.cs`; commands return primitive ids or nothing.

## REQUIRED

1. `internal sealed class : IEndpoint` under `src/Web.Api/Endpoints/<Aggregate>/`, one class per
   file, file name = class name = operation name.
2. Register the route only inside `MapEndpoint` — no route registration in `Program.cs`, a
   filter, or `EndpointExtensions`.
3. Lower-case route template, no leading slash, consistent with the aggregate's existing routes.
4. `CancellationToken cancellationToken` as the last delegate parameter, threaded through.
5. Depend only on `ICommandHandler<,>`/`ICommandHandler<>`/`IQueryHandler<,>` as delegate
   parameters — never a `DbContext`, repository, or `Domain` type directly.
6. Convert every `Result`/`Result<T>` via `result.Match(<success>, CustomResults.Problem)`.
7. `.WithTags(Tags.<Aggregate>)` — add a new constant to `Tags.cs` if needed.
8. Set the authorization posture explicitly: `.RequireAuthorization()`, `.HasPermission("area:action")`,
   or neither (intentionally public, login/register-style) — see
   [api/authorization.md](./authorization.md).
9. Put validation in a `FluentValidation.AbstractValidator<TCommand>` in `Application`, never in
   the endpoint.
10. Return dedicated `*Response` DTOs or primitives/nothing — never a `Domain`/EF entity.

## FORBIDDEN

- `Controller`/`ControllerBase`/`[ApiController]` classes or MVC action routing.
- Route registration outside an `IEndpoint.MapEndpoint` implementation.
- Throwing exceptions to signal expected/business failure.
- Hand-built `ProblemDetails`/`Results.Problem(...)` instead of `CustomResults.Problem`.
- Accessing `Result<T>.Value` without going through `Match`.
- Referencing `Domain`, EF Core, or any other Infrastructure type from `Web.Api`.
- Hand-written `if` validation in the endpoint delegate.
- Exposing `Domain`/EF entities as a response body.

Practical step-by-step for adding a new endpoint: [api/endpoints.md](./endpoints.md).
