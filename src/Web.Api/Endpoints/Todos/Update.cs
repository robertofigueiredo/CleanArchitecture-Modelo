using Application.Abstractions.Messaging;
using Application.Todos.Update;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Todos;

internal sealed class Update : IEndpoint
{
    public sealed record Request(string Description);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("todos/{id:guid}", async (
            Guid id,
            Request request,
            ICommandHandler<UpdateTodoCommand> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new UpdateTodoCommand(id, request.Description);

            Result result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Todos)
        .RequireAuthorization();
    }
}
