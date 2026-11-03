# EF Core Guidelines

Audit of the current Entity Framework Core implementation in this project (PostgreSQL via
Npgsql). Source of truth: `CLAUDE.md` plus the code under `src/Infrastructure/Database`,
`src/Application/Abstractions/Data`, and every Application command/query handler that touches
`IApplicationDbContext`. This document is descriptive/prescriptive only — **no code was changed
to produce it.**

This project deliberately does **not** use a Repository, Unit of Work, or Specification layer —
Application handlers depend on `IApplicationDbContext` (an interface exposing `DbSet<T>` +
`SaveChangesAsync`) directly, one handler per use case, each doing its own query and its own
`SaveChangesAsync`. That is the existing, intentional architecture (CQRS handlers as the
transaction boundary). This document works within it and does not propose replacing it.

---

## EXISTING — what's actually implemented today

### `ApplicationDbContext` / `IApplicationDbContext`

- `src/Infrastructure/Database/ApplicationDbContext.cs`: `sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IDomainEventsDispatcher domainEventsDispatcher) : DbContext(options), IApplicationDbContext` — primary-constructor DI, two dependencies only (options + dispatcher).
- Exposes `DbSet<User> Users` and `DbSet<TodoItem> TodoItems` with public setters (EF Core doesn't require the setter; it's there but unused by EF itself — `Set<T>()` is what the framework actually uses internally, the auto-property is just the public surface).
- `IApplicationDbContext` (`src/Application/Abstractions/Data/IApplicationDbContext.cs`) is the Application-facing seam: it re-exposes the same two `DbSet<T>` properties (read-only, `{ get; }`) plus `SaveChangesAsync(CancellationToken)`. This is what keeps `Application` decoupled from `Microsoft.EntityFrameworkCore.*`-as-EF-Core-provider concerns while still allowing full LINQ-over-`DbSet` query composition — the `LayerTests` rule ("Application must not depend on Infrastructure") is satisfied because the interface, not the concrete `DbContext`, lives in Application.
- Registered as `Scoped` (`services.AddDbContext<ApplicationDbContext>(...)` + `services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>())` in `Infrastructure/DependencyInjection.cs`) — correct default for a request-driven ASP.NET Core app; no background services or long-lived consumers exist in this codebase that would need `IDbContextFactory<T>`.
- `OnModelCreating` calls `modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly)` (auto-discovers `IEntityTypeConfiguration<T>` classes) and `modelBuilder.HasDefaultSchema(Schemas.Default)` (`"public"`).
- `UseNpgsql(connectionString, npgsqlOptions => npgsqlOptions.MigrationsHistoryTable(...))` +`.UseSnakeCaseNamingConvention()` (EFCore.NamingConventions) — all table/column/index names in migrations are snake_case (`todo_items`, `user_id`, `ix_users_email`), matching Postgres convention.

### Entity configurations

