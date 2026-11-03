# Test Templates

Stack: xUnit + Shouldly + NSubstitute (unit), FluentValidation.TestHelper (validators), WebApplicationFactory + Testcontainers (integration). Every new use case gets all three.

## Handler unit tests

`tests/Application.UnitTests/{Feature}/{UseCase}{Command|Query}HandlerTests.cs`. Inherit `BaseHandlerTest` — it provides `CreateDbContext()` (in-memory `TestDbContext` implementing `IApplicationDbContext`) and `CreateCache()` (real `HybridCache`). Mock only interfaces (`IUserContext`, `IDateTimeProvider`) with NSubstitute.

Cover: each failure path (one test per guard clause) and the happy path including persisted state and raised domain events.

```csharp
using Application.Abstractions.Authentication;
using Application.Todos.Archive;
using Application.UnitTests.Abstractions;
using Domain.Todos;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.UnitTests.Todos;

public sealed class ArchiveTodoCommandHandlerTests : BaseHandlerTest
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenTodoDoesNotExist()
    {
        // Arrange
        await using TestDbContext context = CreateDbContext();
        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(UserId);
        IDateTimeProvider dateTimeProvider = Substitute.For<IDateTimeProvider>();

        var command = new ArchiveTodoCommand(Guid.NewGuid());
        var handler = new ArchiveTodoCommandHandler(context, dateTimeProvider, userContext);

        // Act
        Result result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(TodoItemErrors.NotFound(command.TodoItemId));
    }

    [Fact]
    public async Task Handle_Should_ArchiveTodoAndRaiseDomainEvent_WhenValid()
    {
        // Arrange
        await using TestDbContext context = CreateDbContext();
        var todoItem = new TodoItem
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Description = "Archive me",
            CreatedAt = DateTime.UtcNow
        };
        context.TodoItems.Add(todoItem);
        await context.SaveChangesAsync();

        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(UserId);
        IDateTimeProvider dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(DateTime.UtcNow);

        var command = new ArchiveTodoCommand(todoItem.Id);
        var handler = new ArchiveTodoCommandHandler(context, dateTimeProvider, userContext);

        // Act
        Result result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        TodoItem archived = await context.TodoItems.SingleAsync(t => t.Id == todoItem.Id);
        archived.IsArchived.ShouldBeTrue();
        archived.DomainEvents.ShouldContain(e => e is TodoItemArchivedDomainEvent);
    }
}
```

Conventions:
- Test names: `Handle_Should_{Outcome}_When{Condition}`.
- `// Arrange` / `// Act` / `// Assert` comments in every test.
- Assert failures by comparing the exact error: `result.Error.ShouldBe(TodoItemErrors.NotFound(id))`.
- `GlobalUsings.cs` already imports `Xunit`, `NSubstitute`, `Shouldly`, and `SharedKernel` — don't re-add those usings.
- If the entity gains new properties, `TestDbContext` picks them up automatically; only touch it when adding a whole new `DbSet`.

## Validator tests

`tests/Application.UnitTests/{Feature}/{Feature}ValidatorsTests.cs` (extend the existing file if present). One class covers all validators of a feature.

```csharp
[Fact]
public void CreateValidator_Should_HaveError_WhenDescriptionIsEmpty()
{
    var command = new CreateTodoCommand
    {
        UserId = Guid.NewGuid(),
        Description = string.Empty,
        Priority = Priority.Low
    };

    TestValidationResult<CreateTodoCommand> result = _createValidator.TestValidate(command);

    result.ShouldHaveValidationErrorFor(c => c.Description);
}
```

Cover each rule's failure plus one fully-valid command (`ShouldNotHaveAnyValidationErrors`).

## Integration tests

`tests/IntegrationTests/{Feature}/{Feature}Tests.cs` (extend the existing file if present). Inherit `BaseIntegrationTest(factory)` — it runs the real API against a Testcontainers Postgres and provides `HttpClient`, `RegisterAndLoginAsync()`, and `Authenticate(token)`. Tests go through real HTTP, never call handlers directly.

```csharp
[Fact]
public async Task ArchiveTodo_Should_MarkTodoAsArchived()
{
    // Arrange
    (Guid userId, AccessTokens tokens) = await RegisterAndLoginAsync();
    Authenticate(tokens.AccessToken);

    var createRequest = new
    {
        userId,
        description = "Todo to archive",
        labels = Array.Empty<string>(),
        priority = 1
    };
    HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("todos", createRequest);
    createResponse.EnsureSuccessStatusCode();
    Guid todoId = await createResponse.Content.ReadFromJsonAsync<Guid>();

    // Act
    HttpResponseMessage response = await HttpClient.PutAsync($"todos/{todoId}/archive", null);

    // Assert
    response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
}
```

Minimum coverage per endpoint: one unauthorized test (no token → 401) if it's a new route family, one happy-path test asserting observable state via a follow-up GET, and one failure translation test (e.g. unknown id → 404) when the handler has failure paths.

## Run

```
dotnet test
```

Integration tests need Docker running (Testcontainers). Architecture tests will fail the build if a layer dependency rule is violated — fix the dependency, never the test.
