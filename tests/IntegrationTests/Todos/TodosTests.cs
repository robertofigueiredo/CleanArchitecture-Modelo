using System.Net;
using System.Net.Http.Json;

namespace IntegrationTests.Todos;

public sealed class TodosTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private sealed record TodoDto(Guid Id, Guid UserId, string Description, bool IsCompleted);

    [Fact]
    public async Task GetTodo_Should_ReturnUnauthorized_WhenTokenIsMissing()
    {
        // Act
        HttpResponseMessage response = await HttpClient.GetAsync($"todos/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateTodo_Should_PersistTodo_ThatCanBeRetrievedById()
    {
        // Arrange
        (Guid userId, AccessTokens tokens) = await RegisterAndLoginAsync();
        Authenticate(tokens.AccessToken);

        var createRequest = new
        {
            userId,
            description = "Integration test todo",
            labels = new[] { "integration" },
            priority = 2
        };

        // Act
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("todos", createRequest);

        // Assert
        createResponse.EnsureSuccessStatusCode();
        Guid todoId = await createResponse.Content.ReadFromJsonAsync<Guid>();
        todoId.ShouldNotBe(Guid.Empty);

        HttpResponseMessage getResponse = await HttpClient.GetAsync($"todos/{todoId}");
        getResponse.EnsureSuccessStatusCode();

        TodoDto? todo = await getResponse.Content.ReadFromJsonAsync<TodoDto>();
        todo!.Id.ShouldBe(todoId);
        todo.UserId.ShouldBe(userId);
        todo.Description.ShouldBe("Integration test todo");
        todo.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task CompleteTodo_Should_MarkTodoAsCompleted()
    {
        // Arrange
        (Guid userId, AccessTokens tokens) = await RegisterAndLoginAsync();
        Authenticate(tokens.AccessToken);

        var createRequest = new
        {
            userId,
            description = "Todo to complete",
            labels = Array.Empty<string>(),
            priority = 1
        };
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("todos", createRequest);
        createResponse.EnsureSuccessStatusCode();
        Guid todoId = await createResponse.Content.ReadFromJsonAsync<Guid>();

        // Act
        HttpResponseMessage completeResponse = await HttpClient.PutAsync($"todos/{todoId}/complete", null);

        // Assert
        completeResponse.EnsureSuccessStatusCode();

        HttpResponseMessage getResponse = await HttpClient.GetAsync($"todos/{todoId}");
        getResponse.EnsureSuccessStatusCode();
        TodoDto? todo = await getResponse.Content.ReadFromJsonAsync<TodoDto>();
        todo!.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateTodo_Should_ReturnConflict_WhenIdempotencyKeyIsReused()
    {
        // Arrange
        (Guid userId, AccessTokens tokens) = await RegisterAndLoginAsync();
        Authenticate(tokens.AccessToken);

        var createRequest = new
        {
            userId,
            description = "Idempotent todo",
            labels = Array.Empty<string>(),
            priority = 1
        };
        string idempotencyKey = Guid.NewGuid().ToString();

        // Act
        HttpResponseMessage first = await PostTodoAsync(createRequest, idempotencyKey);
        HttpResponseMessage second = await PostTodoAsync(createRequest, idempotencyKey);

        // Assert
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GetTodos_Should_ReturnPagedResults()
    {
        // Arrange
        (Guid userId, AccessTokens tokens) = await RegisterAndLoginAsync();
        Authenticate(tokens.AccessToken);

        var createRequest = new
        {
            userId,
            description = "Paged todo",
            labels = Array.Empty<string>(),
            priority = 1
        };
        await PostTodoAsync(createRequest, Guid.NewGuid().ToString());
        await PostTodoAsync(createRequest, Guid.NewGuid().ToString());

        // Act
        HttpResponseMessage response = await HttpClient.GetAsync($"todos?userId={userId}&page=1&pageSize=1");

        // Assert
        response.EnsureSuccessStatusCode();
        PagedTodos? page = await response.Content.ReadFromJsonAsync<PagedTodos>();
        page!.Items.Count.ShouldBe(1);
        page.TotalCount.ShouldBeGreaterThanOrEqualTo(2);
        page.HasNextPage.ShouldBeTrue();
    }

    private sealed record PagedTodos(
        List<TodoDto> Items,
        int Page,
        int PageSize,
        int TotalCount,
        bool HasNextPage,
        bool HasPreviousPage);

    private async Task<HttpResponseMessage> PostTodoAsync(object request, string idempotencyKey)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "todos")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);

        return await HttpClient.SendAsync(message);
    }
}
