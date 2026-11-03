# Unit Tests

**RECOMMENDED — not present in this template today.** No unit test project exists for `Domain`,
`Application`, or `Infrastructure`. This page describes the intended shape, consistent with how
the codebase is already structured (small, single-purpose handlers behind explicit interfaces),
for when a unit test project is added.

## Project setup

A new `tests/UnitTests` (or `tests/Application.UnitTests` / `tests/Domain.UnitTests` if you want
per-layer separation) xUnit project, referencing `Domain` and `Application` — mirror
`tests/ArchitectureTests`'s use of `Shouldly` for assertions if you want consistent assertion
style across test projects (not required — plain xUnit `Assert` works too).

## Testing a command handler

Handlers depend only on interfaces (`IApplicationDbContext`, `IUserContext`,
`IDateTimeProvider`, `IPasswordHasher`, `ITokenProvider`) — every dependency is fake-able without
a real database or HTTP context.

```csharp
// RECOMMENDED shape — not a real test today
public class CompleteTodoCommandHandlerTests
{
    [Fact]
    public async Task Handle_TodoAlreadyCompleted_ReturnsFailure()
    {
        // Arrange
        var context = new FakeApplicationDbContext();   // e.g. EF Core InMemory/SQLite provider, or a hand-rolled fake
        var userId = Guid.NewGuid();
        var todoItem = new TodoItem { Id = Guid.NewGuid(), UserId = userId, IsCompleted = true };
        context.TodoItems.Add(todoItem);
        await context.SaveChangesAsync(CancellationToken.None);

        var handler = new CompleteTodoCommandHandler(
            context,
            new FakeDateTimeProvider(),
            new FakeUserContext(userId));

        // Act
        Result result = await handler.Handle(new CompleteTodoCommand(todoItem.Id), CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(TodoItemErrors.AlreadyCompleted(todoItem.Id));
    }
}
```

For `IApplicationDbContext`, prefer EF Core's InMemory or SQLite provider over a hand-rolled mock
— it exercises real LINQ translation (closer to production behavior than mocking `DbSet<T>`), and
it's the lowest-friction way to fake a context that exposes `DbSet<T>` directly (this template's
deliberate no-repository choice — see [persistence/ef-core.md](../persistence/ef-core.md) — means
there's no narrower seam to mock against).

## What to assert

- **`Result`/`Error` outcome** — the correct `*Errors` factory result on every failure branch, and
  `IsSuccess` + the correct returned value on the happy path. This is directly testable because
  every handler returns `Result`/`Result<T>` rather than throwing (see
  [patterns/result-pattern.md](../patterns/result-pattern.md)).
- **Ownership checks** — a test asserting that a handler loading a user-owned entity rejects a
  mismatched `userContext.UserId` is the single most valuable test to add given this template's
  existing pattern of scoping every user-owned query by `UserId` (see
  [architecture/application.md](../architecture/application.md#required)) — a regression here is
  a real IDOR vulnerability, not a cosmetic bug.
- **Domain events raised** — assert `entity.DomainEvents` contains the expected event after a
  successful handler call, if the event's presence matters to the feature.
- **Side effects on `SaveChangesAsync`** — that the entity was actually added/mutated as expected.

## Testing a validator

```csharp
// RECOMMENDED shape — not a real test today
public class CreateTodoCommandValidatorTests
{
    private readonly CreateTodoCommandValidator _validator = new();

    [Fact]
    public void Validate_EmptyDescription_HasValidationError()
    {
        var command = new CreateTodoCommand { UserId = Guid.NewGuid(), Description = "" };

        TestValidationResult<CreateTodoCommand> result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.Description);
    }
}
```

`FluentValidation.TestHelper`'s `TestValidate`/`ShouldHaveValidationErrorFor` is the standard way
to unit test an `AbstractValidator<T>` without going through `ValidationDecorator`.

## Testing a Domain entity

Today's entities (`TodoItem`, `User`) are anemic property bags — see
[architecture/domain.md](../architecture/domain.md) — so there's little behavior to unit test on
them directly yet. If you add a behavior method (`TodoItem.Complete(DateTime utcNow)`) per that
page's guidance, test it the same way you'd test any pure method: state before, call, assert state
after (including that it raised the expected domain event via `entity.DomainEvents`).

## What NOT to unit test this way

Don't write a "unit test" that spins up the full DI container, `ValidationDecorator`,
`LoggingDecorator`, and a real HTTP pipeline just to call one handler — that's an integration test
(see [testing/integration-tests.md](./integration-tests.md)). A unit test constructs the handler
directly with fakes, no decorators, no DI container.
