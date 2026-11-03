# RabbitMQ / MassTransit Guidelines

Audit of the current asynchronous-messaging implementation in this project. Source of truth:
`CLAUDE.md`, every `.csproj` under `src/` and `tests/`, `Directory.Packages.props`, and every
`.cs` file under `src/`. This document is descriptive/prescriptive only — **no code was changed
to produce it.**

---

## EXISTING — what's actually implemented today

**There is no messaging implementation in this codebase.** This is a factual finding, not an
oversight in the audit:

- `Directory.Packages.props` has no `MassTransit`, `MassTransit.RabbitMQ`, `RabbitMQ.Client`,
  `Wolverine`, or any other message-broker/bus package version pinned.
- No `.csproj` under `src/Domain`, `src/Application`, `src/Infrastructure`, `src/Web.Api`, or
  `src/SharedKernel` references any such package.
- A repository-wide search for `MassTransit`, `RabbitMQ`, `IConsumer`, `IBus`,
  `IPublishEndpoint`, and `Wolverine` across every `.cs` file returns zero matches. There are no
  consumers, no producers, no message contracts, no exchange/queue/routing configuration, no
  retry/redelivery policy, no error/dead-letter queue, no prefetch/concurrency tuning, and no
  saga.
- `docker-compose.yml` provisions only PostgreSQL and Seq — there is no RabbitMQ container, no
  management UI port, and no broker connection string anywhere in `appsettings*.json`.
- The only asynchronous "event" concept in the codebase is **in-process domain events**
  (`SharedKernel/IDomainEvent`, `Entity.Raise(...)`, `Infrastructure/DomainEventsDispatcher`),
  dispatched in-memory, synchronously, within the same process and the same
  `SaveChangesAsync` call that produced them (see `docs/engineering/EF_CORE_GUIDELINES.md` for
  the exact dispatch-after-commit mechanics). These are **not** a substitute for broker-based
  messaging — they don't cross process boundaries, aren't durable, and have no retry, DLQ, or
  outbox semantics. Nothing today publishes to or consumes from RabbitMQ, MassTransit, or any
  other broker.
- `README.md`'s "topics covered" list mentions "Outbox pattern" alongside Redis caching and
  OpenTelemetry as things the *companion article series* discusses — it is not implemented in
  this repository's code.

Because there is no existing implementation to describe per-consumer, the "for each consumer:
responsibility, dependencies, DB operations, external calls, retry, concurrency, idempotency,
observability, failure handling" breakdown requested alongside this document is **not
applicable** — there are zero consumers in the codebase today. Everything below is the standard
this project should adopt **when** messaging is introduced, not a description of current
behavior.

---

## REQUIRED

These rules are mandatory the moment any messaging code is added to this repository — a PR that
introduces a producer or consumer without satisfying these is not mergeable as-is.

1. **Choose one library and stay in that lane.** Per the project's `messaging` skill, **Wolverine
   is the recommended default** (MIT licensed; combines mediator + messaging with built-in
   outbox/saga support). **MassTransit is the named alternative in this document's title** and
   remains acceptable, but note MassTransit v9+ requires a commercial license for production use
   — that tradeoff must be an explicit, recorded decision (e.g. an ADR), not an implicit default.
   Do not mix both libraries in the same solution.
2. **Message contracts live in their own project/folder, are immutable records with primitive
   types, and carry no behavior.** Mirror the existing `Domain`/`Application` separation: a
   contract is data, not logic (see `## FORBIDDEN`).
3. **Every consumer must be idempotent.** RabbitMQ/AMQP is at-least-once delivery; redelivery
   after a crash, a NACK, or a network partition is a normal operating condition, not an edge
   case. A consumer that mutates state must be safe to run twice for the same message
   (`MessageId`/business key dedup, upsert semantics, or a processed-messages table) — see
   `## IDEMPOTENCY`.
