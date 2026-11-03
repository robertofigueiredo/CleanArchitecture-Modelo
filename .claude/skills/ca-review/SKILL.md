---
name: ca-review
description: Review pending changes against the Clean Architecture template's conventions — layer boundaries, Result-based error handling, slice structure, validation, endpoints, and test coverage. Use when the user asks to review changes, check conventions, or audit a feature before committing.
argument-hint: [optional: specific files or feature to review; defaults to the working-tree diff]
---

# Clean Architecture Convention Review

Review the given scope (default: `git diff` + untracked files) against this template's conventions. Report findings with file:line references, ordered by severity. Do not fix anything unless asked.

## Checklist

### Layer boundaries (violations are blockers)
- Domain references only `SharedKernel` — no EF Core, no Application/Infrastructure/Web.Api types.
- Application references Domain + SharedKernel only; data access exclusively through `IApplicationDbContext`; no `using Infrastructure.*` anywhere in Application.
- Persistence details (keys, conversions, relationships) live in Infrastructure `IEntityTypeConfiguration<>` classes, not on entities.
- Web.Api endpoints contain no business logic — only request→command mapping and result matching.

### Slice structure
- One folder per use case under `src/Application/{Feature}/{UseCase}/`; the endpoint mirrors it at `src/Web.Api/Endpoints/{Feature}/{UseCase}.cs`.
- Naming: `{Verb}{Entity}Command` / `Get{X}Query` / `...Handler` / `...Validator` / `{X}Response`.
- Handlers are `internal sealed` with primary constructors, implementing the custom `ICommandHandler<>`/`IQueryHandler<>` — no MediatR, no manual DI registration for handlers/validators/endpoints.
- Response DTOs are per-slice; queries project with `.Select(...)` and never return domain entities.

### Error handling
- Expected failures return `Result`/`Result<T>` — no exceptions for control flow, no try/catch around business rules.
- Errors come from static `{Entity}Errors` factories with `"{Feature}.{Reason}"` codes and the semantically correct type (`NotFound`/`Conflict`/`Problem`/`Failure`).
- Endpoints translate failures only via `result.Match(Results.Ok|NoContent, CustomResults.Problem)`.

### Validation & security
- Every command has a FluentValidation `{Command}Validator`; handlers don't re-check input shape (but do enforce business rules).
- Handlers acting on user-owned data enforce ownership: filter by `IUserContext.UserId` or return `UserErrors.Unauthorized()`.
- New endpoints call `.RequireAuthorization()` (or `.HasPermission(...)`) and `.WithTags(Tags.X)`.
- No `DateTime.UtcNow`/`DateTime.Now` in Application — use `IDateTimeProvider`.

### State changes & caching
- Commands that mutate state raise a domain event via `entity.Raise(new XDomainEvent(id))` before `SaveChangesAsync`.
- Any `HybridCache`-cached read has matching invalidation (`cache.RemoveAsync`) in every command that mutates that data; keys come from the `{Feature}CacheKeys` class.

### Tests
- New/changed handlers have unit tests covering every `Result.Failure` path plus the happy path (persisted state + domain events).
- New/changed validators have `TestValidate` tests per rule.
- New/changed endpoints have integration tests over real HTTP.
- Test naming and Arrange/Act/Assert structure match existing tests.

## Output format

Group findings as **Blockers** (layer violations, missing auth, thrown exceptions for expected failures), **Convention violations** (naming, structure, error codes, missing events/invalidation), and **Test gaps**. For each: `file:line`, what's wrong, and the one-line fix. Close with a verdict: ready to commit, or what must change first. If everything passes, say so and run `dotnet build` + `dotnet test` to confirm.
