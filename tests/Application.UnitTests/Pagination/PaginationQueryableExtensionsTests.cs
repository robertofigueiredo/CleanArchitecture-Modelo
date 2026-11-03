using Application.Abstractions.Pagination;
using Domain.Todos;
using Domain.Users;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.UnitTests.Pagination;

/// <summary>
/// Exercises <see cref="PaginationQueryableExtensions.ToPagedResultAsync{TSource,TResult}"/>
/// against a real EF Core provider (InMemory) so Count/Skip/Take/Select/ordering/cancellation are
/// verified against actual LINQ-to-Entities translation, not a hand-rolled in-memory fake.
/// </summary>
public sealed class PaginationQueryableExtensionsTests : IDisposable
{
    private readonly ApplicationDbContext _context = CreateContext();

    [Fact]
    public async Task ToPagedResultAsync_FirstPage_ReturnsCorrectItemsAndMeta()
    {
        Guid userId = await SeedTodosAsync(_context, count: 30);
        PaginationRequest pagination = PaginationRequest.Create(page: 1, pageSize: 10).Value;

        PagedResult<Guid> result = await OrderedQuery(_context, userId)
            .ToPagedResultAsync(pagination, t => t.Id, CancellationToken.None);

        result.Items.Count.ShouldBe(10);
        result.TotalRecords.ShouldBe(30);
        result.TotalPages.ShouldBe(3);
        result.Page.ShouldBe(1);
    }

    [Fact]
    public async Task ToPagedResultAsync_MiddlePage_SkipsPreviousPagesAndReturnsCorrectSlice()
    {
        Guid userId = await SeedTodosAsync(_context, count: 30);
        List<Guid> expectedIds = await OrderedQuery(_context, userId).Select(t => t.Id).ToListAsync();
        PaginationRequest pagination = PaginationRequest.Create(page: 2, pageSize: 10).Value;

        PagedResult<Guid> result = await OrderedQuery(_context, userId)
            .ToPagedResultAsync(pagination, t => t.Id, CancellationToken.None);

        result.Items.ShouldBe(expectedIds.Skip(10).Take(10));
    }

    [Fact]
    public async Task ToPagedResultAsync_LastPage_ReturnsRemainderAndNoNextPage()
    {
        Guid userId = await SeedTodosAsync(_context, count: 25);
        PaginationRequest pagination = PaginationRequest.Create(page: 3, pageSize: 10).Value;

        PagedResult<Guid> result = await OrderedQuery(_context, userId)
            .ToPagedResultAsync(pagination, t => t.Id, CancellationToken.None);

        result.Items.Count.ShouldBe(5);
        result.TotalPages.ShouldBe(3);
        result.HasNextPage.ShouldBeFalse();
    }

    [Fact]
    public async Task ToPagedResultAsync_NoRecords_ReturnsEmptyPageWithZeroTotalPages()
    {
        Guid userId = Guid.NewGuid();
        PaginationRequest pagination = PaginationRequest.Create(page: 1, pageSize: 25).Value;

        PagedResult<Guid> result = await OrderedQuery(_context, userId)
            .ToPagedResultAsync(pagination, t => t.Id, CancellationToken.None);

        result.Items.ShouldBeEmpty();
        result.TotalRecords.ShouldBe(0);
        result.TotalPages.ShouldBe(0);
    }

    [Fact]
    public async Task ToPagedResultAsync_UsesProjection_DoesNotReturnTrackedEntities()
    {
        Guid userId = await SeedTodosAsync(_context, count: 5);
        PaginationRequest pagination = PaginationRequest.Create(page: 1, pageSize: 25).Value;

        PagedResult<string> result = await OrderedQuery(_context, userId)
            .ToPagedResultAsync(pagination, t => t.Description, CancellationToken.None);

        result.Items.ShouldAllBe(description => description.StartsWith("Todo "));
    }

    [Fact]
    public async Task ToPagedResultAsync_QueryHasNoOrderBy_ThrowsInvalidOperationException()
    {
        Guid userId = await SeedTodosAsync(_context, count: 5);
        PaginationRequest pagination = PaginationRequest.Create(page: 1, pageSize: 25).Value;

        IQueryable<TodoItem> unorderedQuery = _context.TodoItems.Where(t => t.UserId == userId);

        await Should.ThrowAsync<InvalidOperationException>(
            () => unorderedQuery.ToPagedResultAsync(pagination, t => t.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ToPagedResultAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        Guid userId = await SeedTodosAsync(_context, count: 5);
        PaginationRequest pagination = PaginationRequest.Create(page: 1, pageSize: 25).Value;
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => OrderedQuery(_context, userId).ToPagedResultAsync(pagination, t => t.Id, cts.Token));
    }

    private static IOrderedQueryable<TodoItem> OrderedQuery(ApplicationDbContext context, Guid userId) =>
        context.TodoItems
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.CreatedAt)
            .ThenBy(t => t.Id);

    private static async Task<Guid> SeedTodosAsync(ApplicationDbContext context, int count)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid()}@example.com",
            FirstName = "Test",
            LastName = "User",
            PasswordHash = "hash"
        };

        context.Users.Add(user);

        DateTime baseline = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (int i = 0; i < count; i++)
        {
            context.TodoItems.Add(new TodoItem
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Description = $"Todo {i}",
                Labels = [],
                IsCompleted = false,
                CreatedAt = baseline.AddMinutes(i)
            });
        }

        await context.SaveChangesAsync(CancellationToken.None);

        return user.Id;
    }

    private static ApplicationDbContext CreateContext()
    {
        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options, new NoOpDomainEventsDispatcher());
    }

    public void Dispose() => _context.Dispose();

    private sealed class NoOpDomainEventsDispatcher : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
