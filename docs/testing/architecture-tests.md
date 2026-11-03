# Architecture Tests

**EXISTING.** `tests/ArchitectureTests`, xUnit + NetArchTest + Shouldly. The only test project
shipped with this template.

## Structure

```
tests/ArchitectureTests/
├── BaseTest.cs          — shared assembly references
├── GlobalUsings.cs       — global using Xunit;
└── Layers/
    └── LayerTests.cs     — the six dependency-direction rules
```

```csharp
// tests/ArchitectureTests/BaseTest.cs
public abstract class BaseTest
{
    protected static readonly Assembly DomainAssembly = typeof(User).Assembly;
    protected static readonly Assembly ApplicationAssembly = typeof(ICommand).Assembly;
    protected static readonly Assembly InfrastructureAssembly = typeof(ApplicationDbContext).Assembly;
    protected static readonly Assembly PresentationAssembly = typeof(Program).Assembly;
}
```

Every test class resolves the four layer assemblies through `BaseTest`, using one representative
type per assembly (`User` for `Domain`, `ICommand` for `Application`, `ApplicationDbContext` for
`Infrastructure`, `Program` for `Web.Api`/Presentation) rather than hardcoding
`Assembly.Load("...")` strings. Reuse this base class for any new architecture test rather than
resolving assemblies a different way.

## The six rules

```csharp
// tests/ArchitectureTests/Layers/LayerTests.cs
[Fact]
public void Domain_Should_NotHaveDependencyOnApplication()
{
    TestResult result = Types.InAssembly(DomainAssembly)
        .Should()
        .NotHaveDependencyOn("Application")
        .GetResult();

    result.IsSuccessful.ShouldBeTrue();
}
```

Full table of all six in [architecture/dependency-rules.md](../architecture/dependency-rules.md).
Every rule follows this exact `Types.InAssembly(...).Should().NotHaveDependencyOn(...).GetResult()`
+ `result.IsSuccessful.ShouldBeTrue()` shape.

## Running

```bash
dotnet test CleanArchitecture.slnx --configuration Release --no-restore --no-build
dotnet test tests/ArchitectureTests --filter "FullyQualifiedName~LayerTests"
```

Runs as part of the same CI pipeline as everything else (`.github/workflows/build.yml`) — a
dependency-direction violation fails CI the same way a compile error would.

## Adding a new architecture rule

When a new project/layer is introduced, or an existing gap should be closed (see
[architecture/dependency-rules.md](../architecture/dependency-rules.md#the-six-enforced-rules) for
the one already-known gap — `Infrastructure ↛ Application` is not explicitly asserted today):

```csharp
[Fact]
public void InfrastructureLayer_ShouldNotHaveDependencyOn_ApplicationInternals()
{
    TestResult result = Types.InAssembly(InfrastructureAssembly)
        .Should()
        .NotHaveDependencyOn(SomeInternalNamespace)
        .GetResult();

    result.IsSuccessful.ShouldBeTrue();
}
```

Other rules worth adding as this template's structure grows, following the same pattern:

- "No type in `Web.Api` derives from `ControllerBase`" — enforces the
  [minimal-API-only convention](../api/minimal-api.md) structurally rather than only by review.
- "Every `IEndpoint` implementation is `internal sealed`" — enforces
  [api/endpoints.md](../api/endpoints.md)'s checklist structurally.
- "No broker client type (`RabbitMQ.Client.*`/`MassTransit.*`/`Wolverine.*`) is referenced from
  `Domain` or `Application`'s public surface" — if/when
  [messaging](../messaging/overview.md) is added, add this the same day the first broker package
  reference is added, not as a follow-up.

None of these three exist today — they're the natural next additions to `LayerTests.cs`, not
implied guarantees of the current test suite.
