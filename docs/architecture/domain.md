# Domain Layer

`src/Domain`. References only `SharedKernel`. No EF Core, no ASP.NET Core, no FluentValidation.

## Structure

One folder per aggregate, currently `Todos/` and `Users/`:

```
Domain/
├── Todos/
│   ├── TodoItem.cs                     — entity
│   ├── Priority.cs                     — enum
│   ├── TodoItemErrors.cs               — static Error catalog
│   ├── TodoItemCreatedDomainEvent.cs
│   ├── TodoItemCompletedDomainEvent.cs
│   ├── TodoItemUpdatedDomainEvent.cs
│   └── TodoItemDeletedDomainEvent.cs
└── Users/
    ├── User.cs
    ├── UserErrors.cs
    └── UserRegisteredDomainEvent.cs
```

## Entities

Entities inherit `SharedKernel.Entity`, which provides only domain-event bookkeeping:

```csharp
// src/SharedKernel/Entity.cs
public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];
    public List<IDomainEvent> DomainEvents => [.. _domainEvents];
    public void ClearDomainEvents() => _domainEvents.Clear();
    public void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}
```

`TodoItem` and `User` (`src/Domain/Todos/TodoItem.cs`, `src/Domain/Users/User.cs`) are `sealed
class : Entity` with public `{ get; set; }` properties and **no behavior methods**:

```csharp
public sealed class TodoItem : Entity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Description { get; set; }
    public DateTime? DueDate { get; set; }
    public List<string> Labels { get; set; } = [];
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Priority Priority { get; set; }
}
```

**This is an explicit, existing project decision** — the shipped entities are anemic: state
transitions (e.g. "mark a todo complete") are implemented as inline property assignment in the
Application command handler, not as a method on the entity. This is not "rich domain model" DDD,
and this template does not present it as such. It works because Application handlers are the
single place mutation happens for each aggregate — there is only ever one write path per use case.

**Going forward, for any new invariant or state transition, prefer a behavior method on the
entity over inline property mutation in a handler.** `SharedKernel.Entity` places no constraint
against private setters, factory methods, or methods like `TodoItem.Complete(DateTime utcNow)` —
the existing anemic shape is what the sample feature happens to use, not a limitation of the base
class. If you introduce a new aggregate with real invariants (e.g. "cannot transition to X unless
currently in Y"), encapsulate that on the entity, not spread across the handler that happens to
call it first.

## Errors — `*Errors` static classes

Every aggregate has a static `<Aggregate>Errors` class holding `SharedKernel.Error` factories,
used instead of throwing exceptions for expected business failures:

```csharp
// src/Domain/Todos/TodoItemErrors.cs
public static class TodoItemErrors
{
    public static Error AlreadyCompleted(Guid todoItemId) => Error.Problem(
        "TodoItems.AlreadyCompleted",
        $"The todo item with Id = '{todoItemId}' is already completed.");

    public static Error NotFound(Guid todoItemId) => Error.NotFound(
        "TodoItems.NotFound",
        $"The to-do item with the Id = '{todoItemId}' was not found");
}
```

- Use a `static readonly Error` field when the message has no interpolated values
  (`UserErrors.NotFoundByEmail`, `UserErrors.EmailNotUnique`).
- Use a `static Error MethodName(...)` factory when the message needs to interpolate an argument
  (`TodoItemErrors.NotFound(Guid)`, `UserErrors.NotFound(Guid)`).
- Error codes follow `"<Aggregate>.<Reason>"` (`"TodoItems.NotFound"`, `"Users.EmailNotUnique"`).
- Pick the `Error` factory (`Error.NotFound`, `Error.Conflict`, `Error.Problem`,
  `Error.Failure`) that matches the intended HTTP status — see
  [patterns/error-handling.md](../patterns/error-handling.md) for the full mapping.

**REQUIRED**: every new aggregate gets its own `<Aggregate>Errors` static class in its own file,
following this exact naming and shape. Do not throw exceptions, and do not return raw strings, for
an expected business failure.

## Domain events

Events are `sealed record : IDomainEvent`, one per meaningful state transition, named past-tense:

```csharp
// src/Domain/Todos/TodoItemCreatedDomainEvent.cs
public sealed record TodoItemCreatedDomainEvent(Guid TodoItemId) : IDomainEvent;
```

An entity raises an event via the inherited `Raise(...)` method, from the Application handler that
performs the mutation:

```csharp
todoItem.Raise(new TodoItemCreatedDomainEvent(todoItem.Id));
```

Events are dispatched by `Infrastructure.Database.ApplicationDbContext.SaveChangesAsync` **after**
the underlying transaction commits. Full mechanics, including the explicit before/after
consistency tradeoff, are in [patterns/domain-events.md](../patterns/domain-events.md).

## When to return `Result` vs. throw

- **Expected business failure** (not found, already completed, conflicting email, unauthorized) →
  `Result.Failure(SomeErrors.X(...))` from the Application handler, built from a `Domain`
  `*Errors` factory. This is the only pattern used anywhere in this template's handlers.
- **Truly unexpected failure** (a bug, an infrastructure outage, a null that should be impossible)
  → let the exception propagate. It is caught centrally by `GlobalExceptionHandler`
  (`Web.Api/Infrastructure/GlobalExceptionHandler.cs`) and turned into a generic 500. `Domain` and
  `Application` code never wraps this in a try/catch to translate it to an HTTP status — that
  translation is `GlobalExceptionHandler`'s job alone.

`Domain` itself never throws for a business rule — there is no example anywhere in this codebase
of a `Domain` entity throwing a custom exception for an invariant violation. If a future rich
entity method needs to reject an invalid transition, prefer returning failure information the
caller can turn into a `Result` (or simply document that violating the precondition is a
programmer error, matching how the rest of the template treats bugs vs. business failures) rather
than introducing a new domain-specific exception type.

## What Domain must not do

- Reference `Application`, `Infrastructure`, or `Web.Api` — enforced by
  [dependency-rules.md](./dependency-rules.md).
- Reference EF Core, FluentValidation, ASP.NET Core, or any broker/messaging client.
- Contain orchestration (loading other aggregates, calling `SaveChangesAsync`, checking
  `IUserContext`) — that belongs in an Application handler.
