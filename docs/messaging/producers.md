# Producers

**Not implemented.** No publish call exists anywhere in this template today. RECOMMENDED
placement below, for when messaging is added.

## Where publishing happens

From an Application command handler — the same place `SaveChangesAsync` already happens — never
from `Web.Api`. This mirrors exactly where domain events are raised today (see
[patterns/domain-events.md](../patterns/domain-events.md)): an endpoint calls a command handler;
the handler (or its cascading-message return, if using Wolverine) is what produces the event.

```csharp
// RECOMMENDED shape — extends the existing CreateTodoCommandHandler pattern, not a real type today
internal sealed class CreateTodoCommandHandler(
    IApplicationDbContext context,
    IDateTimeProvider dateTimeProvider,
    IUserContext userContext,
    IEventPublisher eventPublisher)     // new Application-owned abstraction, Infrastructure-implemented
    : ICommandHandler<CreateTodoCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateTodoCommand command, CancellationToken cancellationToken)
    {
        // ... existing validation/entity creation ...

        todoItem.Raise(new TodoItemCreatedDomainEvent(todoItem.Id)); // in-process, unchanged

        context.TodoItems.Add(todoItem);
        await context.SaveChangesAsync(cancellationToken);

        await eventPublisher.PublishAsync(
            new TodoItemCreated(todoItem.Id, todoItem.UserId, dateTimeProvider.UtcNow, correlationId),
            cancellationToken);

        return todoItem.Id;
    }
}
```

## Rules if you add this

1. **Always `await` the publish call.** No fire-and-forget (`Task.Run(...)`, discarded tasks) for
   anything that matters.
2. **Publish through the transactional outbox, not after a bare `SaveChangesAsync`.** Publishing
   right after `SaveChangesAsync` with no outbox reintroduces a dual-write problem: the DB commit
   can succeed while the publish fails (network blip, broker down), or vice versa if publish
   happens first. See [messaging/reliability.md](./reliability.md#outbox).
3. **`Application` depends on an abstraction it owns** (e.g. `IEventPublisher`), not a concrete
   `IBus`/`IPublishEndpoint` from MassTransit or Wolverine directly — same seam pattern as
   `IApplicationDbContext`. `Infrastructure` implements it.
4. **`Web.Api` never publishes.** An endpoint's only job is still: bind request → call handler →
   `Match` the `Result` — publishing is invisible to `Web.Api`, exactly like `SaveChangesAsync` is
   today.

## FORBIDDEN

- Publishing from an `IEndpoint`.
- Fire-and-forget publish.
- Publishing outside a transaction/outbox boundary when the publish is a direct consequence of a
  DB write in the same handler.
