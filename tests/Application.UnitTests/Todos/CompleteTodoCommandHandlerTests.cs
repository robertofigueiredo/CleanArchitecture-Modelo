using Application.Abstractions.Authentication;
using Application.Todos.Complete;
using Application.UnitTests.Abstractions;
using Domain.Todos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SharedKernel;

namespace Application.UnitTests.Todos;

public sealed class CompleteTodoCommandHandlerTests : BaseHandlerTest
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenTodoDoesNotExist()
    {
        // Arrange
        await using TestDbContext context = CreateDbContext();
        HybridCache cache = CreateCache();
        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(UserId);
        IDateTimeProvider dateTimeProvider = Substitute.For<IDateTimeProvider>();

        var handler = new CompleteTodoCommandHandler(context, dateTimeProvider, userContext, cache);
        var command = new CompleteTodoCommand(Guid.NewGuid());

        // Act
        Result result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("TodoItems.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnAlreadyCompleted_WhenTodoIsCompleted()
    {
        // Arrange
        await using TestDbContext context = CreateDbContext();
        Guid todoItemId = await SeedTodoAsync(context, isCompleted: true);

        HybridCache cache = CreateCache();
        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(UserId);
        IDateTimeProvider dateTimeProvider = Substitute.For<IDateTimeProvider>();

        var handler = new CompleteTodoCommandHandler(context, dateTimeProvider, userContext, cache);
        var command = new CompleteTodoCommand(todoItemId);

        // Act
        Result result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("TodoItems.AlreadyCompleted");
    }

    [Fact]
    public async Task Handle_Should_CompleteTodoAndRaiseDomainEvent_WhenValid()
    {
        // Arrange
        await using TestDbContext context = CreateDbContext();
        Guid todoItemId = await SeedTodoAsync(context, isCompleted: false);

        HybridCache cache = CreateCache();
        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(UserId);
        DateTime completedAt = DateTime.UtcNow;
        IDateTimeProvider dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(completedAt);

        var handler = new CompleteTodoCommandHandler(context, dateTimeProvider, userContext, cache);
        var command = new CompleteTodoCommand(todoItemId);

        // Act
        Result result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        TodoItem todoItem = await context.TodoItems.SingleAsync(t => t.Id == todoItemId);
        todoItem.IsCompleted.ShouldBeTrue();
        todoItem.CompletedAt.ShouldBe(completedAt);
        todoItem.DomainEvents.ShouldContain(domainEvent => domainEvent is TodoItemCompletedDomainEvent);
    }

    private static async Task<Guid> SeedTodoAsync(TestDbContext context, bool isCompleted)
    {
        var todoItem = new TodoItem
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Description = "Existing todo",
            Priority = Priority.Low,
            Labels = [],
            IsCompleted = isCompleted,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = isCompleted ? DateTime.UtcNow : null
        };

        context.TodoItems.Add(todoItem);
        await context.SaveChangesAsync();

        return todoItem.Id;
    }
}