- `Infrastructure/Todos/TodoItemConfiguration.cs`: PK on `Id`; a `HasConversion` on `DueDate` that forces `DateTimeKind.Utc` on read/write (required because Npgsql's `timestamp with time zone` mapping rejects `DateTimeKind.Unspecified`); `HasOne<User>().WithMany().HasForeignKey(t => t.UserId)` — an FK **without a CLR navigation property on either side** (no `TodoItem.User`, no `User.TodoItems` collection).
- `Infrastructure/Users/UserConfiguration.cs`: PK on `Id`; `HasIndex(u => u.Email).IsUnique()`.
- No `.HasMaxLength(...)` / `IsRequired()` / `.HasPrecision(...)` calls anywhere — every `string` column materializes as unbounded Postgres `text`, and nullability is inferred purely from the CLR property's nullability (C# nullable reference types are **not** enabled project-wide — see `RECOMMENDED`).
- No concurrency token (`[Timestamp]` / `IsRowVersion()` / `IsConcurrencyToken()`) on any entity.

### Migrations

- Single migration to date: `20240811190111_Create_Database.cs`, generated (not hand-written) — creates `public.users`, `public.todo_items`, the FK with `onDelete: ReferentialAction.Cascade`, `ix_todo_items_user_id`, and the unique `ix_users_email`.
- Applied via `dbContext.Database.Migrate()` in `Web.Api/Extensions/MigrationExtensions.cs`, called from `Program.cs` **only** `if (app.Environment.IsDevelopment())`. There is no dedicated migration worker/service and no automatic migration in non-Development environments — migrations against staging/production are an out-of-band operator action today (`dotnet ef database update`, per `CLAUDE.md`).

### PostgreSQL specifics

- `Labels` (`List<string>` on `TodoItem`) maps straight to Postgres `text[]` — no separate join table, no value converter needed; this is Npgsql's native array support.
- `DueDate`/`CreatedAt`/`CompletedAt` map to `timestamp with time zone`; the `HasConversion` on `DueDate` is the only place `DateTimeKind` is normalized — `CreatedAt`/`CompletedAt` are always assigned from `IDateTimeProvider.UtcNow` (`Infrastructure/Time/DateTimeProvider.cs`) so they're UTC by construction and don't need the same conversion.
- No `EnableRetryOnFailure` / execution-strategy configuration on `UseNpgsql(...)` — transient network/connection failures against Postgres are not automatically retried.

### Relationships / `Include` / N+1

- There is exactly one relationship in the model (`TodoItem` → `User` via `user_id`), and it has **no navigation property in either direction**. Consequently **`Include` is never used anywhere in the codebase** — there is nothing to eager/lazy-load. Lazy-loading proxies are not installed/enabled, so accidental lazy-load N+1 is structurally impossible today.
- Every query that needs "does this user exist" or "what todo is this" issues its own explicit, filtered query (see handler examples below) rather than navigating a relationship — this is a deliberate consequence of the FK-without-navigation modeling choice, not an oversight.

### Tracking / `AsNoTracking` / projections

No global tracking behavior is configured (`ChangeTracker.QueryTrackingBehavior` is left at its
EF Core default, **tracking**). Three distinct, consistent patterns exist across handlers:

1. **Query handlers that return `*Response` DTOs project directly with `.Select(...)`**
   (`Todos/Get`, `Todos/GetById`, `Users/GetById`, `Users/GetByEmail`) — no `.AsNoTracking()`
   call, and none is needed: projecting into a non-entity shape means the query never
   materializes tracked entity instances in the first place; the change tracker has nothing to
   track.
2. **Command handlers that read an entity purely to validate/copy from it, without mutating
   it, call `.AsNoTracking()` explicitly** — `Users` lookups in `Todos/Create` and `Todos/Copy`,
   the `TodoItems` lookup in `Todos/Copy`, and the `Users` lookup in `Users/Login`. This avoids
   change-tracker overhead for entities that are read-only within that handler.
3. **Command handlers that read an entity in order to mutate it rely on default tracking**
   (`Todos/Complete`, `Todos/Update`) — they fetch via `SingleOrDefaultAsync` with no
   `.AsNoTracking()`, mutate properties directly on the returned instance, then call
   `SaveChangesAsync()`. Because tracking is the EF Core default and was never disabled, EF
   detects the property changes automatically — there is no `context.TodoItems.Update(...)`
   call anywhere, and none is needed for this pattern to work.

`Todos/Delete` mixes patterns 3 and an explicit `Remove`: it tracks-fetches the entity (needed
so `todoItem.Raise(...)` can be called on the real instance before deletion) then calls
`context.TodoItems.Remove(todoItem)` before `SaveChangesAsync()`.

### `SaveChanges` / `SaveChangesAsync` / domain events

- Only the async, cancellable overload is ever called — `SaveChangesAsync(cancellationToken)`.
  Synchronous `SaveChanges()` is not used anywhere in the codebase.
- `ApplicationDbContext` overrides `SaveChangesAsync` to extract each tracked `Entity`'s
  `DomainEvents`, clear them, call `base.SaveChangesAsync`, then dispatch the extracted events
  via `IDomainEventsDispatcher` — **after** the transaction commits (documented in-code as a
  deliberate before/after tradeoff comment: eventual consistency, handlers can fail
  independently of the write). There is no outbox table; if `DispatchAsync` throws or a handler
  fails, the `SaveChanges` write has already committed and the exception propagates up to
  `GlobalExceptionHandler` as an unhandled exception — the caller sees a 500, but the underlying
  data change is not rolled back.
- Exactly one `SaveChangesAsync` call per command handler — every write path is a single
  round trip / single implicit transaction; no handler spans multiple `SaveChangesAsync` calls.

### Transactions / concurrency

- No explicit `BeginTransaction`/`ExecutionStrategy` usage anywhere — every command handler is
  "one read (optional) + one mutation + one `SaveChangesAsync`", which Npgsql already wraps in an
  implicit single transaction. This matches the actual current use cases (no multi-aggregate,
  multi-`SaveChanges` operations exist).
- No optimistic concurrency tokens exist on `User` or `TodoItem`. Two concurrent updates to the
  same row (e.g. two `Complete` requests, or `Update` racing `Complete`) last-write-wins with no
  `DbUpdateConcurrencyException` ever thrown, because there's no rowversion/timestamp column for
  EF Core to check.

### `CancellationToken`

Threaded consistently end-to-end: endpoint delegate → `handler.Handle(command, cancellationToken)`
→ every EF async call (`ToListAsync`, `SingleOrDefaultAsync`, `AnyAsync`, `SaveChangesAsync`) in
every handler and query. No handler drops the token or uses a sync/blocking EF call.

### Client-generated keys

- `Guid` is the PK type everywhere (per the project's stated convention). `Todos/Create` and
  `Users/Register` explicitly assign `Id = Guid.NewGuid()` before `Add(...)`.
- `Todos/Copy`'s `copiedTodoItem` does **not** set `Id` explicitly — it relies on EF Core's
  built-in convention for `Guid` key properties (store-generates a value when the property is
  left at its CLR default, `Guid.Empty`, at insert time). This works correctly today but is
  inconsistent with the explicit-assignment style used everywhere else — see RECOMMENDED.

---

## REQUIRED

1. All new Application-layer database access **must** go through `IApplicationDbContext`
   (constructor-injected), never a concrete `ApplicationDbContext`, `DbContext`, or a
   newly-introduced repository/UoW abstraction — this is the existing, intentional seam that
   keeps Application decoupled from Infrastructure per `LayerTests`.
2. Every EF Core call **must** be async (`ToListAsync`, `SingleOrDefaultAsync`,
   `FirstOrDefaultAsync`, `AnyAsync`, `SaveChangesAsync`, …) and **must** take and forward the
   handler's `CancellationToken`. Synchronous EF Core calls (`ToList()`, `SaveChanges()`, `.Find()`
   without async) are not permitted in request-handling code paths.
3. New entity configurations **must** be `IEntityTypeConfiguration<T>` classes under
   `Infrastructure/<Aggregate>/` and picked up by the existing
   `ApplyConfigurationsFromAssembly(...)` scan — no per-entity fluent config inline in
   `OnModelCreating`, no Data Annotations on Domain entities (Domain must stay
   persistence-ignorant per the layer rules).
4. New migrations **must** be generated with `dotnet ef migrations add ... --project
   src/Infrastructure --startup-project src/Web.Api` (see `CLAUDE.md`) and applied with
   `dotnet ef database update`. Never hand-edit a generated migration's `Up()`/`Down()` bodies,
   never hand-edit `ApplicationDbContextModelSnapshot.cs`, never delete/rename an already-applied
   migration file.
5. Query handlers (`IQueryHandler<,>`) that return a response DTO **must** project with
   `.Select(...)` into that DTO rather than materializing the entity and mapping in memory —
   this is the existing pattern in every query handler and is what makes explicit
   `AsNoTracking()` unnecessary for them.
6. Command handlers that load an entity **only** to read from it (not mutate it) **must** call
   `.AsNoTracking()` on that query, matching `Todos/Create`, `Todos/Copy`, `Users/Login`.
7. Command handlers that load an entity in order to mutate it **must not** call
   `.AsNoTracking()` on that load, and **must not** add a redundant `context.Set<T>().Update(...)`
   call afterward — the project relies on default change tracking for in-place mutation
   (`Todos/Complete`, `Todos/Update`); mixing `AsNoTracking()` with in-place mutation is a bug
   (silent no-op on `SaveChangesAsync`), and calling `Update()` on an already-tracked entity is
   redundant noise.
8. Any new relationship **must** decide navigation properties deliberately — don't add a
   navigation property "for convenience" without also deciding its `Include`/loading strategy;
   the current model's FK-without-navigation approach is intentional (it structurally prevents
   N+1 and lazy-load surprises) and should be the default for new relationships unless a
   concrete use case needs traversal.

