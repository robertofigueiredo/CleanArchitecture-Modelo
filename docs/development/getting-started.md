# Getting Started

## Prerequisites

- .NET 10 SDK (the solution targets `net10.0`; see `Directory.Build.props` for how to retarget to
  `net8.0`/`net9.0` if needed — both are commented-out options in that file, with a matching note
  to also update the Dockerfile in `src/Web.Api`).
- Docker (for PostgreSQL + Seq via `docker compose`), or a local PostgreSQL instance.

## Run everything via Docker Compose

```bash
docker compose up
```

Starts:

| Service | Port(s) | Purpose |
|---|---|---|
| `web-api` | 5000 (HTTP), 5001 (HTTPS) | the API itself |
| `postgres` | 5432 | database (`clean-architecture` db, `postgres`/`postgres` credentials — dev only) |
| `seq` | 8081 | structured log viewer UI |

## Run the API locally against Docker-hosted dependencies

```bash
docker compose up postgres seq   # just the dependencies
dotnet run --project src/Web.Api
```

In `Development`, `Program.cs` automatically applies pending EF Core migrations
(`app.ApplyMigrations()`) and enables Swagger UI (`app.UseSwaggerWithUi()`) — see
[persistence/migrations.md](../persistence/migrations.md) and [api/openapi.md](../api/openapi.md).

## Configuration

`src/Web.Api/appsettings.json` ships with empty `ConnectionStrings:Database` and `Jwt:*` values —
set real values via `appsettings.Development.json` (already populated for local Docker use),
environment variables, or `dotnet user-secrets`. Never commit real secrets.

Required keys:

```json
{
  "ConnectionStrings": { "Database": "Host=...;Port=5432;Database=...;Username=...;Password=..." },
  "Jwt": { "Secret": "...", "Issuer": "...", "Audience": "...", "ExpirationInMinutes": 60 }
}
```

## Build

```bash
dotnet restore CleanArchitecture.slnx
dotnet build CleanArchitecture.slnx --configuration Release --no-restore
```

`Directory.Build.props` sets `TreatWarningsAsErrors=true` and `AnalysisMode=All` (SonarAnalyzer +
full .NET analyzers) — a warning fails the build. See
[development/coding-standards.md](./coding-standards.md).

## Test

```bash
dotnet test CleanArchitecture.slnx --configuration Release --no-restore --no-build
```

See [testing/overview.md](../testing/overview.md) — only `tests/ArchitectureTests` exists today.

## Publish

```bash
dotnet publish CleanArchitecture.slnx --configuration Release --no-restore --no-build
```

## First request

1. `POST users/register` — `{ "email": "...", "firstName": "...", "lastName": "...", "password": "..." }`.
2. `POST users/login` with the same email/password → returns a JWT.
3. Use `Authorization: Bearer <token>` on every subsequent `Todos`/`Users` request that requires
   auth (see [api/authorization.md](../api/authorization.md)).
4. `POST todos` to create a todo, `GET todos?userId=...` to list them.

Full endpoint list: `src/Web.Api/Endpoints/Todos/`, `src/Web.Api/Endpoints/Users/`, or browse
Swagger UI at `/swagger` in Development.

## Next steps

- [development/project-structure.md](./project-structure.md) — where everything lives.
- [development/feature-development.md](./feature-development.md) — how to add a new feature.
- [architecture/overview.md](../architecture/overview.md) — the five-project layering and why.
