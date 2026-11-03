# Creating a New Endpoint

Practical, step-by-step. Assumes the Command/Query + Handler already exist (see
[development/feature-development.md](../development/feature-development.md) if not).

## 1. Pick the file location and name

`src/Web.Api/Endpoints/<Aggregate>/<OperationName>.cs` — e.g. a new "archive a todo" operation:
`src/Web.Api/Endpoints/Todos/Archive.cs`, class `internal sealed class Archive : IEndpoint`.

## 2. Route-parameter-only endpoint (no request body)

Pattern from `Todos/Complete`, `Todos/Delete`:

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

## 3. Endpoint with a request body (command returning a value)

Pattern from `Users/Register` (the recommended `Request` style — immutable record):

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

Note: this endpoint has **neither** `.RequireAuthorization()` nor `.HasPermission(...)` — that is
how anonymous access is expressed in this template (no explicit `.AllowAnonymous()` call exists
anywhere). Only omit both calls when the endpoint is intentionally public.

## 4. Query with a route id and permission check

Pattern from `Users/GetById`:

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

Apply the `:guid` route constraint on every new `Guid` route parameter (`{id:guid}`) — some
existing endpoints omit it; treat that as inconsistency to not repeat, not the pattern to copy.

## 5. Tag the new aggregate (first endpoint for it)

```csharp
// src/Web.Api/Endpoints/Tags.cs
internal static class Tags
{
    internal const string Todos = "Todos";
    internal const string Users = "Users";
    // internal const string YourAggregate = "YourAggregate";
}
```

## 6. Checklist before opening a PR

- [ ] `internal sealed class : IEndpoint`, correct file location/name.
- [ ] Route is lower-case, no leading slash, `:guid` on `Guid` route parameters.
- [ ] Verb matches CQRS intent (`MapGet` query, `MapPost`/`MapPut`/`MapDelete` command).
- [ ] Depends only on `ICommandHandler<,>`/`ICommandHandler<>`/`IQueryHandler<,>` + bound
      request/route data + `CancellationToken`.
- [ ] `result.Match(<success>, CustomResults.Problem)` — no manual branching.
- [ ] `.WithTags(Tags.<Aggregate>)`.
- [ ] Authorization posture set explicitly (`.RequireAuthorization()`, `.HasPermission(...)`, or
      deliberately neither).
- [ ] No `Domain`/EF Core types referenced.
- [ ] `dotnet build CleanArchitecture.slnx --configuration Release` passes (also re-verifies
      `LayerTests` implicitly is unaffected — run `dotnet test` to confirm).