## RECOMMENDED

- **Enable nullable reference types** (`<Nullable>enable</Nullable>`) at least on `Domain` and
  `Infrastructure`, and back required `string` properties with `.IsRequired()` in configurations
  where the CLR type doesn't already convey it — today, e.g., `TodoItem.Description` and
  `User.Email` are non-nullable `string` in a nullable-disabled project, so EF Core's convention
  happens to infer `NOT NULL` correctly, but this is fragile: it depends on nobody flipping
  nullable-context or adding a `?` casually.
- **Add `HasMaxLength(...)` to configurations that mirror existing FluentValidation limits** —
  e.g. `TodoItemConfiguration` for `Description` (validator already enforces
  `MaximumLength(255)` in `CreateTodoCommandValidator`) and `UserConfiguration` for `Email`.
  Right now the DB column is unbounded `text` while the validator enforces a limit only at the
  Application boundary; a direct DB write (migration script, admin tooling, future
  bulk-import) would have no matching guardrail.
- **Add an optimistic concurrency token** (`byte[] RowVersion` / Postgres `xmin` mapped via
  `.IsRowVersion()`) to `TodoItem` and `User` if/when concurrent-edit correctness starts to
  matter (e.g. once collaborative editing or an admin UI exists) — not needed for the current
  single-user-owns-their-todos access pattern, but worth flagging since nothing currently
  detects a lost update.
