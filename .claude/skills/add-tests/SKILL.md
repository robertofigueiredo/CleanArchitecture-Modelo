---
name: add-tests
description: Backfill missing tests for existing use cases in the Clean Architecture template — handler unit tests, FluentValidation validator tests, and HTTP integration tests. Use when the user asks to add, improve, or backfill test coverage.
argument-hint: <use case or feature to cover, e.g. "CopyTodoCommand" or "the Users feature">
---

# Add Tests for an Existing Use Case

Backfill the three test types this template expects for every slice. Read the target handler/validator/endpoint first, then mirror the structure of the closest existing test class.

## Workflow

1. **Locate the slice.** Find the command/query, handler, validator, and endpoint for the target use case. List every distinct outcome: each guard clause (`return Result.Failure(...)`) and the happy path.
2. **Check what already exists** in `tests/Application.UnitTests/{Feature}/` and `tests/IntegrationTests/{Feature}/` — extend existing classes, don't duplicate.
3. **Write handler unit tests** — one test per failure path plus one happy path asserting persisted state and raised domain events.
4. **Write validator tests** (commands only) — one failing test per rule plus one fully-valid command.
5. **Write integration tests** — happy path over real HTTP with state asserted via a follow-up GET; error translation (404/409/400) where the handler has failure paths; 401 without a token if the route family is new.
6. **Run** `dotnet test` (Docker must be running for the integration tests) and fix failures before finishing.

## Conventions

- **Frameworks:** xUnit + Shouldly + NSubstitute; `FluentValidation.TestHelper` for validators. Global usings already cover `Xunit`, `NSubstitute`, `Shouldly`, `SharedKernel`.
- **Unit test base:** inherit `BaseHandlerTest`; use `CreateDbContext()` for a fresh in-memory `TestDbContext` and `CreateCache()` for a real `HybridCache`. Substitute only interfaces (`IUserContext`, `IDateTimeProvider`, `IPasswordHasher`, `ITokenProvider`) — never mock the DbContext.
- **Integration test base:** inherit `BaseIntegrationTest(factory)` with the `IntegrationTestWebAppFactory` collection fixture; use `RegisterAndLoginAsync()` + `Authenticate(token)` for authenticated calls. Define private response DTO records inside the test class (see `TodosTests.TodoDto`).
- **Naming:** classes `{Handler}Tests` / `{Feature}ValidatorsTests` / `{Feature}Tests`; methods `Handle_Should_{Outcome}_When{Condition}` (unit) or `{Action}_Should_{Outcome}[_When{Condition}]` (integration).
- **Structure:** `// Arrange` / `// Act` / `// Assert` comments in every test.
- **Assertions:** compare exact domain errors (`result.Error.ShouldBe(TodoItemErrors.NotFound(id))`); assert persisted state by re-reading from the context (unit) or via a GET request (integration); assert domain events with `entity.DomainEvents.ShouldContain(e => e is XDomainEvent)`.

Full annotated templates: [../add-feature/references/tests.md](../add-feature/references/tests.md) (if the `add-feature` skill is installed) or mirror `CreateTodoCommandHandlerTests`, `TodoValidatorsTests`, and `TodosTests` in this repo.
