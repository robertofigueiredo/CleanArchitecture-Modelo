# Consumers

**Not implemented.** There are zero consumers in this template — no `IConsumer<T>`, no Wolverine
`Handle`/`HandleAsync` message handler, nowhere. The per-consumer breakdown a real audit would
produce (responsibility, dependencies, DB operations, external calls, retry, concurrency,
idempotency, observability, failure handling) is not applicable — there is nothing to describe.
Everything below is RECOMMENDED for when a consumer is added.

## Where consumer logic lives, and what it delegates to

One consumer class per message type, `internal sealed`, primary-constructor DI — matching the
existing `internal sealed class X(Dependency dep) : ICommandHandler<...>` shape used throughout
`Application` (see [architecture/application.md](../architecture/application.md)).

A consumer's job is orchestration only: deserialize (handled by the library) → dispatch to
existing Application logic → acknowledge. **Don't re-implement use-case logic inside the
message-handling layer** — if a `TodoItemCreated` consumer in another module needs to react by
creating a notification, that reaction is itself a normal Application command
(`ICommand`/`ICommandHandler`, following [patterns/cqrs.md](../patterns/cqrs.md)); the consumer's
role is just "translate the inbound message into a call to that command handler."

```csharp
// RECOMMENDED shape (Wolverine-style convention-based handler) — not a real type today
public static class TodoItemCreatedConsumer
{
    public static async Task HandleAsync(
        TodoItemCreated message,
        ICommandHandler<SendTodoCreatedNotificationCommand> handler,
        CancellationToken cancellationToken)
    {
        var command = new SendTodoCreatedNotificationCommand(message.TodoItemId, message.UserId);
        await handler.Handle(command, cancellationToken);
    }
}
```

If using MassTransit instead, the same delegation applies inside `IConsumer<T>.Consume(...)`.

## Persistence access

Route through `IApplicationDbContext`, the same seam every command handler already uses — never a
second raw `DbContext` type. See [persistence/ef-core.md](../persistence/ef-core.md).

## Idempotency (RECOMMENDED — REQUIRED once any consumer exists)

RabbitMQ/AMQP is at-least-once delivery — redelivery after a crash, a NACK, or a network partition
is normal operation, not an edge case. Every consumer that mutates state must be safe to process
the same message twice:

- Prefer natural idempotency: model the write as an upsert keyed on the message's business
  identifier, the same way `CreateTodoCommandHandler` already assigns a client-generated `Guid`
  before insert (see [persistence/ef-core.md](../persistence/ef-core.md#client-generated-keys)).
- Where natural idempotency isn't possible, track processed message IDs (the outbox/inbox pattern
  both Wolverine and MassTransit support natively is preferable to a bespoke table per consumer).
- No in-memory dedup state (static dictionaries/counters) — consumers can and will run with
  concurrency > 1 or across multiple instances; dedup state must live in the database or broker.

## Concurrency / prefetch (RECOMMENDED)

Set prefetch/concurrency per consumer deliberately, sized to the downstream dependency (DB
connection pool, external API rate limits) — don't leave client defaults unexamined. Don't assume
message ordering across a queue unless the topology guarantees single-consumer, single-threaded
processing for that queue.

## Failure handling / retry / dead-letter (RECOMMENDED)

See [messaging/reliability.md](./reliability.md) for the full retry/redelivery/dead-letter model
this template's messaging extension should follow — none of it exists today, and a consumer
should not ship without it.

## Observability (RECOMMENDED)

Log at consumer entry (message type + correlation id), completion, and every retry/dead-letter
transition, through the existing Serilog pipeline — the same `LoggingDecorator` pattern already
applied to every command/query handler (see
[observability/logging.md](../observability/logging.md)), not a second logging convention.
