# RabbitMQ

**Not implemented.** No RabbitMQ container, connection string, or client package exists in this
template. Everything below is a **RECOMMENDED extension** describing how to introduce RabbitMQ
into a project based on this template, not current behavior.

## Local broker (RECOMMENDED)

Add a RabbitMQ service to `docker-compose.yml`, alongside the existing `postgres`/`seq` services,
with the management UI exposed for local development:

```yaml
rabbitmq:
  image: rabbitmq:4-management
  container_name: rabbitmq
  environment:
    - RABBITMQ_DEFAULT_USER=guest
    - RABBITMQ_DEFAULT_PASS=guest
  ports:
    - 5672:5672     # AMQP
    - 15672:15672   # management UI
```

## Exchanges, queues, routing (RECOMMENDED)

Neither MassTransit nor Wolverine requires you to hand-declare exchange/queue/binding topology in
most cases — both offer auto-provisioning conventions (MassTransit: default topology by message
type; Wolverine: `AutoProvision()`). Prefer the library's convention-based topology for a new
project rather than hand-authoring bindings, unless a specific routing requirement (fan-out to
multiple unrelated consumer groups, topic-based routing) forces custom exchange/binding
configuration.

- One queue per consumer (matching this template's "one handler per use case" convention already
  used for `ICommandHandler`/`IQueryHandler` — see [patterns/cqrs.md](../patterns/cqrs.md)).
- Name queues after the consumer, not the message (`todo-item-created.notify-owner`, not
  `todo-item-created`), so multiple consumers of the same event don't collide on the queue name.
- Every consumer queue needs a paired dead-letter/error queue before it ships — see
  [messaging/reliability.md](./reliability.md).

## Connection configuration (RECOMMENDED)

Follow the same configuration pattern already used for the database
(`ConnectionStrings:Database` in `appsettings.json`, read via `IConfiguration` in
`Infrastructure/DependencyInjection.cs` — see [persistence/database.md](../persistence/database.md)):
add a `RabbitMq` (or `ConnectionStrings:RabbitMq`) section, read it once in a new
`AddMessaging(this IServiceCollection, IConfiguration)` private extension chained onto
`AddInfrastructure`, matching the existing `AddServices`/`AddDatabase`/`AddHealthChecks`/
`AddAuthenticationInternal`/`AddAuthorizationInternal` composition style in
`Infrastructure/DependencyInjection.cs`.

## Health check (RECOMMENDED)

This template already wires a Postgres health check (`AddHealthChecks().AddNpgSql(...)`,
`Infrastructure/DependencyInjection.cs` → `AddHealthChecks`, exposed at `/health`). Add a matching
RabbitMQ health check (e.g. `AspNetCore.HealthChecks.Rabbitmq`) in the same
`AddHealthChecks` extension when the broker is introduced, so broker connectivity fails the same
`/health` probe as database connectivity.

## Choosing the client library

Per this template's `messaging` skill: **Wolverine (MIT) is the recommended default** — it can
target RabbitMQ directly without a separate mediator library, and it includes outbox/inbox and
saga support out of the box. **MassTransit is a viable alternative** but requires a commercial
license for production use from v9+; treat that choice as a recorded architectural decision (an
ADR), not a default. Do not install both in the same solution.
