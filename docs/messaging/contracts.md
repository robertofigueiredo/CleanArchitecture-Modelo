# Message Contracts

**Not implemented.** No message contract type exists in this template. RECOMMENDED shape below,
for when messaging is added.

## Where contracts live

A dedicated project or folder reachable by both producer and consumer sides without violating this
template's dependency rules ([architecture/dependency-rules.md](../architecture/dependency-rules.md))
— e.g. a `Contracts`/`IntegrationEvents` project referenced by `Infrastructure` on both the
publishing and consuming side. **Never** reference contract types from `Domain`, and don't put
broker attributes/behavior on a contract that `Application` needs to see.

## Shape

```csharp
// RECOMMENDED — not an existing type
public sealed record TodoItemCreated(
    Guid TodoItemId,
    Guid UserId,
    DateTimeOffset OccurredOnUtc,
    Guid CorrelationId);
```

- `sealed record`, immutable, primitive/simple types only (`Guid`, `string`, `decimal`,
  `DateTimeOffset`) — no `Domain` entity references crossing the process boundary.
- Past-tense naming for events (`TodoItemCreated`), imperative for commands
  (`CompleteTodoItem`) — mirrors the existing in-process domain event naming
  (`TodoItemCreatedDomainEvent`, see [patterns/domain-events.md](../patterns/domain-events.md)),
  translated at the boundary rather than reusing the domain event type directly. A broker
  contract and an in-process `IDomainEvent` are different types even when they represent the same
  business fact — don't publish a `SharedKernel.IDomainEvent` directly to a broker.
- No behavior/computed properties on a contract — it is pure data. Validation of an inbound
  message belongs in the consumer, not the contract.
- Include a stable correlation identifier and an occurred-at timestamp on every contract, to
  support tracing an HTTP request through to the events it produced — see
  [messaging/reliability.md](./reliability.md#correlation) and
  [observability/logging.md](../observability/logging.md) for how this template already threads a
  `CorrelationId` through HTTP request logging via `RequestContextLoggingMiddleware`.

## Versioning (RECOMMENDED)

Version contracts additively — add optional/nullable fields, never repurpose or remove a field on
a contract that has already shipped — so producer and consumer can deploy independently, the same
tolerance this template already assumes for API compatibility across deployments (though no
formal API versioning is configured either — see [api/openapi.md](../api/openapi.md)).

## FORBIDDEN

- Business logic, computed properties, or methods on a contract type.
- Reusing a `Domain` entity or `SharedKernel.IDomainEvent` type directly as a wire contract.
- Breaking changes to a contract already consumed elsewhere (removing/retyping a field) without a
  new contract version.
