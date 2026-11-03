# Integration / Functional Tests

**RECOMMENDED — not present in this template today.** No `WebApplicationFactory<Program>`-based
project exists. `Program.cs` already carries the shim this needs:

```csharp
// src/Web.Api/Program.cs — REMARK: Required for functional and integration tests to work.
namespace Web.Api
{
    public partial class Program;
}
```

This page describes the intended shape for when that project is added — do not implement it
speculatively as part of an unrelated task; add it as its own explicit piece of work.

## Project setup

A new `tests/FunctionalTests` (or `IntegrationTests`) xUnit project, referencing `Web.Api`, using
`Microsoft.AspNetCore.Mvc.Testing`'s `WebApplicationFactory<Program>`. One test class per
`IEndpoint`, mirroring `Endpoints/<Aggregate>/<Name>.cs` → `<Aggregate><Name>Tests.cs`.

## What to swap vs. what to keep real

**Keep real**: the actual DI registrations from `AddApplication()`/`AddInfrastructure()`/
`AddPresentation()` — real `ICommandHandler`/`IQueryHandler` resolution,
`ValidationDecorator`, `LoggingDecorator`, `CustomResults.Problem`, `GlobalExceptionHandler`. The
entire point of testing through HTTP is exercising this pipeline end-to-end, not around it —
mocking `ICommandHandler<T>` directly in a "functional" test would just be a slower unit test that
also happens to skip the decorators.

**Swap**:
- The database — point `ApplicationDbContext` at a Testcontainers-managed PostgreSQL instance (matches
  production's actual provider, catches Npgsql-specific behavior like the `DateTimeKind` UTC
  requirement described in [persistence/ef-core.md](../persistence/ef-core.md)) or, if speed
  matters more than fidelity, an EF Core InMemory/SQLite provider.
- `IDateTimeProvider`, if a test needs a fixed clock.
- JWT signing configuration (`Jwt:Secret`/`Issuer`/`Audience`), pointed at test values via
  `WebApplicationFactory.WithWebHostBuilder(...).ConfigureAppConfiguration(...)`.

## What to cover per endpoint

- **Happy path** → expected status code (`200`/`201`/`204`) and response shape (`*Response` DTO
  fields specifically — not raw entity fields, since entities are never returned directly, see
  [architecture/presentation.md](../architecture/presentation.md)).
- **Validation failure** → `400` with a FluentValidation-shaped `errors` extension on the
  `ProblemDetails` body — exercises `ValidationDecorator` + `CustomResults.Problem` together (see
  [patterns/error-handling.md](../patterns/error-handling.md)).
- **Not-found / conflict business failures** → `404`/`409` with `title`/`detail` matching the
  relevant `Domain.<Aggregate>Errors` factory.
- **Authorization**: an anonymous request to a `.RequireAuthorization()`/`.HasPermission(...)`
  endpoint → `401`. **Note**: per [api/authorization.md](../api/authorization.md), the permission
  system is currently a non-functional stub — an authenticated-but-unpermitted request to a
  `.HasPermission(...)` endpoint will **not** currently return `403`, because
  `PermissionAuthorizationHandler` unconditionally succeeds for any authenticated user. Don't
  write a test asserting `403` for that case until `PermissionProvider.GetForUserIdAsync` is
  actually implemented — the test would be asserting behavior that doesn't exist yet.

## Example shape

```csharp
// RECOMMENDED — not a real test today
public class TodosCompleteTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Complete_AlreadyCompletedTodo_ReturnsProblem()
    {
        HttpClient client = factory.CreateClient();
        // ... seed a completed TodoItem via the test database, authenticate the client ...

        HttpResponseMessage response = await client.PutAsync($"todos/{todoItemId}/complete", null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest); // ErrorType.Problem -> 400
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Title.ShouldBe("TodoItems.AlreadyCompleted");
    }
}
```

## EF Core-specific integration coverage

Beyond HTTP-level tests, a narrower EF Core integration test (against a real/test-container
Postgres, bypassing HTTP) is worth adding for anything the InMemory provider can't validate
faithfully — e.g. the `DueDate` `HasConversion` UTC-forcing behavior, the unique index on
`User.Email`, cascade delete behavior on the `TodoItem → User` foreign key. See
[persistence/ef-core.md](../persistence/ef-core.md) for what's configured today that would need
this kind of coverage.