- **Assign `Id = Guid.NewGuid()` explicitly** in `Todos/Copy`'s `copiedTodoItem` initializer,
  matching `Todos/Create` and `Users/Register`, instead of relying on EF Core's implicit
  Guid-default store-generation convention — same runtime result, but removes the inconsistency
  and makes the id source obvious without needing to know the convention.
- **Consider `EnableRetryOnFailure()` on `UseNpgsql(...)`** for resilience against transient
  connection blips (container restarts, network hiccups), wrapped through
  `Database.CreateExecutionStrategy()` if any handler ever grows a multi-step
  read/compute/write flow. Not urgent today since every handler is a single `SaveChangesAsync`,
  but worth planning for before adding anything that spans multiple round trips.
- **Consider a dedicated migration-apply step for non-Development environments** (a startup
  job, an init container, or a manual `dotnet ef database update` gate in the deploy pipeline)
  since today `app.ApplyMigrations()` only runs in Development — this is fine as-is (avoids
  multiple app instances racing to migrate in production) but should be a conscious deployment
  decision, not an implicit gap.
- **Revisit `IApplicationDbContext` exposing raw `DbSet<T>`** only if handlers start
  duplicating the same non-trivial query logic across multiple use cases — at that point a
  small, targeted extension method (not a repository) on `IQueryable<TodoItem>`/`DbSet<TodoItem>`
  is preferable to introducing a Repository interface. Not needed today; every current query is
  a one-off, use-case-specific shape.

## FORBIDDEN

- Introducing a `Repository<T>` / `IRepository<T>`, Unit of Work, or Specification pattern
  layer. `IApplicationDbContext` + direct LINQ in each handler **is** the data-access pattern
  for this project; wrapping it in another abstraction adds indirection without the
  multi-datastore or swappable-persistence need that would justify it.
