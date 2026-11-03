# Dependency Rules

These rules are not documentation-only — they are asserted by
`tests/ArchitectureTests/Layers/LayerTests.cs` via NetArchTest and run as part of `dotnet test`.
Breaking one of them fails the build.

## The six enforced rules

```csharp
// tests/ArchitectureTests/Layers/LayerTests.cs
Domain_Should_NotHaveDependencyOnApplication
DomainLayer_ShouldNotHaveDependencyOn_InfrastructureLayer
DomainLayer_ShouldNotHaveDependencyOn_PresentationLayer
ApplicationLayer_ShouldNotHaveDependencyOn_InfrastructureLayer
ApplicationLayer_ShouldNotHaveDependencyOn_PresentationLayer
InfrastructureLayer_ShouldNotHaveDependencyOn_PresentationLayer
```

In plain terms:

| # | Rule |
|---|---|
| 1 | `Domain` must not reference `Application`. |
| 2 | `Domain` must not reference `Infrastructure`. |
| 3 | `Domain` must not reference `Web.Api` (Presentation). |
| 4 | `Application` must not reference `Infrastructure`. |
| 5 | `Application` must not reference `Web.Api` (Presentation). |
| 6 | `Infrastructure` must not reference `Web.Api` (Presentation). |

`SharedKernel` is intentionally not tested against misuse — it exists specifically to be
referenced by every layer.

**REQUIRED**: `Infrastructure → Application` is not asserted by an explicit `LayerTests` rule
today, but it is the correct and expected direction (`Infrastructure` implements Application's
abstractions — e.g. `Infrastructure/Authentication/UserContext.cs` implements
`Application.Abstractions.Authentication.IUserContext`). Treat it as a real rule even though
`LayerTests` doesn't check it yet; do not add a dependency from `Application` back onto
`Infrastructure` and call it "fine because there's no test for it."

## What this means when adding code

- **New EF Core / Npgsql / ASP.NET Core hosting types** belong in `Infrastructure` or `Web.Api`
  only. Never reference `Microsoft.EntityFrameworkCore` types (`DbContext`, `DbSet<T>` as a
  concrete class, `IQueryable` extensions requiring the EF provider) from `Domain` or as part of
  `Application`'s public surface — `Application` sees EF Core only through
  `Abstractions/Data/IApplicationDbContext`.
- **New broker/messaging client types** (RabbitMQ.Client, MassTransit, Wolverine) follow the same
  rule as EF Core: wiring lives in `Infrastructure` (or a new Infrastructure-tier project), never
  in `Domain`/`Application` directly. See [messaging/overview.md](../messaging/overview.md).
- **New business logic** goes in `Domain` (entity methods, domain events) or `Application`
  (orchestration in a command/query handler) — never in `Web.Api`. An `IEndpoint` maps HTTP to a
  command/query and a `Result` to an `IResult`; it does not contain business rules.
- **Web.Api never touches `Domain` or EF Core directly.** Endpoints depend only on
  `ICommandHandler<,>` / `ICommandHandler<>` / `IQueryHandler<,>` as delegate parameters.

## How to add a new architecture test

When a new project or a new rule is introduced (e.g. asserting `Infrastructure ↛ Application`
explicitly, or that no type in `Web.Api` derives from `ControllerBase`), add a test to
`tests/ArchitectureTests/Layers/LayerTests.cs` following the existing shape:

```csharp
[Fact]
public void SomeLayer_ShouldNotHaveDependencyOn_SomeOtherLayer()
{
    TestResult result = Types.InAssembly(SomeLayerAssembly)
        .Should()
        .NotHaveDependencyOn(SomeOtherLayerAssembly.GetName().Name)
        .GetResult();

    result.IsSuccessful.ShouldBeTrue();
}
```

Assembly references (`DomainAssembly`, `ApplicationAssembly`, `InfrastructureAssembly`,
`PresentationAssembly`) are defined once in `tests/ArchitectureTests/BaseTest.cs` — reuse them,
don't hardcode `Assembly.Load(...)` calls in individual tests. See
[testing/architecture-tests.md](../testing/architecture-tests.md).
