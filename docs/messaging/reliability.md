# Reliability — Retry, Redelivery, Dead-Letter, Outbox, Correlation

**Not implemented.** None of the mechanisms below exist in this template today — no retry policy,
no dead-letter queue, no outbox table, no broker-level correlation propagation. All RECOMMENDED,
for when messaging is added to a project based on this template.

## Retry

Define an explicit immediate-retry count and backoff (e.g. exponential, capped) per consumer —
don't leave it at a library's silent default. Distinguish:

- **Transient failures** (DB connection blip, downstream HTTP timeout) — retry.
- **Poison messages** (deserialization failure, contract violation) — dead-letter immediately,
  don't burn the retry budget on something that can never succeed.

## Redelivery / dead-letter

Every consumer queue needs a paired error/dead-letter queue before it ships. A message that
exhausts retries must land somewhere observable and re-processable — never silently dropped. No
consumer should be considered production-ready without this in place.

## Idempotency & concurrency

Covered in [messaging/consumers.md](./consumers.md#idempotency-recommended--required-once-any-consumer-exists)
— required the moment any consumer exists, since AMQP delivery is at-least-once.

## Outbox

Required for every consumer/producer that touches `ApplicationDbContext`, so the DB write and the
message publish/consume are atomic — otherwise you reintroduce exactly the dual-write problem this
template's own domain-event dispatch already accepts as a documented tradeoff for **in-process**
side effects (see [patterns/domain-events.md](../patterns/domain-events.md#architectural-decision-after-commit-dispatch-no-outbox)).
For cross-service messaging, that tradeoff is not acceptable — use the chosen library's outbox
integration:

- **Wolverine**: `AddDbContextWithWolverineIntegration<ApplicationDbContext>(...)` +
  `opts.Policies.AutoApplyTransactions()`, with
  `modelBuilder.AddIncomingWolverineMessageTable()` / `AddOutgoingWolverineMessageTable()` added to
  `ApplicationDbContext.OnModelCreating` alongside the existing
  `ApplyConfigurationsFromAssembly(...)` call.
- **MassTransit**: `x.AddEntityFrameworkOutbox<ApplicationDbContext>(o => { o.UsePostgres();
  o.UseBusOutbox(); })`.

Either way, the outbox/inbox tables are EF Core-managed and migrated through the standard
`dotnet ef migrations add` flow (see [persistence/migrations.md](../persistence/migrations.md)) —
not hand-run SQL.

## Correlation

Propagate a correlation id from the inbound HTTP request through to every message it produces.
This template already has an HTTP-side correlation mechanism to extend rather than duplicate:
`RequestContextLoggingMiddleware` (`Web.Api/Middleware/RequestContextLoggingMiddleware.cs`) reads
a `Correlation-Id` header (falling back to `HttpContext.TraceIdentifier`) and pushes it onto the
Serilog `LogContext` for the request — see
[observability/logging.md](../observability/logging.md). Thread that same id onto every published
message's `CorrelationId` field (see [messaging/contracts.md](./contracts.md#shape)) so a Seq
query on a request id can be traced through to every downstream message and consumer log line it
triggered.

## Observability

Publish/consume counts, retry counts, dead-letter counts should be exposed as metrics once
OpenTelemetry is adopted for the project (see
[observability/opentelemetry.md](../observability/opentelemetry.md) — not present in this template
today either). Until then, structured Serilog logging at each consumer's entry/success/retry/
dead-letter transition (see [messaging/consumers.md](./consumers.md#observability-recommended)) is
the minimum bar.

## Testing

See [testing/overview.md](../testing/overview.md#messaging-when-added) for the layered test
strategy (contract shape, consumer logic, idempotency, end-to-end broker behavior) to apply once
messaging exists.
