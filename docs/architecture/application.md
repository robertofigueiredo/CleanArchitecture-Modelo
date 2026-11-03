# Application Layer

`src/Application`. References `Domain` and `SharedKernel` only — no EF Core, no ASP.NET Core, no
broker client types anywhere in its public surface.

## Structure

```
Application/
├── Abstractions/
│   ├── Authentication/     — IPasswordHasher, ITokenProvider, IUserContext (implemented by Infrastructure)
│   ├── Behaviors/          — ValidationDecorator, LoggingDecorator
│   ├── Data/                — IApplicationDbContext (implemented by Infrastructure)
│   └── Messaging/           — ICommand, ICommand<T>, ICommandHandler<>/<,>, IQuery<T>, IQueryHandler<,>
├── DependencyInjection.cs  — AddApplication(): Scrutor scan + decoration + validator registration
├── Todos/
│   ├── Create/  Complete/  Copy/  Delete/  Get/  GetById/  Update/
└── Users/
    ├── GetByEmail/  GetById/  Login/  Register/
```

One folder per use case: `Application/<Aggregate>/<UseCase>/`, containing the
Command/Query, its Handler, an optional Validator, and (for queries) a `*Response` DTO.

## CQRS abstractions

See [patterns/cqrs.md](../patterns/cqrs.md) for the full contract, decorator pipeline, and worked
examples. In short: `ICommand`/`ICommand<TResponse>` + `ICommandHandler<>`/`ICommandHandler<,>`
for writes, `IQuery<TResponse>` + `IQueryHandler<,>` for reads. No MediatR — these are
hand-rolled interfaces, wired up by Scrutor assembly scanning in
`Application/DependencyInjection.cs`.

## Abstractions Application owns, Infrastructure implements

This is the seam that keeps `Application` free of EF Core / ASP.NET Core / JWT-library types:

| Interface | Purpose | Implemented in |
|---|---|---|
| `IApplicationDbContext` | `DbSet<User>`, `DbSet<TodoItem>`, `SaveChangesAsync` — the only way a handler touches persistence | `Infrastructure/Database/ApplicationDbContext.cs` |
| `IUserContext` | Current authenticated user's id, read from the JWT claims principal | `Infrastructure/Authentication/UserContext.cs` |
| `IPasswordHasher` | Hash/verify passwords | `Infrastructure/Authentication/PasswordHasher.cs` |
| `ITokenProvider` | Issue a JWT for a `User` | `Infrastructure/Authentication/TokenProvider.cs` |
| `IDateTimeProvider` | `UtcNow` (declared in `SharedKernel`, not `Application`) | `Infrastructure/Time/DateTimeProvider.cs` |

**REQUIRED**: any new external capability a handler needs (email sending, file storage, an HTTP
client to a third party) is declared as a new interface under `Application/Abstractions/<Area>/`
and implemented in `Infrastructure`. Never `new()` a concrete Infrastructure type, and never
reference a NuGet package that pulls in I/O, EF Core, or transport concerns directly from
`Application`.

## A handler, end to end

```csharp
// src/Application/Todos/Complete/CompleteTodoCommandHandler.cs
internal sealed class CompleteTodoCommandHandler(
    IApplicationDbContext context,
    IDateTimeProvider dateTimeProvider,
    IUserContext userContext)
    : ICommandHandler<CompleteTodoCommand>
{
    public async Task<Result> Handle(CompleteTodoCommand command, CancellationToken cancellationToken)
    {
        TodoItem? todoItem = await context.TodoItems
            .SingleOrDefaultAsync(t => t.Id == command.TodoItemId && t.UserId == userContext.UserId, cancellationToken);

        if (todoItem is null)
            return Result.Failure(TodoItemErrors.NotFound(command.TodoItemId));

        if (todoItem.IsCompleted)
            return Result.Failure(TodoItemErrors.AlreadyCompleted(command.TodoItemId));

        todoItem.IsCompleted = true;
        todoItem.CompletedAt = dateTimeProvider.UtcNow;
        todoItem.Raise(new TodoItemCompletedDomainEvent(todoItem.Id));

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
```

Shape to copy for every new command handler:

1. `internal sealed class`, primary-constructor DI.
2. Load the entity scoped to the current user in the same query (`&& t.UserId ==
   userContext.UserId`) whenever the aggregate is user-owned — see **REQUIRED** below.
3. Return `Result.Failure(...)` for every expected failure before mutating anything.
4. Mutate, `Raise(...)` any domain events, call `SaveChangesAsync` exactly once.
5. Return `Result.Success()` / the created id / nothing to map — no manual DTO shaping mixed into
   a command handler beyond returning an id.

## Decorator pipeline

`AddApplication()` wraps every command/query handler:

```
Endpoint → ValidationDecorator → LoggingDecorator → <your handler>
```

Validation runs first and short-circuits on failure (the inner handler and the logging decorator
around it never run); logging then wraps whatever came back — see
[patterns/cqrs.md](../patterns/cqrs.md#decorators) for the exact registration order and
[patterns/validation.md](../patterns/validation.md) for `ValidationDecorator` details.

## REQUIRED

1. New command handlers that load a user-owned entity **must** filter by `userContext.UserId` in
   the same query, matching `Complete`/`Delete`/`Create`/`Copy`. Skipping this is an IDOR
   vulnerability the moment the endpoint is exposed — this is a hard requirement, not a style
   preference.
2. `internal sealed` for every handler, validator, and command/query type unless something outside
   `Application` genuinely needs to reference it (nothing does today).
3. One `SaveChangesAsync` call per command handler. Do not span a single use case across multiple
   `SaveChangesAsync` calls or manual transactions — if a use case genuinely needs that, treat it
   as a signal the use case is doing too much, not a reason to add `BeginTransaction`.
4. New response DTOs: check sibling use-case folders for the same aggregate before adding a new
   `*Response` type — don't create a second copy of a shape that already exists.

## FORBIDDEN

- Referencing `Microsoft.EntityFrameworkCore.*` concrete types, ASP.NET Core hosting types, or a
  broker client (`RabbitMQ.Client`, `MassTransit`, `Wolverine`) directly — go through an
  `Abstractions/*` interface.
- Throwing for expected/business failure — return `Result`/`Result<T>`.
- Doing HTTP-shaping work (status codes, `ProblemDetails`) — that's `Web.Api`'s job.
