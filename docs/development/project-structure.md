# Project Structure

```
clean-architecture/
├── CleanArchitecture.slnx        — solution file
├── Directory.Build.props          — shared MSBuild props (TFM, nullable, analyzers)
├── Directory.Packages.props       — central package version management
├── docker-compose.yml             — web-api + postgres + seq
├── .cursorrules                   — legacy Cursor rules (superseded in practice — see coding-standards.md)
│
├── src/
│   ├── SharedKernel/               — Entity, Result, Error, IDomainEvent, IDateTimeProvider
│   ├── Domain/
│   │   ├── Todos/                  — TodoItem, Priority, TodoItemErrors, *DomainEvent
│   │   └── Users/                  — User, UserErrors, UserRegisteredDomainEvent
│   ├── Application/
│   │   ├── Abstractions/
│   │   │   ├── Authentication/     — IPasswordHasher, ITokenProvider, IUserContext
│   │   │   ├── Behaviors/          — ValidationDecorator, LoggingDecorator
│   │   │   ├── Data/                — IApplicationDbContext
│   │   │   └── Messaging/           — ICommand(<T>), ICommandHandler<>/<,>, IQuery<T>, IQueryHandler<,>
│   │   ├── DependencyInjection.cs  — AddApplication()
│   │   ├── Todos/<UseCase>/         — Command/Query + Handler + Validator
│   │   └── Users/<UseCase>/
│   ├── Infrastructure/
│   │   ├── Authentication/          — TokenProvider, PasswordHasher, UserContext
│   │   ├── Authorization/           — HasPermissionAttribute, PermissionAuthorizationHandler, PermissionProvider, ...
│   │   ├── Database/
│   │   │   ├── ApplicationDbContext.cs
│   │   │   ├── Schemas.cs
│   │   │   └── Migrations/
│   │   ├── DomainEvents/            — IDomainEventsDispatcher, DomainEventsDispatcher
│   │   ├── Time/                    — DateTimeProvider
│   │   ├── Todos/ Users/             — IEntityTypeConfiguration<T>
│   │   └── DependencyInjection.cs  — AddInfrastructure()
│   └── Web.Api/
│       ├── Endpoints/
│       │   ├── IEndpoint.cs  Tags.cs
│       │   ├── Todos/  Users/       — one IEndpoint class per operation
│       ├── Extensions/               — EndpointExtensions, ResultExtensions, MigrationExtensions, ...
│       ├── Infrastructure/           — CustomResults, GlobalExceptionHandler
│       ├── Middleware/               — RequestContextLoggingMiddleware
│       ├── DependencyInjection.cs   — AddPresentation()
│       ├── Program.cs
│       └── appsettings*.json
│
├── tests/
│   └── ArchitectureTests/
│       ├── BaseTest.cs
│       └── Layers/LayerTests.cs
│
└── docs/                            — this documentation set
    ├── engineering/                 — deep per-topic architectural audits (EF Core, Minimal API, RabbitMQ, standards)
    ├── architecture/  development/  patterns/  persistence/  api/  messaging/  observability/  testing/
```

## Where new code goes

| Adding... | Goes in |
|---|---|
| A new aggregate/entity | `Domain/<Aggregate>/<Entity>.cs` + `<Entity>Errors.cs` |
| A domain event | `Domain/<Aggregate>/<Event>DomainEvent.cs` |
| A use case (command/query) | `Application/<Aggregate>/<UseCase>/` (Command/Query + Handler + Validator) |
| A response DTO | `Application/<Aggregate>/<UseCase>/<Aggregate>Response.cs` (check sibling use-case folders first — see [architecture/application.md](../architecture/application.md)) |
| A new Application-owned abstraction | `Application/Abstractions/<Area>/I<Thing>.cs` |
| The abstraction's implementation | `Infrastructure/<Area>/<Thing>.cs` |
| An entity configuration | `Infrastructure/<Aggregate>/<Entity>Configuration.cs` |
| A migration | generated via `dotnet ef migrations add` into `Infrastructure/Database/Migrations/` |
| An HTTP endpoint | `Web.Api/Endpoints/<Aggregate>/<OperationName>.cs` |
| A permission constant | `Web.Api/Endpoints/<Aggregate>/Permissions.cs` |
| An architecture rule | `tests/ArchitectureTests/Layers/LayerTests.cs` |

See [development/feature-development.md](./feature-development.md) for the full walkthrough tying
these together.

## `docs/engineering/` vs. the rest of `docs/`

`docs/engineering/*.md` (`EF_CORE_GUIDELINES.md`, `MINIMAL_API_GUIDELINES.md`,
`ENGINEERING_STANDARDS.md`, `RABBITMQ_GUIDELINES.md`) are deep, line-cited architectural audits —
every claim traceable to a specific file/line, including every known inconsistency and gap found
by review. The rest of `docs/` (this documentation set) is the practitioner-facing distillation of
those audits into task-oriented guides — read `docs/engineering/` when you need the full
justification or a complete list of deviations; read the topic pages under `docs/` for "how do I
do X in this template."
