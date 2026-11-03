namespace Application.Todos;

internal static class TodoCacheKeys
{
    internal static string ById(Guid userId, Guid todoItemId) => $"todos-{userId}-{todoItemId}";
}
