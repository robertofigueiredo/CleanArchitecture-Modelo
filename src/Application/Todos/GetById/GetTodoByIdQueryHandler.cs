using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Todos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SharedKernel;

namespace Application.Todos.GetById;

internal sealed class GetTodoByIdQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    HybridCache cache)
    : IQueryHandler<GetTodoByIdQuery, TodoResponse>
{
    public async Task<Result<TodoResponse>> Handle(GetTodoByIdQuery query, CancellationToken cancellationToken)
    {
        Guid userId = userContext.UserId;

        TodoResponse? todo = await cache.GetOrCreateAsync(
            TodoCacheKeys.ById(userId, query.TodoItemId),
            async cancellation => await context.TodoItems
                .Where(todoItem => todoItem.Id == query.TodoItemId && todoItem.UserId == userId)
                .Select(todoItem => new TodoResponse
                {
                    Id = todoItem.Id,
                    UserId = todoItem.UserId,
                    Description = todoItem.Description,
                    DueDate = todoItem.DueDate,
                    Labels = todoItem.Labels,
                    IsCompleted = todoItem.IsCompleted,
                    CreatedAt = todoItem.CreatedAt,
                    CompletedAt = todoItem.CompletedAt
                })
                .SingleOrDefaultAsync(cancellation),
            cancellationToken: cancellationToken);

        if (todo is null)
        {
            return Result.Failure<TodoResponse>(TodoItemErrors.NotFound(query.TodoItemId));
        }

        return todo;
    }
}
