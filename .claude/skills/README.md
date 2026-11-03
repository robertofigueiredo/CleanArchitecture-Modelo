# Clean Architecture Agent Skills for Claude Code

A skill pack that teaches Claude Code the conventions of the Clean Architecture template — so every feature it builds looks like you wrote it: vertical-slice use cases, custom command/query handlers (no MediatR), Result-based error handling, minimal API endpoints, and full test coverage.

## What's inside

| Skill | Invoke with | What it does |
|---|---|---|
| **add-feature** | `/add-feature archive a todo item` | Scaffolds a complete vertical slice: command/query, handler, validator, endpoint, and unit + validator + integration tests. |
| **add-entity** | `/add-entity Project with a name and owner` | Adds a domain entity end to end: entity, error catalog, domain events, EF configuration, DbContext wiring, migration. |
| **add-tests** | `/add-tests CopyTodoCommand` | Backfills handler, validator, and integration tests for existing use cases. |
| **add-migration** | `/add-migration Add_Projects` | Creates and applies an EF Core migration (Infrastructure project, Web.Api as startup). |
| **add-background-job** | `/add-background-job delete expired refresh tokens hourly` | Adds a scheduled `BackgroundService` that safely resolves scoped services per run. |
| **bootstrap** | `/bootstrap Acme.Billing` | Renames the solution to your app and strips the sample Todos feature (keeps Users + auth). |
| **ca-review** | `/ca-review` | Reviews pending changes against the template's conventions: layer boundaries, error handling, security, caching, and test coverage. |

You don't have to invoke them explicitly — once installed, Claude Code picks the right skill automatically when you say things like "add an endpoint to snooze a todo."

## Installation

The skills live in `.claude/skills/`. If you cloned the template, they're already active — just open the repo in Claude Code.

To use them in another project based on this template, copy the folder:

```
your-project/
└── .claude/
    └── skills/
        ├── add-feature/
        ├── add-entity/
        ├── add-tests/
        ├── add-migration/
        ├── add-background-job/
        ├── bootstrap/
        └── ca-review/
```

Works with both the standard and the Aspire variants of the template.

## Try it

```
/add-feature snooze a todo until a given date
```

Claude will create the command, validator, handler (with ownership check, domain event, and cache invalidation), the endpoint, and the three test types — then build and run the tests.

## Customizing

Each skill is a plain Markdown file (`SKILL.md`, plus templates under `references/`). Renamed your layers, prefer records everywhere, use a different test stack? Edit the templates once and every future feature follows suit. The skills are the executable version of your team's conventions doc.

---

Built for the [Clean Architecture template](https://www.milanjovanovic.tech) by Milan Jovanović.