4. **Every consumer that writes to `ApplicationDbContext` (or any DbContext) must use the
   transactional outbox**, so the DB write and the message consumption/production are atomic.
   This project already has exactly one write path per command handler
   (`docs/engineering/EF_CORE_GUIDELINES.md` — "one read + one mutation + one
   `SaveChangesAsync`"); messaging must not introduce a dual-write (DB commit succeeds, publish
   fails, or vice versa) anywhere in that path.
5. **No fire-and-forget publish.** `await` every publish/send call. If a handler must not block
   the HTTP response on the publish, that is a job for the outbox's async dispatch, not
   `Task.Run`/discarded tasks.
6. **Retry and redelivery policy must be explicit**, not left at library defaults silently
   inherited into production. Define: number of immediate retries, backoff strategy, and where a
   message goes after retries are exhausted (see `## RETRY`).
7. **A dead-letter/error queue must exist for every consumer queue** before that consumer ships.
   Messages that exhaust retries must land somewhere observable and re-processable, not vanish.
8. **Correlation must be preserved across the message boundary.** Propagate a correlation ID
   (from the inbound HTTP request, per `Web.Api`'s existing request pipeline, or generated at the
   publish site if none exists) onto every outgoing message so a failure can be traced from HTTP
   request → published event → consumer → DB write, consistent with this project's existing
   Serilog/Seq structured-logging setup.
9. **Respect the existing layer-dependency rules** (`tests/ArchitectureTests/Layers/LayerTests.cs`).
   Message contracts referenced by `Application` handlers must not pull in `Infrastructure` or
   broker-specific types (`RabbitMQ.Client`, `MassTransit.IBus`, etc.) into `Domain` or
   `Application` — broker wiring belongs in `Infrastructure` (or a new `Infrastructure`-tier
   messaging project), consistent with how EF Core is already isolated behind
   `IApplicationDbContext`.

---

## RECOMMENDED

1. **Prefer Wolverine's cascading-message style** (returning published events as tuple values
   from a handler) over manually calling `IMessageBus.PublishAsync` when publishing is
   unconditional — it keeps the handler a pure function and is easier to unit test, matching this
   project's existing preference for small, single-purpose CQRS handlers
   (`Application/Todos/*`, `Application/Users/*`).
2. **Model integration events the same way domain events are modeled today**: past-tense,
   immutable, one event per meaningful state transition (`TodoItemCreatedDomainEvent` is the
   existing in-process precedent — an integration event like `TodoItemCreated` for
   cross-service consumers should follow the same naming/shape convention, translated at the
   boundary rather than reusing the domain event type directly).
3. **Keep one consumer per message type**, mirroring the one-handler-per-use-case convention
   already used for `ICommandHandler`/`IQueryHandler` in `Application`.
4. **Reuse the existing `ILogger`/Serilog structured-logging pattern** (see the `LoggingDecorator`
   in `Application/DependencyInjection.cs`) inside consumers instead of introducing a second
   logging convention — log message type, correlation ID, and outcome (success/retry/dead-letter)
   at each consumer boundary.
5. **Version message contracts additively** (add optional/nullable fields, never repurpose or
   remove a field on a contract that has already shipped) so producer and consumer can deploy
   independently.
6. **Local development broker via `docker-compose.yml`**, alongside the existing Postgres/Seq
   services, with the management UI port exposed — keep parity with how Postgres/Seq are already
   wired for `docker compose up`.
7. **Prefer choreography (plain publish/consume) for 2–3 step flows; reserve a saga for workflows
   with compensating actions or multi-step state**, per the `messaging` skill's decision guide.
   Given this project's current single-aggregate `Todos`/`Users` scope, nothing today justifies a
   saga — don't introduce one speculatively.

---

## FORBIDDEN

1. **No business logic, computed properties, or methods on message contracts.** Contracts are
   pure data — no `CalculateX()`, no validation logic embedded in the record itself (validation
   belongs in the consumer or a FluentValidation validator, matching the existing
   `Application`-layer convention).
2. **No direct broker-type leakage into `Domain` or `Application`.** `RabbitMQ.Client`,
   `MassTransit.*`, or `Wolverine.*` types must not appear in `src/Domain` or as a hard dependency
   surface in `src/Application` beyond an abstraction seam — this mirrors the existing
   `IApplicationDbContext` seam that keeps EF Core out of `Application`.