- Injecting `ApplicationDbContext` (the concrete class) or any `Microsoft.EntityFrameworkCore`
  type into `Application` — always go through `IApplicationDbContext`. Referencing EF Core
  types from `Application` would violate the `ApplicationLayer_ShouldNotHaveDependencyOn_*`
  rules in `tests/ArchitectureTests/Layers/LayerTests.cs` in spirit even where it might not
  literally trip the assembly-reference check.
- Calling `.AsNoTracking()` on an entity you are about to mutate and save — silently defeats
  `SaveChangesAsync` (no `Update()` call exists anywhere in this codebase to compensate for it).
- Hand-editing a generated migration file's `Up()`/`Down()` methods or the
  `ApplicationDbContextModelSnapshot.cs` — always regenerate via `dotnet ef migrations add`/
  `remove`.
- Loading entire entity collections into memory to filter/aggregate in C# where the same
  result can be expressed in the LINQ query (`.Where(...)` /`.Select(...)` pushed to Postgres) —
  none of the current handlers do this; keep it that way.
- Adding `.Include(...)` calls that create navigation properties purely to avoid writing a
  second query, without considering `AsSplitQuery()`/cartesian-explosion implications — not a
  concern today (no navigations exist), but the first `Include` introduced into this codebase
  should come with a deliberate split-vs-single-query decision, not the untyped default.
