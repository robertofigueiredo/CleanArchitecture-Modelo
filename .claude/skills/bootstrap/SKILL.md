---
name: bootstrap
description: Turn this template into a fresh project — rename the solution and (optionally) namespaces to the user's app name, and remove the sample Todos feature so they start clean. Use right after cloning when the user wants to begin their own project from the template.
argument-hint: <new project/solution name, e.g. "Acme.Billing">
---

# Bootstrap a New Project from the Template

Two jobs: **(1)** rename the template to the user's name, and **(2)** strip the sample **Todos** feature (keep Users + auth — most apps need them). Confirm the new name with the user first.

## 1. Rename

The template ships as the `CleanArchitecture` solution with a `Web.Api` app and `Domain`/`Application`/`Infrastructure`/`SharedKernel` projects.

- Rename `CleanArchitecture.slnx` → `{Name}.slnx`.
- If the user wants a namespace prefix (e.g. `Acme.Billing.Domain`), rename each project folder + `.csproj` + its root namespace + every `using`, and the `<ProjectReference>`/`InternalsVisibleTo` entries. If they're fine with the plain layer names (`Domain`, `Application`, …), skip this.
- Replace the template's hardcoded identifiers: the database name and `Jwt:Issuer`/`Audience` in `appsettings*.json`, the `POSTGRES_DB`/service names + `Host=` in `docker-compose.yml`, the `README.md` title, and any `UserSecretsId` if they want a fresh one.

## 2. Remove the Todos sample

Delete the Todos slice everywhere:

- `src/Domain/Todos/`, `src/Application/Todos/`, `src/Infrastructure/Todos/`, `src/Web.Api/Endpoints/Todos/`
- `tests/Application.UnitTests/Todos/`, `tests/IntegrationTests/Todos/`
- Remove the `TodoItems` `DbSet` from `IApplicationDbContext`, `ApplicationDbContext`, and `tests/Application.UnitTests/Abstractions/TestDbContext.cs`; delete `TodoItemConfiguration`.
- Drop the table with a migration (`/add-migration Remove_Todos`), or — for a brand-new database — delete `src/Infrastructure/Database/Migrations/`, `dotnet ef migrations add Initial`, and let it recreate.

## Rules

- Keep the whole SharedKernel/Domain/Application/Infrastructure/Web.Api structure and every cross-cutting concern (auth, caching, endpoints, rate limiting, security headers, observability) — only the *sample feature* is removed.
- Build + `dotnet test` after each stage; ArchitectureTests and the integration tests surface any dangling reference.
- Leave the `.claude/skills/` pack in place so the conventions travel with the new project.