3. **No publish without awaiting the result**, and no publish outside of a transaction/outbox
   boundary when the publish is a consequence of a DB write.
4. **No shared mutable state between concurrent consumer instances** (e.g. static
   dictionaries/caches used for dedup or counters) — idempotency and dedup state must live in the
   database or broker, not in process memory, since consumers can and will run with
   concurrency/prefetch > 1 or across multiple instances.
5. **No silent message loss.** A message must never be ack'd and then dropped on an unhandled
   exception with no dead-letter/error-queue landing spot.
6. **No manual `dotnet ef`-style out-of-band schema changes for outbox/inbox tables** — these
   ship as EF Core migrations through the same pipeline as every other schema change
   (`dotnet ef migrations add ... --project src/Infrastructure --startup-project src/Web.Api`,
   per `CLAUDE.md`), not hand-run SQL.

---

## MESSAGE CONTRACTS

- Define contracts as `public sealed record` (or `internal sealed record` if only consumed inside
  this solution) with primitive/simple types (`Guid`, `string`, `decimal`, `DateTimeOffset`) —
  no entity references, no `Domain` types leaking across the process boundary.
- Name events past-tense (`TodoItemCompleted`), commands imperative (`CompleteTodoItem`),
  matching the existing `TodoItemCompletedDomainEvent` / command-handler naming precedent in
  `Application`.
- Place contracts in a location reachable by both producer and consumer without violating layer
  rules — e.g. a dedicated `Contracts`/`IntegrationEvents` project referenced by `Infrastructure`
  on both sides, never by `Domain` or `Application` directly.
- Include a stable identifier for correlation (`CorrelationId` or reuse the message's own
  `MessageId` from the library) and a timestamp (`OccurredOnUtc`), consistent with
  `IDateTimeProvider`/UTC-everywhere convention already used for `CreatedAt`/`CompletedAt` in
  `Domain`.

## CONSUMERS

- One consumer class per message type, `internal sealed`, primary-constructor DI — matching the
  existing `internal sealed class X(Dependency dep) : ICommandHandler<...>` convention throughout
  `Application`.
- Consumers that write to the database go through the same `IApplicationDbContext` seam used by
  command handlers, not a separate raw `DbContext` — keep one persistence access pattern in the
  codebase.
- No business logic beyond orchestration in the consumer itself — delegate to Application
  commands/handlers where the logic already exists, rather than re-implementing use-case logic
  inside the message-handling layer. A consumer's job is: deserialize → dispatch to the existing
  `ICommandHandler`/domain logic → ack.
- Consumers must be idempotent (see `## IDEMPOTENCY`) and must not assume ordering unless the
  broker/queue topology explicitly guarantees it for that message type (see `## RECOMMENDED` on
  correlation and `## CONCURRENCY` below).

## PRODUCERS

- Publish from within the same transaction/outbox as the triggering DB write — no producer may
  call `PublishAsync`/`Publish` after `SaveChangesAsync` has already committed without outbox
  coverage.
- Producers are Application/Infrastructure code, never `Web.Api` endpoint code directly — an
  endpoint calls a command handler; the handler (or its cascading-message return) is what
  produces the event, matching the existing `Result.Match(Results.Ok, CustomResults.Problem)`
  boundary where `Web.Api` stays thin.

## RETRY

- Define an explicit immediate-retry count and backoff (e.g. exponential, capped) per consumer,
  not the library's silent default.
- After retries are exhausted, the message must move to that consumer's dead-letter/error queue
  — never be discarded.
- Transient infrastructure failures (DB connection blips, downstream HTTP timeouts) should retry;
  poison messages (deserialization failure, contract violation) should dead-letter immediately
  without burning the retry budget, since retrying a message that can never succeed just delays
  detection.

## IDEMPOTENCY

- Prefer natural idempotency first: model consumer-side DB writes as upserts keyed on the
  message's business identifier (e.g. `TodoItem.Id`) rather than blind inserts.
