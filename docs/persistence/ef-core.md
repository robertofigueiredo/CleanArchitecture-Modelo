# EF Core

Full architectural review: [`docs/engineering/EF_CORE_GUIDELINES.md`](../engineering/EF_CORE_GUIDELINES.md)
(deep audit with line-level citations). This page is the practitioner-facing summary + the
"how do I..." you need day to day.

This template does **not** use a Repository, Unit of Work, or Specification layer. Application
handlers depend directly on `IApplicationDbContext` — an interface exposing `DbSet<T>` +
`SaveChangesAsync` — one handler per use case, each doing its own query and its own
`SaveChangesAsync`. This is the deliberate, existing architecture; don't introduce a repository
layer on top of it.

## `ApplicationDbContext` / `IApplicationDbContext`

```csharp
// src/Application/Abstractions/Data/IApplicationDbContext.cs — Application-facing seam
public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<TodoItem> TodoItems { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

// src/Infrastructure/Database/ApplicationDbContext.cs — concrete implementation
public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    IDomainEventsDispatcher domainEventsDispatcher)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<User> Users { get; set; }
    public DbSet<TodoItem> TodoItems { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        modelBuilder.HasDefaultSchema(Schemas.Default); // "public"
    }
}
```

`IApplicationDbContext` is what keeps EF Core out of `Application`'s public surface — the
`LayerTests` rule ("Application must not depend on Infrastructure") holds because the interface,
not the concrete `DbContext`, lives in `Application`. Registered `Scoped`:
`services.AddDbContext<ApplicationDbContext>(...)` +
`services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>())`
in `Infrastructure/DependencyInjection.cs`.

## Adding a new entity — full walkthrough

1. **Domain**: create `Domain/<Aggregate>/<Entity>.cs` — `sealed class : Entity` with public
   `{ get; set; }` properties (or behavior methods for new invariants — see
   [architecture/domain.md](../architecture/domain.md)). Add a `<Entity>Errors` static class.
2. **`IApplicationDbContext`**: add `DbSet<TEntity> TEntities { get; }` to the interface
   (`Application/Abstractions/Data/IApplicationDbContext.cs`).
3. **`ApplicationDbContext`**: add the matching `public DbSet<TEntity> TEntities { get; set; }`
   property (`Infrastructure/Database/ApplicationDbContext.cs`).
4. **Entity configuration**: create `Infrastructure/<Aggregate>/<Entity>Configuration.cs` —
   `internal sealed class : IEntityTypeConfiguration<TEntity>` — configure the key, indexes,
   relationships, conversions. Picked up automatically by
   `modelBuilder.ApplyConfigurationsFromAssembly(...)` — no manual registration.
5. **Migration**: `dotnet ef migrations add <Name> --project src/Infrastructure --startup-project
   src/Web.Api`. See [persistence/migrations.md](./migrations.md).
6. **Use cases**: add Command/Query + Handler (+ Validator) under `Application/<Aggregate>/`. See
   [development/feature-development.md](../development/feature-development.md).

## Entity configuration example

```csharp
// src/Infrastructure/Todos/TodoItemConfiguration.cs
internal sealed class TodoItemConfiguration : IEntityTypeConfiguration<TodoItem>
{
    public void Configure(EntityTypeBuilder<TodoItem> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.DueDate)
            .HasConversion(d => d != null ? DateTime.SpecifyKind(d.Value, DateTimeKind.Utc) : d, v => v);

        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId);
    }
}
```

- `HasConversion` on a nullable `DateTime` forces `DateTimeKind.Utc` — required because Npgsql's
  `timestamp with time zone` mapping rejects `DateTimeKind.Unspecified`. Apply this to any new
  nullable `DateTime`/`DateTime?` property that isn't already guaranteed UTC by construction
  (properties assigned from `IDateTimeProvider.UtcNow` don't need it — see
  `CreatedAt`/`CompletedAt`).
- `HasOne<User>().WithMany().HasForeignKey(...)` is a **foreign key with no CLR navigation
  property on either side** — deliberate; there is no `TodoItem.User` or `User.TodoItems`. Every
  query that needs related data issues its own explicit filtered query instead of `.Include(...)`.
  Follow this for new relationships unless a specific feature genuinely needs graph navigation.
- **Missing today, add explicitly for new string/decimal properties**: no `.HasMaxLength(...)` /
  `.IsRequired()` / `.HasPrecision(...)` calls exist anywhere — every `string` currently
  materializes as unbounded Postgres `text`. Nullable reference types are **not** enabled
  project-wide (see [development/coding-standards.md](../development/coding-standards.md)), so
  nullability is not inferred from C# annotations. If a new property needs a length/precision
  bound, set it explicitly in the configuration — don't rely on convention.
