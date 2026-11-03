---
name: add-migration
description: Create and apply an EF Core migration in the Clean Architecture template. Use when the user changes the model (adds or edits an entity or its configuration) and needs a migration, or asks to update the database schema.
argument-hint: <migration name or a description of the model change, e.g. "Add_Projects">
---

# Add an EF Core Migration

Migrations live in the **Infrastructure** project; **Web.Api** is the startup project (it owns the connection string and DI).

## Steps

1. Finish the model change first — entity + `IEntityTypeConfiguration` + a `DbSet` on `ApplicationDbContext` **and** `IApplicationDbContext` (see `/add-entity`). The migration reads the model, so it must compile.

2. Create the migration from the repo root:

   ```
   dotnet ef migrations add {Migration_Name} --project src/Infrastructure --startup-project src/Web.Api
   ```

   Names are `PascalCase_With_Underscores` (e.g. `Add_Projects`, `Add_RefreshTokens`).

3. Review the generated files in `src/Infrastructure/Database/Migrations/` — check the `Up`/`Down`, snake_case table/column names, and that nothing unexpected was dropped.

4. Apply it. In Development the app runs `ApplyMigrations()` on startup, so `dotnet run --project src/Web.Api` (with `docker compose up -d` running) applies it. To apply explicitly:

   ```
   dotnet ef database update --project src/Infrastructure --startup-project src/Web.Api
   ```

## Rules

- Needs the EF tool: `dotnet tool install --global dotnet-ef` if `dotnet ef` is missing.
- Never edit a migration that has already been applied/committed — add a new one.
- To drop the last **unapplied** migration: `dotnet ef migrations remove --project src/Infrastructure --startup-project src/Web.Api`.
- Commit the migration together with the model change.
