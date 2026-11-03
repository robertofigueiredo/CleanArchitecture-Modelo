using Application.Abstractions.Messaging;
using Application.Abstractions.Pagination;
using Application.Todos.Get;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Todos;

internal sealed class Get : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("todos", async (
            Guid userId,
            string? search,
            string? sortColumn,
            string? sortOrder,
            int? page,
            int? pageSize,
            IQueryHandler<GetTodosQuery, PagedList<TodoResponse>> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetTodosQuery(
                userId,
                search,
                sortColumn,
                sortOrder,
                page ?? 1,
                pageSize ?? 10);

            Result<PagedList<TodoResponse>> result = await handler.Handle(query, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Todos)
        .RequireAuthorization();
    }
}
