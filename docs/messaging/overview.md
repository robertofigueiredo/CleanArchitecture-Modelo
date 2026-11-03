# Messaging — Overview

**Not implemented.** This template ships with no message broker, no RabbitMQ, no MassTransit, no
Wolverine, and no producer/consumer code anywhere in `src/`. Verified: no package reference to
`MassTransit`, `MassTransit.RabbitMQ`, `RabbitMQ.Client`, or `Wolverine` exists in
`Directory.Packages.props` or any `.csproj`; no `IConsumer`, `IBus`, `IPublishEndpoint`, or
`Wolverine.*` type appears anywhere in `src/`; `docker-compose.yml` provisions only PostgreSQL and
Seq, no broker container.

The only asynchronous-eventing concept present is **in-process domain events**
(`Entity.Raise(...)` / `IDomainEventHandler<T>` / `DomainEventsDispatcher`) — see
[patterns/domain-events.md](../patterns/domain-events.md). These run in the same process, in
memory, dispatched after `SaveChangesAsync` commits, with no durability, no retry, and no
dead-letter handling. **They are not a substitute for broker-based messaging** and don't cross a
process/service boundary. If a new project needs cross-service communication, that's the
RabbitMQ/MassTransit or Wolverine work described below — not an extension of domain events.

## When you actually need messaging

Use this section to decide, don't add a broker speculatively:

| Need | Use |
|---|---|
| React to a state change within the same process/deployable | Domain events (already in the template) |
| Notify another service/module of something that happened, no reply needed | Broker event (RabbitMQ/MassTransit or Wolverine) — **RECOMMENDED extension** |
| Ask another service to do something and wait for the result | Direct HTTP/gRPC call, or a broker request/reply pattern if async is required |
| Background/long-running work triggered by an HTTP request | A hosted background service, or a broker command consumer if it must survive process restarts |

## RECOMMENDED extension — if you add messaging to a project based on this template

This template's own `messaging` skill (`.claude/skills/messaging/SKILL.md`) documents **Wolverine
as the recommended default** (MIT licensed, combines mediator + messaging, built-in
outbox/saga support) with **MassTransit as an alternative requiring a commercial license from
v9+**. Full guidance, including code samples for both, lives in that skill; the pages under
`docs/messaging/` in this documentation set restate the parts specific to fitting either library
into this template's layering and conventions:

- [messaging/rabbitmq.md](./rabbitmq.md) — broker topology (exchanges, queues, routing) and how it
  fits this template's layer boundaries.
- [messaging/contracts.md](./contracts.md) — where message contracts live and how they're shaped.
- [messaging/producers.md](./producers.md) — where publishing happens relative to Application
  handlers.
- [messaging/consumers.md](./consumers.md) — where consumer logic lives and how it reuses existing
  handler patterns.
- [messaging/reliability.md](./reliability.md) — retry, redelivery, dead-lettering, idempotency,
  outbox — all marked RECOMMENDED, none of it exists today.

Every page under `docs/messaging/` documents a **recommendation for extending this template**, not
current behavior. Nothing in `docs/messaging/` should be read as "this template does X" — read it
as "if you add messaging, do X".

## Fitting messaging into the existing layer rules

If/when messaging is added, it must respect the same boundaries as everything else in this
template (see [architecture/dependency-rules.md](../architecture/dependency-rules.md)):

- Broker client types (`RabbitMQ.Client.*`, `MassTransit.*`, `Wolverine.*`) belong in
  `Infrastructure` (or a new Infrastructure-tier project) — never in `Domain`, and never as a hard
  dependency in `Application`'s public surface.
- `Application` sees messaging the same way it sees persistence today: through an abstraction it
  owns (e.g. an `IEventPublisher`/`IMessageBus`-shaped interface under
  `Application/Abstractions/Messaging` or a new `Abstractions/EventBus` folder), implemented in
  `Infrastructure` — following the exact seam pattern `IApplicationDbContext` already
  establishes for EF Core (see [architecture/application.md](../architecture/application.md)).
- `Web.Api` never publishes directly — publishing happens from Application command handlers (or
  their cascading-message return, if using Wolverine), the same place `SaveChangesAsync` already
  happens.
