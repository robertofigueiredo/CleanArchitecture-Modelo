# Engineering Standards — Architecture & Design Review

A review of this project's architectural and design patterns as actually implemented — Clean
Architecture layering, CQRS, DDD, the `Result` pattern, domain events, DI, SOLID, encapsulation,
error handling, validation, logging, naming/accessibility conventions, and DTOs. Source of truth:
`CLAUDE.md` and the code itself (verified with `dotnet build`, not assumed). This document is
descriptive/prescriptive only — **no code was changed to produce it.**

Endpoint-specific conventions are covered in [`MINIMAL_API_GUIDELINES.md`](./MINIMAL_API_GUIDELINES.md)
and EF Core-specific conventions in [`EF_CORE_GUIDELINES.md`](./EF_CORE_GUIDELINES.md); this
document does not repeat their content except where an issue spans both a specific pattern and
the broader architecture.

Every finding below cites the actual file/method it comes from. Nothing here is generic advice
disconnected from this codebase.

---

## 1. Clean Architecture & dependency boundaries

**Correct today, verified, not just asserted:** `Domain` (`src/Domain`) references only
`SharedKernel`; `Application` references `Domain` + `SharedKernel` only (no EF Core types leak
into its public surface — `IApplicationDbContext` is the seam); `Infrastructure` and `Web.Api`
are the only projects referencing EF Core / ASP.NET Core hosting types.
`tests/ArchitectureTests/Layers/LayerTests.cs` encodes exactly these six one-way rules
(`Domain ↛ Application/Infrastructure/Presentation`, `Application ↛ Infrastructure/Presentation`,
`Infrastructure ↛ Presentation`) and `dotnet build` currently passes clean. No violation of the
dependency direction was found anywhere in the codebase.