- Catching and swallowing `DbUpdateException`/`DbUpdateConcurrencyException` to "make the error
  go away" — if concurrency tokens are added per RECOMMENDED, a concurrency conflict should
  surface as a `Result.Failure` with a `Conflict` `ErrorType` (consistent with
  `CustomResults.Problem`'s existing 409 mapping), not be silently retried or ignored.

## EXAMPLES

**Read-only projection query (no tracking needed)** — pattern used by every `IQueryHandler`:

```csharp
List<TodoResponse> todos = await context.TodoItems
    .Where(todoItem => todoItem.UserId == query.UserId)
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
    .ToListAsync(cancellationToken);
```

**Read-only entity load inside a command handler (explicit `AsNoTracking`)** — pattern used by
`Todos/Create`, `Todos/Copy`, `Users/Login`:

```csharp
User? user = await context.Users.AsNoTracking()
    .SingleOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
```

**Mutating entity load (default tracking, no `Update()` call)** — pattern used by
`Todos/Complete`, `Todos/Update`:

```csharp
TodoItem? todoItem = await context.TodoItems
    .SingleOrDefaultAsync(t => t.Id == command.TodoItemId && t.UserId == userContext.UserId, cancellationToken);

if (todoItem is null)
{
    return Result.Failure(TodoItemErrors.NotFound(command.TodoItemId));
}

todoItem.IsCompleted = true;
todoItem.CompletedAt = dateTimeProvider.UtcNow;
todoItem.Raise(new TodoItemCompletedDomainEvent(todoItem.Id));

await context.SaveChangesAsync(cancellationToken); // tracked change is picked up automatically
```

**Insert** — pattern used by `Todos/Create`, `Users/Register`:

```csharp
var todoItem = new TodoItem { Id = Guid.NewGuid(), UserId = user.Id, /* ... */ };
todoItem.Raise(new TodoItemCreatedDomainEvent(todoItem.Id));
context.TodoItems.Add(todoItem);
await context.SaveChangesAsync(cancellationToken);
```

**Delete** — pattern used by `Todos/Delete`:

```csharp
TodoItem? todoItem = await context.TodoItems
    .SingleOrDefaultAsync(t => t.Id == command.TodoItemId && t.UserId == userContext.UserId, cancellationToken);

if (todoItem is null)
{
    return Result.Failure(TodoItemErrors.NotFound(command.TodoItemId));
}

context.TodoItems.Remove(todoItem);
todoItem.Raise(new TodoItemDeletedDomainEvent(todoItem.Id));
await context.SaveChangesAsync(cancellationToken);
```

**Entity configuration with a UTC-safe date conversion and an FK without a navigation
property** — `TodoItemConfiguration`:

```csharp
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

## PERFORMANCE

What's already correct:

- **No N+1 risk exists structurally** — no navigation properties, no `Include`, no lazy-loading
  proxies installed. Every "related data" need is served by an explicit, filtered query.
- **Query handlers project directly to DTOs**, so Postgres only returns the columns actually
  needed for the response, and the change tracker never materializes entity instances for
  read-only paths — the cheapest shape for a read-heavy CQRS query.
- **Explicit `AsNoTracking()`** on read-then-discard entity loads inside command handlers avoids
  change-tracker snapshot overhead for data that's never going to be saved.
- **Single round trip per handler** — one query (at most two, e.g. user-exists + todo-lookup in
  `Copy`) plus one `SaveChangesAsync`; no chatty per-item loops anywhere.
- **Postgres native array (`text[]`) for `Labels`** avoids a join table + N+1 for what would
  otherwise be a one-to-many "tags" relationship.

What to watch as the project grows (not urgent fixes, just where the next bottleneck would come
from):

- `Todos/Get` (`GetTodosQueryHandler`) has no pagination — it loads every todo for a user in one
  query. Fine at current scale; add `.Skip()/.Take()` (or keyset pagination) before this becomes
  a "list everything" endpoint over large per-user datasets.
- No `AsSplitQuery()`/`AsSingleQuery()` decision exists yet because no query has more than one
  `Include` — revisit this guidance the moment a query needs two related collections at once.
- No bulk `ExecuteUpdateAsync`/`ExecuteDeleteAsync` usage exists because no current use case
  needs a multi-row update/delete (`Complete`/`Update`/`Delete` are always single-entity,
  ownership-checked operations) — reach for `ExecuteUpdateAsync`/`ExecuteDeleteAsync` instead of
  a load-then-loop-then-save pattern if a bulk operation (e.g. "archive all completed todos
  older than X") is ever added, rather than materializing and mutating a list.
- `EnableRetryOnFailure` is not configured — under transient Postgres connectivity issues (e.g.
  container restarts in `docker-compose.yml`), a request fails outright today rather than
  retrying; low risk for a dev-oriented template, worth reconsidering for a production
  deployment.

## TESTING

- There is currently **no** EF Core / database-backed test project in the repository —
  `tests/ArchitectureTests` only asserts assembly-dependency layering (`LayerTests.cs`) and does
  not touch `ApplicationDbContext`, migrations, or query behavior at all.
- `CLAUDE.md`'s documented `dotnet ef` commands are the only current way to validate a migration
  (`dotnet ef migrations add`, `dotnet ef database update` against the `docker-compose.yml`
  Postgres instance) — there's no automated migration-applies-cleanly check in CI
  (`.github/workflows/build.yml` runs build/test/publish, not `dotnet ef database update`).

Recommended shape when EF Core tests are introduced (documented for future work — not
implemented here):

- Prefer a **real PostgreSQL instance** (Testcontainers, or the existing `docker-compose.yml`
  `postgres` service) over the EF Core in-memory provider for anything exercising queries,
  constraints, or migrations — the in-memory provider doesn't enforce the unique index on
  `users.email`, doesn't validate the `text[]`/`timestamp with time zone` mappings, and doesn't
  catch provider-specific translation failures (e.g. an unsupported LINQ pattern against
  Npgsql), all of which are load-bearing in this schema.
- Test through `IApplicationDbContext` / the real command & query handlers (via
  `AddApplication()` + `AddInfrastructure()` wired against the test Postgres connection string),
  not by asserting on `ApplicationDbContext` internals directly — this exercises the same
  tracking/`AsNoTracking`/projection choices documented above end-to-end, which is where a
  tracking mistake (e.g. someone adding `AsNoTracking()` to a mutating load) would actually be
  caught.
- Add a migration-integrity check: spin up a fresh Postgres container, run
  `dbContext.Database.Migrate()`, and assert it succeeds — cheap, and it's the only thing that
  currently guards against a migration that only "works on my machine" because the local DB
  already has manually-applied schema drift.
- If concurrency tokens are added (see RECOMMENDED), add a test that updates the same row from
  two loaded instances and asserts a `Result.Failure` with a `Conflict` error rather than
  silent last-write-wins — there is nothing today that would catch a regression here since no
  such token exists yet.
