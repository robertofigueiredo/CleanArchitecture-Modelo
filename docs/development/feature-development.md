# Feature Development — Step by Step

Worked example: adding a **"Snooze todo"** feature (postpone a todo's due date) to the existing
`Todos` aggregate. The same steps apply to a brand-new aggregate — steps 1–2 just have more to
create (a new `Domain/<Aggregate>/` folder, a new `DbSet<T>`, a new configuration) the first time.

## 1. Domain — entity change / new entity, and errors

If extending an existing aggregate, add the property/behavior to the entity
(`Domain/Todos/TodoItem.cs`) directly. If this were a new aggregate, create
`Domain/<Aggregate>/<Entity>.cs` (`sealed class : Entity`) first — see
[architecture/domain.md](../architecture/domain.md).

Add any new failure case to the aggregate's `*Errors` class:

```csharp
// Domain/Todos/TodoItemErrors.cs
public static Error CannotSnoozeCompleted(Guid todoItemId) => Error.Problem(
    "TodoItems.CannotSnoozeCompleted",
    $"The todo item with Id = '{todoItemId}' is already completed and cannot be snoozed.");
```

## 2. Persistence — only if the model shape changed

- New `DbSet<T>`: add to `Application/Abstractions/Data/IApplicationDbContext.cs` and
  `Infrastructure/Database/ApplicationDbContext.cs`.
- New/changed configuration: `Infrastructure/<Aggregate>/<Entity>Configuration.cs`.
- Not needed for "Snooze" — `DueDate` already exists on `TodoItem`.

See [persistence/ef-core.md](../persistence/ef-core.md#adding-a-new-entity--full-walkthrough).

## 3. Application — command, validator, handler

```
Application/Todos/Snooze/
├── SnoozeTodoCommand.cs
├── SnoozeTodoCommandHandler.cs
└── SnoozeTodoCommandValidator.cs
```

```csharp
// SnoozeTodoCommand.cs
public sealed record SnoozeTodoCommand(Guid TodoItemId, DateTime NewDueDate) : ICommand;

// SnoozeTodoCommandValidator.cs
internal sealed class SnoozeTodoCommandValidator : AbstractValidator<SnoozeTodoCommand>
{
    public SnoozeTodoCommandValidator()
    {
        RuleFor(c => c.TodoItemId).NotEmpty();
        RuleFor(c => c.NewDueDate).GreaterThan(DateTime.UtcNow);
    }
}

// SnoozeTodoCommandHandler.cs
internal sealed class SnoozeTodoCommandHandler(IApplicationDbContext context, IUserContext userContext)
    : ICommandHandler<SnoozeTodoCommand>
{
    public async Task<Result> Handle(SnoozeTodoCommand command, CancellationToken cancellationToken)
    {
        TodoItem? todoItem = await context.TodoItems
            .SingleOrDefaultAsync(t => t.Id == command.TodoItemId && t.UserId == userContext.UserId, cancellationToken);

        if (todoItem is null)
            return Result.Failure(TodoItemErrors.NotFound(command.TodoItemId));

        if (todoItem.IsCompleted)
            return Result.Failure(TodoItemErrors.CannotSnoozeCompleted(command.TodoItemId));

        todoItem.DueDate = command.NewDueDate;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
```

Note the `&& t.UserId == userContext.UserId` ownership filter in the same query — **required** for
every command that loads a user-owned entity, see
[architecture/application.md](../architecture/application.md#required). See
[patterns/cqrs.md](../patterns/cqrs.md) for the full contract and
[patterns/validation.md](../patterns/validation.md) for validator conventions.

## 4. Endpoint

```csharp
// Web.Api/Endpoints/Todos/Snooze.cs
internal sealed class Snooze : IEndpoint
{
    public sealed record Request(DateTime NewDueDate);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("todos/{id:guid}/snooze", async (
            Guid id,
            Request request,
            ICommandHandler<SnoozeTodoCommand> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new SnoozeTodoCommand(id, request.NewDueDate);
            Result result = await handler.Handle(command, cancellationToken);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Todos)
        .RequireAuthorization();
    }
}
```

No manual registration needed — `AddEndpoints`/`MapEndpoints` pick it up automatically. See
[api/endpoints.md](../api/endpoints.md).

## 5. Authorization

`.RequireAuthorization()` for "any authenticated user", or `.HasPermission("todos:snooze")` if
this should be permission-gated (add the constant to a `Web.Api/Endpoints/Todos/Permissions.cs` if
`Todos` doesn't have one yet — see [api/authorization.md](../api/authorization.md), including the
current permission-system-is-a-stub caveat).

## 6. Domain events (only if something should react to this)

```csharp
// Domain/Todos/TodoItemSnoozedDomainEvent.cs
public sealed record TodoItemSnoozedDomainEvent(Guid TodoItemId, DateTime NewDueDate) : IDomainEvent;
```

Raise it in the handler (`todoItem.Raise(new TodoItemSnoozedDomainEvent(...))`, before
`SaveChangesAsync`) and add an `IDomainEventHandler<TodoItemSnoozedDomainEvent>` implementation
anywhere in `Application` if something needs to react. See
[patterns/domain-events.md](../patterns/domain-events.md).

## 7. Migration (only if step 2 changed the model)

```bash
dotnet ef migrations add AddTodoSnoozeSupport --project src/Infrastructure --startup-project src/Web.Api
```

Not needed for "Snooze" (no schema change). See [persistence/migrations.md](../persistence/migrations.md).

## 8. Tests

- Handler unit test: not-found, already-completed, wrong-owner, happy-path — see
  [testing/unit-tests.md](../testing/unit-tests.md).
- Validator unit test — see
  [testing/unit-tests.md](../testing/unit-tests.md#testing-a-validator).
- Endpoint functional test, once that project exists — see
  [testing/integration-tests.md](../testing/integration-tests.md).
- If a new architecture rule is relevant (it usually isn't for a same-aggregate feature), see
  [testing/architecture-tests.md](../testing/architecture-tests.md).

## 9. Build & verify

```bash
dotnet build CleanArchitecture.slnx --configuration Release --no-restore
dotnet test CleanArchitecture.slnx --configuration Release --no-restore --no-build
```

`dotnet test` re-runs `LayerTests` — confirms the new code didn't introduce a forbidden
cross-layer reference (e.g. accidentally referencing `Domain` from `Web.Api`, or EF Core from
`Application`).

## Expected file set for this example

```
src/Domain/Todos/TodoItemSnoozedDomainEvent.cs        (new, optional)
src/Domain/Todos/TodoItemErrors.cs                     (edited — new error)
src/Application/Todos/Snooze/SnoozeTodoCommand.cs       (new)
src/Application/Todos/Snooze/SnoozeTodoCommandHandler.cs (new)
src/Application/Todos/Snooze/SnoozeTodoCommandValidator.cs (new)
src/Web.Api/Endpoints/Todos/Snooze.cs                   (new)
```

For a brand-new aggregate, add on top of this: `Domain/<Aggregate>/<Entity>.cs`,
`Domain/<Aggregate>/<Entity>Errors.cs`, `Infrastructure/<Aggregate>/<Entity>Configuration.cs`, the
`DbSet<T>` additions to `IApplicationDbContext`/`ApplicationDbContext`, a migration, and (the
first time) a `Web.Api/Endpoints/<Aggregate>/Permissions.cs` + a `Tags.<Aggregate>` constant.