One structural gap in the safety net itself: **`LayerTests` never asserts
`Infrastructure ↛ Application`**, nor does it check `Domain`/`Application` against `SharedKernel`
misuse (not needed — `SharedKernel` is meant to be shared). In practice `Infrastructure` *does*
correctly depend inward on `Application` (`Infrastructure/DependencyInjection.cs` implements
`Application`'s abstractions like `IUserContext`), so this isn't a live violation — just an
untested rule. Not urgent, but if `Infrastructure` ever grew a direct dependency back onto
something Application-internal, nothing here would catch it.

## 2. CQRS

**Correct and consistent:** every use case is either an `ICommand`/`ICommand<TResponse>` +
`ICommandHandler` or an `IQuery<TResponse>` + `IQueryHandler`, one folder per use case under
`Application/<Aggregate>/<UseCase>/`. No handler does both a "command that also returns rich
query data beyond an id" or a "query that mutates" — the separation is real, not just naming.
Commands return `Guid`/nothing; queries return `*Response` DTOs. This is hand-rolled (no
MediatR) via `Application/Abstractions/Messaging/*` — a deliberate, working choice, not a gap.

## 3. DDD — anemic domain model (the main real finding here)

`Domain.Todos.TodoItem` and `Domain.Users.User` (`src/Domain/Todos/TodoItem.cs`,
`src/Domain/Users/User.cs`) are plain data holders: every property is `{ get; set; }`, and
neither class has a single behavior method. All business logic that should arguably live on the
entity instead lives in Application command handlers, operating on the entity via direct property
assignment:

- `CompleteTodoCommandHandler.Handle` (`src/Application/Todos/Complete/CompleteTodoCommandHandler.cs:26-34`)
  checks `todoItem.IsCompleted` itself, then does `todoItem.IsCompleted = true;
  todoItem.CompletedAt = dateTimeProvider.UtcNow; todoItem.Raise(new
  TodoItemCompletedDomainEvent(todoItem.Id));` — three lines of what should be
  `todoItem.Complete(dateTimeProvider.UtcNow)` if `TodoItem` owned its own "already completed"
  invariant.
- Nothing stops any other code, anywhere in `Application` (or, since the setters are `public`,
  anywhere with a reference to the object), from writing `todoItem.IsCompleted = false` after
  completion, or `todoItem.CompletedAt = someDate` without ever setting `IsCompleted`, producing
  an inconsistent entity. The invariant "if `IsCompleted` then `CompletedAt` is set" exists only
  as an implicit convention across handlers, not as an enforced rule.
- `SharedKernel.Entity` (`src/SharedKernel/Entity.cs`) only provides domain-event bookkeeping
  (`Raise`/`ClearDomainEvents`) — it has no support for (and the two entities have no use of)
  private setters, factory methods, or invariant-enforcing behavior methods, which is the
  standard DDD entity shape this template's own README claims ("Domain layer with sample
  entities" under "Domain-Driven Design").

This is not a bug — nothing in the current use cases breaks because of it — but it is a genuine
**DDD inconsistency**: the project advertises DDD but the entities are anemic. Whether to fix
this is a real design decision (see RECOMMENDED), not a drive-by nitpick.

## 4. Result pattern

**Correct and consistent everywhere.** Every handler returns `Result`/`Result<T>`
(`src/SharedKernel/Result.cs`); no handler throws for an expected/business failure. Every
`*Errors` static class (`TodoItemErrors`, `UserErrors`) returns pre-built `Error` values with a
stable `Code`. The one place this could silently break — accessing `Result<T>.Value` on a failed
result — never happens outside `ResultExtensions.Match`, which is used by every endpoint. No
issues found.

One inconsistency worth naming: `UserErrors.NotFound(Guid)` and `UserErrors.Unauthorized()` are
methods (build a new `Error` each call), while `UserErrors.NotFoundByEmail` and
`UserErrors.EmailNotUnique` are `static readonly` fields (built once). `TodoItemErrors` mixes the
same way (`AlreadyCompleted`/`NotFound` are methods since they interpolate an id, which is the
correct reason for that split — but `UserErrors.NotFound(Guid)` also interpolates while
`NotFoundByEmail` doesn't need to, so the split is actually consistent once you check *why*, not
an issue — noted only so it isn't mistaken for one).

## 5. Domain events

**Correct core mechanism:** `Entity.Raise(...)` queues events; `ApplicationDbContext.SaveChangesAsync`
(`src/Infrastructure/Database/ApplicationDbContext.cs:26-44`) extracts and dispatches them after
`base.SaveChangesAsync` commits, with an explicit in-code comment about the before/after commit
tradeoff — this is a deliberate, documented design choice (eventual consistency, no outbox),
not an oversight.

Two concrete issues found in the dispatch mechanism itself (`src/Infrastructure/DomainEvents/DomainEventsDispatcher.cs`):

- `DispatchAsync` creates a **new `IServiceScope` per domain event** inside the `foreach`
  (line 18), not once per dispatch call — unnecessary allocation that scales with event count.
- `HandlerWrapper.Create` casts `Activator.CreateInstance(...)` to `HandlerWrapper` without a
  null-check (line 51) — low risk today (the type/constructor are fully controlled), but silent
  under the project's suppressed `CS8600` (see §11).

One concrete dead-code/duplication issue: `src/Application/Users/Register/UserRegisteredDomainEventHandler.cs`
declares **two** classes — `UserRegisteredDomainEventHandler` and
`UserRegisteredDomainEventHandler1` — both identical no-op `// TODO` stubs for the same event.
Both get picked up by the `IDomainEventHandler<>` Scrutor scan in
`Application/DependencyInjection.cs` and both run on every registration. This reads as an
accidental copy-paste left in the repo, not an intentional "multiple handlers" demonstration
(nothing distinguishes their purpose, and the name is a giveaway).

## 6. Dependency Injection

**Correct backbone:** `Application.DependencyInjection.AddApplication()` uses Scrutor to scan
and register every `IQueryHandler`/`ICommandHandler`/`IDomainEventHandler` implementation,
scoped, then layers `ValidationDecorator` and `LoggingDecorator` via `services.Decorate(...)` —
decoration order is deliberate (validate first, then log the validated-or-short-circuited
result). `Infrastructure.DependencyInjection.AddInfrastructure()` composes four focused private
extension methods (`AddServices`, `AddDatabase`, `AddHealthChecks`,
`AddAuthenticationInternal`/`AddAuthorizationInternal`) rather than one large method — good SRP
at the composition-root level.

**Primary constructor inconsistency** (the project's own `.cursorrules` states "Use primary
constructors for dependency injection in services, use cases, etc."), found in two places that
don't follow it despite being simple, single-dependency services exactly like the ones that do:

- `Infrastructure/Authentication/UserContext.cs` — traditional constructor + manually assigned
  `private readonly IHttpContextAccessor _httpContextAccessor` field, while every comparable
  single-dependency Infrastructure service (`TokenProvider`, `PermissionAuthorizationHandler`,
  every Application handler) uses a primary constructor.
- `Infrastructure/Authorization/PermissionAuthorizationPolicyProvider.cs` — traditional
  constructor calling `: base(options)`, assigning `_authorizationOptions = options.Value` in the
  body. C# 12 primary constructors support base-constructor arguments
  (`class Foo(IOptions<T> options) : Base(options)`), so this isn't a case where the language
  forced the older style.

## 7. SOLID

- **SRP**: holds up well — every class found is small and single-purpose (handlers do one use
  case, configurations configure one entity, decorators cross-cut one concern). No "god class"
  or "god method" was found anywhere in `src/`.
- **OCP**: the decorator pipeline (`ValidationDecorator`, `LoggingDecorator`) is a genuine
  open/closed extension point — new cross-cutting behavior can wrap `ICommandHandler`/
  `IQueryHandler` without touching existing handlers.
- **LSP**: not meaningfully exercised — there's no inheritance hierarchy of substitutable types
  in the domain (entities don't inherit from each other; the only inheritance is
  `PermissionAuthorizationPolicyProvider : DefaultAuthorizationPolicyProvider`, which correctly
  overrides `GetPolicyAsync` and falls back to `base.GetPolicyAsync` first).
- **ISP**: interfaces are small and focused (`IPasswordHasher` has 2 methods, `ITokenProvider`
  1, `IUserContext` 1 property) — no fat interfaces forcing unrelated implementations.
- **DIP**: correctly applied at the layer boundary — `Application` depends on
  `IApplicationDbContext`/`IUserContext`/`IPasswordHasher`/`ITokenProvider`/`IDateTimeProvider`
  abstractions it owns, and `Infrastructure` provides the implementations. No handler
  new()s a concrete Infrastructure type.

## 8. Encapsulation & abstractions

- `IApplicationDbContext` exposing raw `DbSet<T>` (not a repository/query-object abstraction) is
  a deliberate low-abstraction choice already covered in `EF_CORE_GUIDELINES.md` — consistent
  with the project's explicit "no Repository/UoW" stance, not an inconsistency.
- The entity-level encapsulation gap is §3 above (anemic model) — the one place encapsulation is
  genuinely weaker than the DDD label implies.
- `Entity.DomainEvents` (`src/SharedKernel/Entity.cs:7`) returns `[.. _domainEvents]` — a
  **defensive copy** on every access, which is correct encapsulation (callers can't mutate the
  internal list) but is a small, repeated allocation; `ExtractDomainEvents()` in
  `ApplicationDbContext` calls this getter once per tracked entity per `SaveChangesAsync`, so the
  cost is proportional to entities-with-events per request — negligible at this scale, noted
  only because "encapsulation" and "allocations" were both explicitly asked about together.

## 9. Error handling & validation

**Core pattern is correct and consistent**: FluentValidation validators wrapped by
`ValidationDecorator`, translated to a `ValidationError`/400 by `CustomResults.Problem` — see
`MINIMAL_API_GUIDELINES.md` for the full flow. Issues found beyond that flow:

- **Accessibility inconsistency across validators.** `DeleteTodoCommandValidator`,
  `CompleteTodoCommandValidator`, and `RegisterUserCommandValidator` are all `internal sealed
  class` (matching the project's "internal/sealed by default" convention). `CreateTodoCommandValidator`
  and `CopyTodoCommandValidator` are both plain `public class` — neither `internal` nor `sealed`.
  Nothing requires these two validators to be public (they're only ever resolved through DI as
  `IValidator<T>`), so this is drift, not a deliberate API decision.
- **Missing validators on two commands that plausibly need one:**
  `UpdateTodoCommand` (`Application/Todos/Update/UpdateTodoCommandHandler.cs`) and
  `LoginUserCommand` (`Application/Users/Login/LoginUserCommandHandler.cs`) have no
  `AbstractValidator<T>` at all, unlike every sibling command in the same folders. For `Login`,
  the absence is defensible (empty email/password will simply fail the lookup/hash check and
  return a uniform `NotFoundByEmail`), but `UpdateTodoCommand.Description` has no `NotEmpty()`/
  `MaximumLength` guard the way `CreateTodoCommand.Description` does — an empty or arbitrarily
  long description can be persisted via `Update` where it couldn't via `Create`.
- **`UpdateTodoCommandHandler` is unreachable dead code with a latent ownership-check bug.**
  `src/Web.Api/Endpoints/Todos/` has no `Update.cs` — grepping the whole repo confirms
  `UpdateTodoCommand` is referenced only by its own command file and its own handler, nowhere
  else. So today this is inert. But note for whenever it's wired up:
  `UpdateTodoCommandHandler.Handle` (`src/Application/Todos/Update/UpdateTodoCommandHandler.cs:15-16`)
  loads the entity with `SingleOrDefaultAsync(t => t.Id == command.TodoItemId, cancellationToken)`
  — **no `UserId` filter** — whereas `CompleteTodoCommandHandler` and `DeleteTodoCommandHandler`
  both filter with `t.Id == command.TodoItemId && t.UserId == userContext.UserId`. If an
  `Update` endpoint is added by copying the existing endpoint pattern without also copying that
  ownership check, any authenticated user could update any other user's todo by id (IDOR). Flag
  this the moment someone goes to wire up the missing endpoint.
- **Inconsistent enumeration-safety posture between two very similar handlers.**
  `LoginUserCommandHandler` deliberately returns the *same* `UserErrors.NotFoundByEmail` whether
  the email doesn't exist or the password is wrong (`Login/LoginUserCommandHandler.cs:21-30`) —
  correct practice, avoids leaking which emails are registered. `GetUserByEmailQueryHandler`
  (`Application/Users/GetByEmail/GetUserByEmailQueryHandler.cs`) does the opposite: it returns
  `UserErrors.NotFoundByEmail` if the email doesn't exist, but a *different* error,
  `UserErrors.Unauthorized()`, if the email exists but belongs to someone else
  (`user.Id != userContext.UserId`). That's a behavioral inconsistency between the two handlers
  that both branch on "does this email exist" — `GetByEmail` lets a caller distinguish
  "unknown email" from "exists, but not yours" via the response body, which `Login` was
  specifically written to avoid.

## 10. Logging

`LoggingDecorator` (`Application/Abstractions/Behaviors/LoggingDecorator.cs`) is applied
uniformly to every `ICommandHandler`/`ICommandHandler<,>`/`IQueryHandler<,>` via `services.Decorate`,
logs a consistent `Processing {X}`/`Completed {X}`/`Completed {X} with error` pattern with the
`Error` pushed into the Serilog `LogContext` on failure — consistent, no per-handler ad-hoc
logging exists to duplicate or contradict it. The one logging-adjacent defect
(`RequestContextLoggingMiddleware.Invoke` not awaiting inside its `using` block, so
`CorrelationId` can be popped from `LogContext` before the downstream pipeline actually
finishes) is a correctness bug already filed in the async/C# audit — cross-referenced here
because it undermines exactly this decorator's structured-logging guarantees for requests that
have any async continuation after the middleware.

## 11. Naming & accessibility conventions

`.cursorrules` states: "Make types internal and sealed by default unless otherwise specified."
Cataloguing every deviation found (beyond the validators in §9):

| Type | File | Deviation |
|---|---|---|
| `CreateTodoCommandValidator` | `Application/Todos/Create/CreateTodoCommandValidator.cs` | `public`, not `internal`/`sealed` |
| `CopyTodoCommandValidator` | `Application/Todos/Copy/CopyTodoCommandValidator.cs` | `public`, not `internal`/`sealed` |
| `RequestContextLoggingMiddleware` | `Web.Api/Middleware/RequestContextLoggingMiddleware.cs` | `public`, not `internal`/`sealed` (no other type references it outside `Web.Api`, so it doesn't need to be public) |

Everything else audited (handlers, decorators, endpoints, entity configurations,
`DomainEventsDispatcher`, authorization types) correctly follows `internal sealed` — these three
are the exceptions, not the rule, which is exactly why they're worth listing rather than
dismissing as pre-existing style noise.

`.editorconfig` also suppresses `CS8600`/`CS8603`/`CS8618` project-wide despite
`<Nullable>enable</Nullable>` being set in `Directory.Build.props` (verified: `dotnet build`
produces 0 warnings even with uninitialized non-nullable properties like `TodoItem.Description`).
This is a nullable-handling policy decision, not a naming/accessibility issue, but it's the same
kind of "convention says one thing, `.editorconfig` quietly says another" gap — see the
async/C# audit for the full finding.

## 12. DTOs

**Verbatim duplication, not just similar shapes:**

- `Application/Todos/Get/TodoResponse.cs` and `Application/Todos/GetById/TodoResponse.cs` are
  byte-for-byte identical (`Id`, `UserId`, `Description`, `DueDate`, `Labels`, `IsCompleted`,
  `CreatedAt`, `CompletedAt` — same types, same mutable `{ get; set; }` shape) — two separate
  `sealed class` definitions in two namespaces for the exact same DTO, because the CQRS
  folder-per-use-case convention was applied even to the response shape.
- `Application/Users/GetById/UserResponse.cs` and `Application/Users/GetByEmail/UserResponse.cs`
  are likewise byte-for-byte identical (`Id`, `Email`, `FirstName`, `LastName`).
- **Cross-aggregate style inconsistency on top of the duplication**: the `TodoResponse` pair is
  a mutable `sealed class` with `{ get; set; }`; the `UserResponse` pair is an immutable `sealed
  record` with `{ get; init; }`. Two DTOs serving the identical purpose (a flat read projection)
  are modeled with two different idioms depending only on which aggregate they belong to.

This is real duplication with a real inconsistency layered on it — not a case of "these just
happen to look similar." A shared `TodoResponse`/`UserResponse` per aggregate (one file, reused
by both queries) would remove both the duplication and force the style question to be answered
once.

## 13. Services & handlers — sizing

No handler, service, or configuration class in this codebase exceeds ~60 lines or mixes more
than one responsibility. The largest single file by logical complexity is
`DomainEventsDispatcher` (reflection-based dynamic dispatch with two caching dictionaries) —
justified complexity for genuine polymorphic dispatch-by-runtime-type, not overengineering, but
it is the one place worth a comment explaining *why* reflection was chosen over a simpler
`switch` (there's no such comment today).

## 14. Overengineering / underengineering

- **Overengineering: none found.** No unnecessary abstraction layers, no interfaces with a
  single implementation "for testability" that aren't actually used polymorphically, no generic
  frameworks-within-the-framework. The explicit avoidance of Repository/UoW/Specification (per
  `CLAUDE.md`/`EF_CORE_GUIDELINES.md`) is itself evidence of restraint, not gaps.
- **Underengineering: one real instance.** `PermissionProvider.GetForUserIdAsync`
  (`Infrastructure/Authorization/PermissionProvider.cs`) always returns an empty `HashSet<string>`
  — combined with `PermissionAuthorizationHandler` unconditionally calling
  `context.Succeed(requirement)` for any authenticated user (both explicitly marked `// TODO` in
  the source), the entire permission system is a non-functional stub today. `.HasPermission(...)`
  on `Users/GetById` currently behaves identically to `.RequireAuthorization()` with no policy.
  This is flagged, not "fixed," because it's clearly a known, intentional template placeholder —
  but it means **no endpoint in this codebase is actually permission-gated today**, which matters
  if anyone assumes otherwise from reading the endpoint code alone.

## 15. Consolidated findings

| # | Area | File | Finding | Severity |
|---|------|------|---------|----------|
| 1 | DDD | `Domain/Todos/TodoItem.cs`, `Domain/Users/User.cs` | Anemic domain model — public setters, no invariant-enforcing behavior methods | Medium (design debt) |
| 2 | Domain Events | `Infrastructure/DomainEvents/DomainEventsDispatcher.cs` | New `IServiceScope` per event, not per dispatch call | Low (perf) |
| 3 | Domain Events | `Application/Users/Register/UserRegisteredDomainEventHandler.cs` | Duplicate no-op handler class (`...Handler1`) | Low (dead code / clarity) |
| 4 | DI / Style | `Infrastructure/Authentication/UserContext.cs`, `Infrastructure/Authorization/PermissionAuthorizationPolicyProvider.cs` | Traditional constructor instead of primary constructor, against `.cursorrules` | Low (style) |
| 5 | Validation | `Application/Todos/Create/CreateTodoCommandValidator.cs`, `Application/Todos/Copy/CopyTodoCommandValidator.cs` | `public class`, not `internal sealed` | Low (style) |
| 6 | Validation | `Application/Todos/Update/UpdateTodoCommandHandler.cs` | No validator on `UpdateTodoCommand.Description` | Low (currently unreachable — see #9) |
| 7 | Error handling | `Application/Users/GetByEmail/GetUserByEmailQueryHandler.cs` vs `Application/Users/Login/LoginUserCommandHandler.cs` | Inconsistent enumeration-safety posture (one leaks "exists but not yours", the other doesn't) | Medium (security-adjacent inconsistency) |
| 8 | Dead code | `Application/Todos/Update/UpdateTodoCommand.cs` + handler | No `Update.cs` endpoint maps this command anywhere — fully unreachable | Low (dead code) |
| 9 | Security (latent) | `Application/Todos/Update/UpdateTodoCommandHandler.cs` | Missing `UserId` ownership filter, unlike `Complete`/`Delete` — IDOR risk **if** wired up later | Medium (latent, currently unreachable) |
| 10 | DTOs | `Application/Todos/Get/TodoResponse.cs` vs `Application/Todos/GetById/TodoResponse.cs` | Verbatim duplicate DTO | Low (duplication) |
| 11 | DTOs | `Application/Users/GetById/UserResponse.cs` vs `Application/Users/GetByEmail/UserResponse.cs` | Verbatim duplicate DTO, plus class-vs-record style split from the `TodoResponse` pair | Low (duplication + inconsistency) |
| 12 | Underengineering | `Infrastructure/Authorization/PermissionProvider.cs`, `PermissionAuthorizationHandler.cs` | Permission system is a non-functional stub — `HasPermission(...)` currently gates nothing | High (if unnoticed before shipping) |
| 13 | Naming/Accessibility | `Web.Api/Middleware/RequestContextLoggingMiddleware.cs` | `public`, not `internal sealed`, with no cross-project consumer | Low (style) |

Items already filed in full detail in the prior async/C# audit (not repeated here beyond
cross-reference): the `RequestContextLoggingMiddleware` `using`-without-`await` bug, the
suppressed `CS8600`/`CS8603`/`CS8618` nullable diagnostics, `ClaimsPrincipalExtensions` throwing
`ApplicationException`, `PasswordHasher.Verify` not length-checking `Split('-')`, and the
`Activator.CreateInstance` unchecked cast in `DomainEventsDispatcher`.

---

## REQUIRED (going forward)

1. New entities that gain a business rule (a state transition, an invariant across two
   properties) **must** express it as a method on the entity, not as inline property assignment
   in a handler — don't deepen the anemic-model pattern (§3) further; existing entities are
   grandfathered, not a template to copy.
2. New validators **must** be `internal sealed class : AbstractValidator<TCommand>`, matching
   the majority pattern — not `public class` (§9, §11).
3. New response DTOs **must** be checked against existing `*Response` types in sibling
   use-case folders for the same aggregate before creating a new one — don't reintroduce the
   `TodoResponse`/`UserResponse` duplication pattern (§12) a third time.
4. Any command handler that loads an entity scoped to the current user **must** filter by
   `userContext.UserId` in the same query that loads it (matching `Complete`/`Delete`/`Create`/
   `Copy`), not rely on a later check — this is the one place a missed convention becomes a
   security bug (§9, finding #9).
5. Before wiring up `UpdateTodoCommand` to an endpoint, add the missing `UserId` ownership
   filter and a validator for `Description` — don't ship the current handler as-is (§9, §15 #6/#9).

## RECOMMENDED

- Deduplicate `TodoResponse` (Get/GetById) and `UserResponse` (GetById/GetByEmail) into one type
  per aggregate, reused by both queries; settle the class-vs-record question once when doing so.
- Decide, as a team, whether to invest in rich entities (`TodoItem.Complete(DateTime utcNow)`
  encapsulating the "already completed" check) or explicitly document the anemic-model choice as
  intentional in `CLAUDE.md` so it stops reading as accidental DDD drift.
- Remove `UserRegisteredDomainEventHandler1` or rename/document it if a second handler is
  genuinely intentional.
- Align `GetUserByEmailQueryHandler`'s error behavior with `LoginUserCommandHandler`'s
  enumeration-safety posture, or explicitly document why `GetByEmail` is allowed to distinguish
  "not found" from "not yours" (e.g., because it's only ever called by/for an already-authenticated
  caller looking up their own account, not a public lookup) — right now the difference looks
  unintentional.
- Either implement `PermissionProvider.GetForUserIdAsync` for real or add a startup-time guard
  (e.g., a log warning, or fail-fast in non-Development environments) so the stub can't silently
  ship as "authorization" in a real deployment.
- Add the missing `Infrastructure ↛ Application` (and, if useful, `Infrastructure ↛ Domain`
  where not legitimately needed) checks to `LayerTests` alongside the existing six, since
  `Infrastructure` is the layer most likely to accumulate a stray convenience reference over time.

## FORBIDDEN

- Introducing a second, differently-shaped DTO for data that already has a `*Response` type in a
  sibling use-case folder of the same aggregate — check first (§12).
- Adding new business rules as inline property mutation spread across a handler when the
  behavior naturally belongs on the entity — don't grow §3 further; new invariants belong on the
  entity even if old ones don't yet.
- Wiring a new `Update`/`Patch`-style command handler that loads an entity by id **without** an
  ownership (`UserId`) filter when the aggregate is user-owned, matching the pattern already
  established by `Complete`/`Delete`/`Create`/`Copy` — see finding #9.
- Declaring a new validator, handler, or endpoint-adjacent type as `public` when nothing outside
  its own project needs to reference it — default to `internal sealed` per `.cursorrules`, and
  treat the three cataloged exceptions (§11) as debt, not precedent.
