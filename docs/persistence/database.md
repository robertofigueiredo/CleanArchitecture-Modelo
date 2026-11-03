# Database

PostgreSQL via Npgsql. Local development is provisioned by `docker-compose.yml`:

```yaml
postgres:
  image: postgres:17
  environment:
    - POSTGRES_DB=clean-architecture
    - POSTGRES_USER=postgres
    - POSTGRES_PASSWORD=postgres
  volumes:
    - ./.containers/db:/var/lib/postgresql/data
  ports:
    - 5432:5432
```

`docker compose up` also starts `web-api` (ports 5000/5001) and `seq` (port 8081, see
[observability/logging.md](../observability/logging.md)).

## Connection string & configuration

`ConnectionStrings:Database` in `src/Web.Api/appsettings.json` (shipped empty — set via
environment variable, user secrets, or `appsettings.Development.json` locally; never commit a
real connection string). Read in `Infrastructure/DependencyInjection.cs`:

```csharp
string? connectionString = configuration.GetConnectionString("Database");
services.AddDbContext<ApplicationDbContext>(options => options
    .UseNpgsql(connectionString, npgsqlOptions =>
        npgsqlOptions.MigrationsHistoryTable(HistoryRepository.DefaultTableName, Schemas.Default))
    .UseSnakeCaseNamingConvention());
```

Health check: `AddHealthChecks().AddNpgSql(configuration.GetConnectionString("Database")!)`,
exposed at the `health` endpoint (`app.MapHealthChecks("health", ...)` in `Program.cs`).

## Naming convention

`EFCore.NamingConventions`'s `.UseSnakeCaseNamingConvention()` converts every table/column/index
name to snake_case automatically — `TodoItems` → `todo_items`, `UserId` → `user_id`,
`ix_users_email`. **Do not hand-roll snake_case names** in entity configurations (no
`.ToTable("todo_items")`, no `.HasColumnName("user_id")`) — the convention does this for every
new entity/property for free; only override it if a name genuinely needs to differ from the
mechanical conversion.

## Schema

Single schema, `"public"`, set once via `modelBuilder.HasDefaultSchema(Schemas.Default)`
(`Infrastructure/Database/Schemas.cs` — `internal static class Schemas { public const string
Default = "public"; }`). Add new schema constants here rather than hardcoding string literals if a
new project needs a second schema.

## PostgreSQL-specific mapping

- `List<string>` (`TodoItem.Labels`) maps straight to Postgres `text[]` — Npgsql's native array
  support, no join table, no value converter. Use the same approach for any new
  `List<primitive>` property.
- `DateTime`/`DateTime?` map to `timestamp with time zone`. Properties assigned from
  `IDateTimeProvider.UtcNow` are UTC by construction and need no conversion; a nullable `DateTime`
  that can be supplied by a caller (like `TodoItem.DueDate`) needs the `HasConversion` UTC-forcing
  pattern shown in [persistence/ef-core.md](./ef-core.md#entity-configuration-example) — Npgsql
  rejects `DateTimeKind.Unspecified`.

## Migration history table

`npgsqlOptions.MigrationsHistoryTable(HistoryRepository.DefaultTableName, Schemas.Default)` keeps
EF Core's `__EFMigrationsHistory` table in the `public` schema alongside application tables —
don't move it without updating this call.
