---
name: add-entity
description: Add a new domain entity to the Clean Architecture template — entity class, error catalog, domain events, EF Core configuration, DbContext wiring, and migration. Use when the user asks to add an entity, aggregate, domain model, or table.
argument-hint: <entity description, e.g. "Project with a name, owner, and list of todos">
---

# Add a Domain Entity

Create a new entity and wire it through every layer, following the `TodoItem` pattern.

## Files to create/modify

1. **Entity** — `src/Domain/{Feature}/{Entity}.cs`

```csharp
using SharedKernel;

namespace Domain.Projects;

public sealed class Project : Entity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

`sealed class`, inherits `Entity` (gives it `DomainEvents` + `Raise(...)`), `Guid Id`, plain settable properties, collections initialized with `= [];`.

2. **Error catalog** — `src/Domain/{Feature}/{Entity}Errors.cs`

```csharp
using SharedKernel;

namespace Domain.Projects;

public static class ProjectErrors
{
    public static Error NotFound(Guid projectId) => Error.NotFound(
        "Projects.NotFound",
        $"The project with the Id = '{projectId}' was not found");
}
```

Codes are `"{FeaturePlural}.{Reason}"`. Pick the factory by semantics: `Error.NotFound` (404), `Error.Conflict` (409), `Error.Problem` (400), `Error.Failure` (500).

3. **Domain events** — one record per file, `src/Domain/{Feature}/{Entity}{PastTenseVerb}DomainEvent.cs`

```csharp
using SharedKernel;

namespace Domain.Projects;

public sealed record ProjectCreatedDomainEvent(Guid ProjectId) : IDomainEvent;
```

Create at minimum the `Created` event; add others as commands need them. Events carry ids, not entities.

4. **EF configuration** — `src/Infrastructure/{Feature}/{Entity}Configuration.cs`

```csharp
using Domain.Projects;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Projects;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.HasKey(p => p.Id);

        builder.HasOne<User>().WithMany().HasForeignKey(p => p.OwnerId);
    }
}
```

Relationships are configured shadow-style (`HasOne<User>().WithMany()`) — entities hold foreign-key ids, not navigation properties. Configurations are picked up automatically by `ApplyConfigurationsFromAssembly`.

5. **DbContext wiring** — add `DbSet<{Entity}> {Plural}` to **both**:
   - `src/Application/Abstractions/Data/IApplicationDbContext.cs`
   - `src/Infrastructure/Database/ApplicationDbContext.cs`

   Also add the `DbSet` to `tests/Application.UnitTests/Abstractions/TestDbContext.cs` so handlers stay unit-testable.

6. **Migration** — from the repo root:

```
dotnet ef migrations add Add_{Plural} --project src/Infrastructure --startup-project src/Web.Api
```

Migration names are `PascalCase_With_Underscores` (see `Add_RefreshTokens`).

## Rules

- The Domain project references only `SharedKernel` — no EF, no Application types. Persistence concerns (keys, conversions, relationships) live exclusively in the Infrastructure configuration.
- Run `dotnet build` and `dotnet test` when done — `ArchitectureTests` enforce the layer rules.
- If the user also wants use cases for the entity, continue with the `add-feature` skill.
