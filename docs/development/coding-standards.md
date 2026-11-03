# Coding Standards

Sources: `.cursorrules`, `Directory.Build.props`, `CLAUDE.md`, and the code itself (the actual,
observed convention wins where `.cursorrules` and the code disagree — see the Minimal APIs note
below). Full deviation catalog with file citations:
[`docs/engineering/ENGINEERING_STANDARDS.md`](../engineering/ENGINEERING_STANDARDS.md).

## Language / project settings

`Directory.Build.props`:

```xml
<TargetFramework>net10.0</TargetFramework>
<ImplicitUsings>enable</ImplicitUsings>
<Nullable>enable</Nullable>
<AnalysisLevel>latest</AnalysisLevel>
<AnalysisMode>All</AnalysisMode>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<CodeAnalysisTreatWarningsAsErrors>true</CodeAnalysisTreatWarningsAsErrors>
<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
```

C# 12, .NET 10 (`net8.0`/`net9.0` are supported alternate targets — see the commented-out lines in
`Directory.Build.props` and its instruction to also update `src/Web.Api/Dockerfile` if you switch).
`TreatWarningsAsErrors=true` + `AnalysisMode=All` (SonarAnalyzer + full built-in analyzers) means
**a warning fails `dotnet build`** — don't suppress with `#pragma` or `NoWarn` to work around a
real issue; fix it.

**EXISTING caveat**: `.editorconfig` suppresses `CS8600`/`CS8603`/`CS8618` project-wide despite
`Nullable` being enabled — meaning nullable-reference-type warnings for uninitialized non-nullable
properties (e.g. `TodoItem.Description` has no initializer and is never null-checked) do not
actually fail the build today. Don't assume `<Nullable>enable</Nullable>` gives you real
compile-time null safety in this template as shipped — it's enabled at the project level but its
most common warnings are suppressed at the `.editorconfig` level.

## REQUIRED

1. **`internal sealed` by default.** Every class is `internal sealed` unless something outside its
   own project genuinely needs to reference it. Existing exceptions
   (`CreateTodoCommandValidator`, `CopyTodoCommandValidator`, `RequestContextLoggingMiddleware`)
   are drift, not precedent — don't add a fourth.
2. **Primary constructors for DI**, per `.cursorrules`: `internal sealed class
   CompleteTodoCommandHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider,
   IUserContext userContext) : ICommandHandler<CompleteTodoCommand>` — not a traditional
   constructor assigning `private readonly` fields by hand. (Two existing Infrastructure classes,
   `UserContext` and `PermissionAuthorizationPolicyProvider`, don't follow this — legacy, not the
   model to copy; C# 12 primary constructors do support base-constructor arguments, so there's no
   language reason to fall back to the old style.)
3. **Explicit typing by default; `var` only when the type is obvious from the right-hand side**
   (`var user = new User();` is fine; `var result = SomeMethod();` where the return type isn't
   visually obvious should spell out the type).
4. **`Guid` for identifiers** unless a specific reason dictates otherwise.
5. **`is null` / `is not null`**, never `== null` / `!= null`.
6. **`async`/`await` for all I/O** — no `.Result`/`.Wait()`/`.GetAwaiter().GetResult()` blocking
   on a `Task`. Every EF Core call uses the async, cancellable overload
   (`SingleOrDefaultAsync(..., cancellationToken)`, never the sync `SingleOrDefault`).
7. **Thread `CancellationToken` end-to-end** — endpoint delegate → handler → every awaited call.
   No handler constructs its own token or silently drops the one it was given.
8. **Prefer record types for immutable data** — commands/queries/events/DTOs that don't need
   mutation after construction (`sealed record`), matching `GetTodosQuery`,
   `TodoItemCreatedDomainEvent`, `Users/Login.Request`. Mutable `sealed class` with `{ get; set;
   }` is used in a few existing places (`CreateTodoCommand`, `TodoItem`, `User`) — legacy style,
   not the one to prefer for new types.

## RECOMMENDED

- Match a new validator/response DTO/handler's accessibility and sealing to the majority pattern
  in its sibling folder before assuming the file you're copying from is itself correct — see the
  known exceptions cataloged in `ENGINEERING_STANDARDS.md`.
- For a new aggregate with real invariants, favor a behavior method on the entity over inline
  property mutation in a handler — see [architecture/domain.md](../architecture/domain.md).
- Keep handlers small and single-purpose — no handler in this template exceeds roughly 60 lines or
  mixes more than one responsibility; if a new handler is growing past that, it's likely doing two
  use cases' worth of work.

## FORBIDDEN

- Synchronous I/O (`.Result`, `.Wait()`, sync EF Core calls) anywhere in a handler or Infrastructure
  service.
- Throwing exceptions for expected/business failure — see
  [patterns/error-handling.md](../patterns/error-handling.md).
- Introducing MediatR, a Repository/Unit of Work/Specification layer, or a second CQRS mechanism —
  see [architecture/overview.md](../architecture/overview.md#architectural-decisions-baked-into-this-template).
- `Controller`/`ControllerBase`/`[ApiController]` classes — see
  [api/minimal-api.md](../api/minimal-api.md). (`.cursorrules` line 9, "Prefer controller
  endpoints over minimal APIs", is a legacy/generic rule from before this template's actual
  convention was set; the code, `CLAUDE.md`, and every endpoint in `src/Web.Api` are unambiguous
  that Minimal APIs are the pattern — follow the code and `CLAUDE.md`, not that one `.cursorrules`
  line.)
- Suppressing an analyzer warning (`#pragma warning disable`, a new `.editorconfig` suppression,
  `NoWarn`) to make a real issue go away instead of fixing it.

## Naming

- `<Verb><Aggregate>Command`/`Query`, `<Same>Handler`, `<Same>Validator`, `<Aggregate>Response` —
  see [patterns/cqrs.md](../patterns/cqrs.md#feature-folder-structure).
- `<Aggregate>Errors` static class per aggregate, error codes `"<Aggregate>.<Reason>"` — see
  [architecture/domain.md](../architecture/domain.md#errors--errors-static-classes).
- Domain events: past-tense, `<Aggregate><Transition>DomainEvent`.
- Endpoint classes: operation name, not HTTP verb (`Create`, `Complete`, not `Post`, `Put`).