- **No concurrency token** (`[Timestamp]`/`IsRowVersion()`/`IsConcurrencyToken()`) exists on any
  entity — see Transactions/Concurrency below before assuming optimistic concurrency protection.

## Queries — tracking, `AsNoTracking`, projections

Three consistent patterns, pick based on what the handler does with the entity:

1. **Query handlers returning a `*Response` DTO project directly** — `.Select(...)` into the DTO,
   no `.AsNoTracking()` needed (a projection never materializes a tracked entity instance):

   ```csharp
   List<TodoResponse> todos = await context.TodoItems
       .Where(todoItem => todoItem.UserId == query.UserId)
       .Select(todoItem => new TodoResponse { Id = todoItem.Id, /* ... */ })
       .ToListAsync(cancellationToken);
   ```

2. **Command handlers that read an entity purely to validate/copy from it, without mutating it,
   call `.AsNoTracking()` explicitly** (e.g. the `Users` lookup in `Todos/Create`):

   ```csharp
   User? user = await context.Users.AsNoTracking()
       .SingleOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
   ```

3. **Command handlers that read an entity in order to mutate it rely on default tracking** — fetch
   with no `.AsNoTracking()`, mutate properties directly, `SaveChangesAsync` — EF Core detects the
   changes automatically, no explicit `context.TodoItems.Update(...)` call needed or used:

   ```csharp
   TodoItem? todoItem = await context.TodoItems
       .SingleOrDefaultAsync(t => t.Id == command.TodoItemId && t.UserId == userContext.UserId, cancellationToken);
   todoItem.IsCompleted = true;
   await context.SaveChangesAsync(cancellationToken);
   ```

No global tracking behavior override exists (`ChangeTracker.QueryTrackingBehavior` is left at the
EF Core default). Pick the pattern above matching your handler's intent, don't set
`AsNoTrackingWithIdentityResolution()` or a global override without a specific reason.

## Relationships / N+1

There is exactly one relationship in the model today (`TodoItem` → `User`), with no navigation
property either direction — so `.Include(...)` is never used anywhere, and lazy-loading proxies
are not installed. If a new relationship genuinely needs graph traversal, add a navigation
property deliberately and use `.Include(...)`/`.Select(...)` projections — don't enable
lazy-loading proxies (would reintroduce silent N+1 risk this template currently structurally
avoids).

## Transactions / concurrency

- No explicit `BeginTransaction`/execution strategy anywhere — every command handler is "one read
  (optional) + one mutation + one `SaveChangesAsync`", which Npgsql wraps in an implicit single
  transaction. **Exactly one `SaveChangesAsync` call per command handler** — keep new handlers to
  this shape; if a use case needs more than one, it's doing too much for one handler.
- **No optimistic concurrency tokens exist.** Two concurrent updates to the same row last-write-
  wins with no `DbUpdateConcurrencyException`. If a new feature has a real concurrent-write risk,
  add a rowversion/timestamp column via `.IsRowVersion()` in that entity's configuration — this is
  not the default posture today.
- No `EnableRetryOnFailure` — transient Postgres connection failures are not automatically
  retried.

## `CancellationToken`

Threaded consistently end-to-end in every existing handler: endpoint delegate →
`handler.Handle(command, cancellationToken)` → every EF async call. Do the same in new handlers —
no sync/blocking EF Core call (`.ToList()`, `.SaveChanges()`) anywhere; only the async,
cancellable overloads.

## Client-generated keys

`Guid` is the PK type everywhere. Explicitly assign `Id = Guid.NewGuid()` before `Add(...)` in
every new command handler that creates an entity — matching `Todos/Create` and `Users/Register`
(don't rely on EF Core's store-generation-on-`Guid.Empty` convention, which one existing handler,
`Todos/Copy`, does inconsistently — that's drift, not the pattern to copy).

## Performance / N+1 checklist for a new query

- Project with `.Select(...)` into a DTO rather than returning tracked entities.
- Filter (`.Where(...)`) before projecting, not after materializing a list in memory.
- Don't call `.ToList()`/`.ToListAsync()` and then `.Where(...)` in C# — push the filter into the
  query.
- No navigation properties exist to `.Include()` today; if you add one, watch for N+1 the same way
  you would with any EF Core project (project or explicitly `.Include()`, never loop-and-query).