- Where natural idempotency isn't possible (e.g. "send an email" side effects), track processed
  message IDs in a dedicated table and check-before-act, consistent with how this project already
  favors explicit database state over implicit in-memory assumptions
  (`docs/engineering/EF_CORE_GUIDELINES.md`'s "no optimistic concurrency tokens exist" finding is
  a reminder that this codebase does not currently have any concurrency-safety net to lean on —
  messaging idempotency must be self-contained, not assume one).
- Wolverine's/MassTransit's built-in inbox (paired with the outbox) is the preferred mechanism
  once adopted — it deduplicates at the framework level instead of requiring bespoke
  processed-message tables per consumer.

## CONCURRENCY

- Set prefetch/concurrency limits per consumer deliberately, sized to the downstream dependency
  (DB connection pool, external API rate limits) — don't leave broker-client defaults
  unexamined.
- Because consumers may run with concurrency > 1, all idempotency/dedup guarantees must hold
  under concurrent delivery of the same message (see `## FORBIDDEN` — no in-memory shared
  state).
- Do not assume message ordering across a queue unless the topology guarantees single-consumer,
  single-threaded processing for that specific queue — the current codebase has zero ordering
  guarantees to preserve today, so this is a decision to make explicitly when the first
  order-sensitive consumer is introduced, not to assume by default.

## OUTBOX

- Required for every consumer/producer that touches `ApplicationDbContext` — see `## REQUIRED`
  item 4.
- Outbox/inbox tables are EF Core-managed (via the chosen library's
  `AddOutgoingWolverineMessageTable`/`AddEntityFrameworkOutbox<T>`-equivalent), migrated through
  the standard `dotnet ef migrations add` flow (`CLAUDE.md`), and included in
  `ApplyConfigurationsFromAssembly`/`OnModelCreating` alongside `TodoItemConfiguration` and
  `UserConfiguration`.
- The outbox dispatch loop must not silently swallow publish failures — failed dispatch attempts
  need visibility (logging + retry), matching the `## OBSERVABILITY` requirements below.

## OBSERVABILITY

- Log at consumer entry (message type + correlation ID), at successful completion, and at every
  retry/dead-letter transition, through the existing Serilog pipeline — no second logging
  framework.
- Correlation ID must flow from the originating HTTP request (if any) through to the consumer's
  log scope, so a support engineer can go from a Seq query on a request ID to every downstream
  message it triggered.
- Publish/consume counts, retry counts, and dead-letter counts should be exposed as metrics once
  the project's `configuring-opentelemetry-dotnet` setup (referenced in the project's skill list)
  is adopted for messaging — this repository does not currently have OpenTelemetry wired up
  anywhere (README lists it only as a topic covered by the companion article series), so this is
  a forward-looking recommendation, not a description of existing instrumentation.

## TESTING

- Contract shape: a plain unit test asserting the record's properties/serialization round-trip —
  no broker needed.
- Consumer logic: unit-test the consumer's handling logic directly (construct the message, call
  the consumer/handler, assert on the resulting DB state via an in-memory/test `DbContext`),
  matching how `Application` command handlers are presumably tested today against
  `IApplicationDbContext`.
- Idempotency: an explicit test that delivers the same message twice and asserts the second
  delivery is a no-op (or produces the same end state, not a duplicate side effect).
- End-to-end broker behavior (actual publish → actual consume, retry/dead-letter transitions):
  cover with an integration test against a real or test-container RabbitMQ instance — do not rely
  on unit tests alone to validate broker-level retry/DLQ configuration, since that configuration
  lives outside the C# code path unit tests exercise.
- Note: `tests/ArchitectureTests` currently only contains `LayerTests.cs`
  (`tests/ArchitectureTests/Layers/LayerTests.cs`) enforcing the five-project dependency
  direction. When messaging is introduced, extend that same architecture-test suite with a rule
  that broker-client types (`RabbitMQ.Client.*`, `MassTransit.*`/`Wolverine.*` beyond the
  Application-facing abstraction) don't leak into `Domain`, consistent with how EF Core is
  already excluded from `Domain`/`Application`.
