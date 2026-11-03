# API Response & Pagination Guidelines

Defines this template's HTTP response envelope and pagination pattern, inspired by (not a
compliance implementation of) the [Open Banking Brasil Fase 1](https://openbanking-brasil.github.io/areadesenvolvedor-fase1/)
payload/pagination/error conventions. Source of truth for the code: `SharedKernel/Result.cs`,
`SharedKernel/Error.cs`, `SharedKernel/ErrorType.cs`, `Application/Abstractions/Pagination/*`,
`Web.Api/Contracts/*`, `Web.Api/Infrastructure/CustomResults.cs`,
`Web.Api/Infrastructure/ApiResponseFactory.cs`, `Web.Api/Infrastructure/PaginationLinkBuilder.cs`,
`Web.Api/Infrastructure/GlobalExceptionHandler.cs`, and the reference feature
`Application/Todos/Get/*` + `Web.Api/Endpoints/Todos/Get.cs`. This document is
descriptive/prescriptive; it does not repeat the general CQRS/Result/error-handling material in
[`docs/patterns/`](../patterns) — see those pages for the mechanics this pattern builds on top of.

**Scope decision, stated up front**: this template does **not** implement Open Banking Brasil's
regulated APIs, OAuth/FAPI security profile, consent flows, or resource-specific payloads. It
borrows only the *structural* conventions — envelope shape, links, meta, error array — that are
generically useful for any .NET API, and adapts them to this template's existing `Result`/`Error`
pattern rather than replacing it.

---

## EXISTING vs. REQUIRED vs. RECOMMENDED — how to read this document

- **EXISTING** — implemented in this template today, described as it is.
- **REQUIRED** — the rule every new feature that adopts this pattern must follow.
- **RECOMMENDED** — a direction to prefer when there's a choice, not enforced by any test/analyzer.

---

## 1. Success Response

**EXISTING.** Envelope: `{ "data": ..., "links": { "self": "..." }, "meta": ... }`.

```csharp
// Web.Api/Contracts/SuccessResponse.cs
public sealed class SuccessResponse<TData>
{
    public required TData Data { get; init; }
    public required Links Links { get; init; }
    public Meta? Meta { get; init; }
}
```

Rules (**REQUIRED** whenever this envelope is used):

- `data` — always present on a body-carrying success response.
- `links` — always present; `links.self` is always present, built from the *actual* request
  (`request.Scheme`/`Host`/`PathBase`/`Path`/`QueryString`), never hardcoded.
- `meta` — present **only** for paginated collections (`totalRecords`/`totalPages`); `null`
  (omitted from JSON, since `System.Text.Json`'s default web options don't serialize `null`
  members that are reference/nullable-value types... actually this template does **not** set
  `DefaultIgnoreCondition`, so a `null` `meta` **is** serialized as `"meta": null`, not omitted —
  see §9 for why this specific deviation from strict Open Banking payload minimalism is accepted)
  on everything else.
- No additional top-level properties beyond `data`/`links`/`meta` — don't add `status`,
  `timestamp`, etc. to the envelope without a documented reason.
- camelCase JSON — automatic: ASP.NET Core minimal APIs' default `Results.Ok(...)`/`Results.Json(...)`
  use the framework's Web defaults (`JsonSerializerDefaults.Web`), which is camelCase. No custom
  `JsonSerializerOptions` were added for this.
- Original payload types are preserved — `Data` is generic (`TData`), never coerced to `object`/
  `string`; a `PagedResult<TodoResponse>`'s `Items` stay `IReadOnlyList<TodoResponse>` all the way
  into `SuccessResponse<IReadOnlyList<TodoResponse>>.Data`.

### Non-paginated example

```json
{
  "data": { "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6", "email": "user@example.com" },
  "links": { "self": "https://api.example.com/users/3fa85f64-5717-4562-b3fc-2c963f66afa6" }
}
```

### Building it — `ApiResponseFactory`

**EXISTING.** `Web.Api/Infrastructure/ApiResponseFactory.cs` is the single place a successful
`Result`/`Result<T>` value is wrapped:

```csharp
public static IResult Ok<TData>(TData data, HttpRequest request) => ...        // non-paginated
public static IResult Ok<TItem>(PagedResult<TItem> pagedResult, HttpRequest request) => ...  // paginated
```

**REQUIRED** when an endpoint adopts this pattern: call `ApiResponseFactory.Ok(...)` from the
success branch of `result.Match(...)` — never construct `SuccessResponse<T>` by hand inside an
endpoint. This is the same "one centralized translation point" principle
[`patterns/error-handling.md`](../patterns/error-handling.md) already establishes for
`CustomResults.Problem` on the failure side.

**Scope note**: adopting `ApiResponseFactory`/the envelope on an endpoint is **opt-in per
endpoint**, not a global change. Existing endpoints other than the reference feature (`Todos/Get`,
see §8) still return their original bodies (`Results.Ok(value)`, `Results.NoContent()`) — this
template does not force every endpoint onto the new envelope. See §9 for why.

---

## 2. Error Response

**EXISTING.** Envelope: `{ "errors": [ { "code": "...", "title": "...", "detail": "..." } ] }`.

```csharp
// Web.Api/Contracts/ErrorResponse.cs / ApiError.cs
public sealed class ErrorResponse { public required IReadOnlyCollection<ApiError> Errors { get; init; } }
public sealed class ApiError
{
    public required string Code { get; init; }    // stable, machine-readable business/API code
    public required string Title { get; init; }    // short category label, derived from ErrorType
    public required string Detail { get; init; }    // Error.Description — the specific message
}
```

`errors` is **always** an array, even for a single failure — so a single business-rule failure and
a multi-field validation failure share one shape; callers never need to branch on "is this one
error or many."

```json
{
  "errors": [
    {
      "code": "TodoItems.NotFound",
      "title": "Resource Not Found",
      "detail": "The to-do item with the Id = '3fa85f64-5717-4562-b3fc-2c963f66afa6' was not found"
    }
  ]
}
```

```json
{
  "errors": [
    { "code": "Description.Required", "title": "Validation Error", "detail": "Description is required." },
    { "code": "Priority.Invalid", "title": "Validation Error", "detail": "Priority is invalid." }
  ]
}
```

### `code` vs. HTTP status — deliberately separate

**REQUIRED**: `code` identifies the business/API error (`"TodoItems.AlreadyCompleted"`,
`"Pagination.PageSizeTooLarge"`) — it is the same stable string already produced by every
`Domain.<Aggregate>Errors`/`Application.Abstractions.Pagination.PaginationErrors` factory
(`Error.Code`). It is **never** the HTTP status ("404", "NotFound") and never changes if the HTTP
mapping for that `ErrorType` is later revisited. `title` is a short, generic category derived from
`ErrorType` (`"Resource Not Found"`, `"Validation Error"`); `detail` is `Error.Description`, the
specific human-readable message.

---

## 3. Result Pattern integration — no second error mechanism

**EXISTING.** The flow is unchanged from before this pattern existed — see
[`patterns/result-pattern.md`](../patterns/result-pattern.md) and
[`patterns/error-handling.md`](../patterns/error-handling.md) for the full mechanics. This
document only changes what `CustomResults.Problem` and `GlobalExceptionHandler` *emit*, not how a
failure reaches them:

```
Domain/Application  →  Result.Failure(SomeErrors.X(...))  →  Web.Api  →  CustomResults.Problem  →  ErrorResponse
Unhandled exception  →  GlobalExceptionHandler                                                    →  ErrorResponse
```

- Expected/business failures **never** become exceptions — same rule as before, unchanged.
- `CustomResults.Problem(Result result)` (`Web.Api/Infrastructure/CustomResults.cs`) is still the
  **only** place a failed `Result` becomes an HTTP response — its output shape changed (see §5),
  its role and call sites (`result.Match(<success>, CustomResults.Problem)`, unchanged across
  every existing endpoint) did not.
- `GlobalExceptionHandler` is still the only place an unhandled exception becomes an HTTP
  response — its output shape now matches `CustomResults.Problem`'s (`ErrorResponse`), for a
  single consistent error contract across the whole API regardless of whether the failure was
  expected or not.

**REQUIRED**: don't introduce a second exception-to-HTTP or Result-to-HTTP translation path. If a
new failure case needs a status code this document doesn't cover, add an `ErrorType` +
`CustomResults` mapping entry (see §5) — don't hand-roll a `Results.Json(...)` call in an endpoint.

---

## 4. Application boundaries — who knows what

**REQUIRED**, matches this template's existing dependency rules
([`architecture/dependency-rules.md`](../architecture/dependency-rules.md)):

| Concept | Lives in | Knows about HTTP? |
|---|---|---|
| `PaginationRequest`, `PaginationErrors`, `PagedResult<T>`, `PaginationQueryableExtensions` | `Application/Abstractions/Pagination` | No — page/pageSize/skip/take/count are plain data and EF Core LINQ, no `HttpContext`/`HttpRequest`/status codes/URLs |
| `Links`, `Meta`, `SuccessResponse<T>`, `ApiError`, `ErrorResponse` | `Web.Api/Contracts` | Yes — these are the literal wire contract |
| `ApiResponseFactory`, `PaginationLinkBuilder`, `CustomResults`, `GlobalExceptionHandler` | `Web.Api/Infrastructure` | Yes — HTTP status codes, `IResult`, `HttpRequest`, URL construction |

**Why pagination's core types live in `Application`, not `SharedKernel` or `Web.Api`**: they are
tightly coupled to the CQRS query-handler pattern (`IQueryHandler<TQuery, PagedResult<TResponse>>`)
that already lives in `Application` — the same place `*Response` DTOs live per
[`patterns/cqrs.md`](../patterns/cqrs.md). `SharedKernel` was considered and rejected: it holds
cross-cutting DDD primitives (`Entity`, `Result`, `Error`) used by every layer including `Domain`;
pagination is a query/read-side concern with no meaning in `Domain`, so putting it in
`SharedKernel` would widen that project's purpose without a corresponding need in `Domain`.
`Web.Api` was also rejected for the core pagination types, since `PagedResult<T>`/
`PaginationRequest` carry no HTTP concept (no status code, no URL) and are exactly the kind of
"data and pagination, not HTTP" the Application layer already owns for every other query.

**Why the envelope/link/error contracts live in `Web.Api`, not `Application`**: they are the
literal HTTP/JSON wire shape — `Links.Self` is a URL string, `ApiError`'s HTTP-status-adjacent
`Title` is derived from `ErrorType` specifically for the HTTP presentation, and building them
requires `HttpRequest`. Per this template's existing convention ("contratos HTTP específicos devem
permanecer na camada de apresentação"), and consistent with `CustomResults`/`GlobalExceptionHandler`
already living in `Web.Api` before this change, these stay in `Web.Api/Contracts` and
`Web.Api/Infrastructure`.

**REQUIRED** — `Domain` and `Application` must never reference: `HttpContext`, `HttpRequest`,
`IResult`, `Results`, HTTP status codes, URLs, `ProblemDetails`, or any HTTP header. This is
already enforced structurally: `Application`'s pagination types have zero ASP.NET Core package
reference, and `tests/ArchitectureTests/Layers/LayerTests.cs`'s existing
`ApplicationLayer_ShouldNotHaveDependencyOn_PresentationLayer` rule (see
[`architecture/dependency-rules.md`](../architecture/dependency-rules.md)) already fails the build
if `Application` ever references `Web.Api` — no new architecture test was needed for this feature
specifically, since the existing layer rule already covers it by construction.

---

## 5. HTTP Status Codes — the `ErrorType` → status mapping

**EXISTING**, in `Web.Api/Infrastructure/CustomResults.cs`:

| `ErrorType` | HTTP status | Produced by |
|---|---|---|
| `Validation` | 400 Bad Request | `ValidationDecorator` (FluentValidation failures) |
| `Problem` | 400 Bad Request | A `Domain.*Errors` factory for a business-rule violation (e.g. `TodoItemErrors.AlreadyCompleted`) |
| `NotFound` | 404 Not Found | `Domain.*Errors.NotFound(...)` |
| `Conflict` | 409 Conflict | `Domain.*Errors` for a uniqueness/state conflict (e.g. `UserErrors.EmailNotUnique`) |
| `Forbidden` | 403 Forbidden | An authenticated caller acting on a resource they don't own (e.g. `UserErrors.Unauthorized()`) |
| `Unprocessable` | 422 Unprocessable Entity | Pagination parameter validation (`PaginationErrors`) — see §6 |
| `Failure` (default/unmapped) | 500 Internal Server Error | `Error.NullValue`, or any genuinely unexpected `Error` |

Success codes are set by the endpoint's chosen `Results.*` call, unchanged by this pattern:
`200 OK` (`ApiResponseFactory.Ok`/`Results.Ok`), `201 Created` (`Results.Created`, where an
endpoint returns a created resource's location — not currently used by any existing endpoint, but
supported the same way as before), `204 No Content` (`Results.NoContent`, for commands with no
return value — see §5.1).

### 5.1 — `204 No Content` and the envelope

**REQUIRED**: `204 No Content` has, by definition, no response body — the envelope does not (and
must not) apply to it. Commands that return `Result` (no value) — `Todos/Complete`,
`Todos/Delete` — keep returning `Results.NoContent()` on success, exactly as before this pattern
was introduced. Don't wrap a `204` in `{ "data": null, "links": {...} }`; that would contradict the
"data is required on a success **response with a body**" rule by inventing a body where the
correct contract has none.

### 5.2 — Codes deliberately *not* produced by `Result`/`CustomResults.Problem`

**Decision, documented per the instruction to record every difference from Open Banking Brasil's
own list**: `401 Unauthorized`, `403 Forbidden`\* and `429 Too Many Requests` are **not** part of
the `ErrorType` → status table above, because they don't originate from a business-rule check
inside a handler — they originate from infrastructure that runs *before* a handler is ever
reached:

- **401 Unauthorized** — produced by ASP.NET Core's authentication middleware
  (`app.UseAuthentication()`) when `.RequireAuthorization()`/`.HasPermission(...)` rejects an
  unauthenticated request. This already happens today, unrelated to this feature.
- **429 Too Many Requests** — would be produced by a rate-limiting middleware. **Not implemented
  in this template** — no rate limiting exists. **RECOMMENDED**: if a project built on this
  template adds `Microsoft.AspNetCore.RateLimiting`, it operates at the middleware level, the same
  way 401 does, not through `ErrorType`/`Result`. No `ErrorType.TooManyRequests` was added
  speculatively — see the "don't invent unused enum members" reasoning in §9.

\* One exception: `ErrorType.Forbidden` **was** added (see §5.3) because a real, existing gap
(`UserErrors.Unauthorized()`) needed it — this is a `Result`-driven 403, distinct from the
ASP.NET Core-pipeline 401 above; both exist, for different reasons.

### 5.3 — Correction made during this work: `UserErrors.Unauthorized()` was mapping to 500

**Bug found and fixed while establishing this mapping table.** Before this change,
`Domain/Users/UserErrors.cs`'s `Unauthorized()` factory used `Error.Failure(...)`
(`ErrorType.Failure`), which `CustomResults.Problem` maps to `500 Internal Server Error` — meaning
every existing call site (`CreateTodoCommandHandler`, `GetTodosQueryHandler`,
`GetUserByEmailQueryHandler` rejecting a caller acting on someone else's resource) returned a
generic 500 "server failure" for what is actually an authorization failure. Fixed:
`UserErrors.Unauthorized()` now uses `Error.Forbidden(...)` (`ErrorType.Forbidden` → 403). The
C# method name (`Unauthorized()`) was **not** renamed — every existing call site keeps compiling
unchanged — but it now correctly maps to 403, not 401 (the caller *is* authenticated; they're
acting on a resource that isn't theirs, which is a Forbidden, not an Unauthorized, condition in
HTTP terms). This naming mismatch (method says "Unauthorized", HTTP status is 403 Forbidden) is
called out explicitly in a code comment at the call site and here, so it isn't mistaken for a
new inconsistency.

---

## 6. Pagination

**EXISTING.** Query parameters: `?page=1&page-size=25` (hyphenated `page-size`, matching the
Open Banking Brasil convention, bound via `[FromQuery(Name = "page-size")]` since C# identifiers
can't contain hyphens).

### 6.1 — `PaginationRequest`

```csharp
// Application/Abstractions/Pagination/PaginationRequest.cs
public static Result<PaginationRequest> Create(int? page, int? pageSize)
```

| Rule | Behavior |
|---|---|
| `page` default | `1` (`page=null` → `1`) |
| `page-size` default | `25` (`page-size=null` → `25`) |
| `page-size` maximum | `1000` |
| `page < 1` | `Result.Failure`, `ErrorType.Unprocessable` → **422** |
| `page-size < 1` | `Result.Failure`, `ErrorType.Unprocessable` → **422** |
| `page-size > 1000` | `Result.Failure`, `ErrorType.Unprocessable` → **422** |

**Decision**: all three invalid-pagination-parameter cases map to `422`, not just
`page-size > 1000` (the only one the task explicitly named a status for). Rationale: all three
represent the same category of failure — syntactically well-formed but semantically invalid
request parameters — so giving them one consistent status is more predictable for API consumers
than splitting `page < 1` onto a different code than `page-size > 1000` for no functional reason.

Validation is **centralized in `PaginationRequest.Create`**, called once per endpoint (see §8) —
**REQUIRED**: don't re-implement page/page-size bounds-checking in an endpoint or handler; call
`PaginationRequest.Create` and branch on its `Result` exactly like the `Todos/Get` reference
endpoint does.

**Why validation isn't done via `ValidationDecorator`/FluentValidation**: `ValidationDecorator`
only wraps `ICommandHandler`/`ICommandHandler<,>` (see
[`patterns/validation.md`](../patterns/validation.md)) — queries have no validation decorator in
this template. Rather than extending the decorator infrastructure (out of scope for this feature,
and would change behavior for every existing query), pagination validation happens once at the
`Web.Api` boundary, immediately converted through the same `Result` → `CustomResults.Problem` path
every other failure already uses — no second validation mechanism, no decorator change.

### 6.2 — `PagedResult<TItem>`

```csharp
// Application/Abstractions/Pagination/PagedResult.cs
public sealed class PagedResult<TItem>
{
    public IReadOnlyList<TItem> Items { get; }
    public int Page { get; }
    public int PageSize { get; }
    public int TotalRecords { get; }
    public int TotalPages { get; }   // totalRecords == 0 ? 0 : Ceiling(totalRecords / (double)pageSize)
}
```

- `totalRecords` and `totalPages` are always present in the response `meta` — never optional once
  a response is paginated.
- Zero records → `totalPages = 0` (not `1`).
- `totalPages` uses ceiling division against the **requested** `page-size** (100 records / 25 →
  4; 101 records / 25 → 5 — one partial page still counts as a full page).

### 6.3 — EF Core execution — no full-table materialization

**EXISTING.** `Application/Abstractions/Pagination/PaginationQueryableExtensions.ToPagedResultAsync`
centralizes the count/skip/take/project shape so it isn't re-implemented per handler:

```csharp
int totalRecords = await source.CountAsync(cancellationToken);
if (totalRecords == 0) return PagedResult<TResult>.Create([], pagination, totalRecords: 0);

List<TResult> items = await source
    .Skip(pagination.Skip)
    .Take(pagination.PageSize)
    .Select(selector)
    .ToListAsync(cancellationToken);
```

- One `CountAsync` against the filtered (not yet paged) query — translated by the EF Core
  provider into `SELECT COUNT(*) ... WHERE ...`, never a client-side count over a materialized
  list.
- `Skip`/`Take` run **before** `.ToListAsync()` — the database returns only the requested page's
  rows; the full table is never pulled into memory.
- `.Select(selector)` projects directly to the target DTO type (same "project, don't
  `.AsNoTracking()` a whole entity" pattern already established in
  [`persistence/ef-core.md`](../persistence/ef-core.md#queries--tracking-asnotracking-projections)
  for non-paginated queries) — so paginated queries get the same non-tracking, low-allocation
  behavior other query handlers already have, with no extra `.AsNoTracking()` call needed (a
  `.Select(...)` projection never materializes tracked entities in the first place).
- Zero-record short-circuit: if `CountAsync` returns `0`, the `Skip`/`Take`/`ToListAsync` round
  trip is skipped entirely — no wasted second query for a page that's guaranteed empty.

### 6.4 — Deterministic ordering is REQUIRED before `Skip`/`Take`

**Decision, explicitly requested to be documented**: pagination over an unordered query can
return duplicate or skipped rows across pages (the database is free to return rows in a different
order for each `Skip`/`Take` call with no `ORDER BY`). `ToPagedResultAsync` **enforces this
structurally**, not just by convention:

```csharp
if (!IsOrdered(source))
{
    throw new InvalidOperationException(
        $"Paginated query for '{typeof(TSource).Name}' has no OrderBy/OrderByDescending. " +
        "Pagination requires a deterministic order — add one before calling ToPagedResultAsync.");
}
```

`IsOrdered` walks the `IQueryable`'s expression tree looking for an `OrderBy`/`OrderByDescending`
call anywhere in the chain (a `.ThenBy(...)` after `.OrderBy(...)` is detected too, since
`ThenBy`'s source expression resolves back to the `OrderBy` call). This is a **developer-facing
guard** (an `InvalidOperationException`, not a `Result.Failure` — a missing `OrderBy` is a bug in
the calling code, not a runtime/user-facing condition) that fails fast in development/tests rather
than shipping non-deterministic pagination.

**REQUIRED**: every paginated query must supply an explicit order before calling
`ToPagedResultAsync`, using a column that is unique or has a unique tiebreaker as the final sort
key — a non-unique column alone (e.g. `CreatedAt`, which can collide for rows created in the same
tick) is not sufficient by itself. The reference feature demonstrates this:

```csharp
// Application/Todos/Get/GetTodosQueryHandler.cs
IOrderedQueryable<TodoItem> orderedQuery = context.TodoItems
    .Where(todoItem => todoItem.UserId == query.UserId)
    .OrderBy(todoItem => todoItem.CreatedAt)
    .ThenBy(todoItem => todoItem.Id);   // Id (Guid PK) as the stable secondary key
```

### 6.5 — `CancellationToken`

**REQUIRED**, unchanged from this template's existing convention (see
[`persistence/ef-core.md`](../persistence/ef-core.md#cancellationtoken)): every I/O call in the
pagination path threads the token — endpoint delegate → `handler.Handle(query, cancellationToken)`
→ `ToPagedResultAsync(pagination, selector, cancellationToken)` → `CountAsync(cancellationToken)`
→ `ToListAsync(cancellationToken)`. No sync/blocking call anywhere in the pagination path.

---

## 7. Links

**EXISTING.** `Web.Api/Infrastructure/PaginationLinkBuilder.Build` constructs every link from the
current request — never a hardcoded URL — and preserves every existing query parameter except
`page`/`page-size`, which it overrides per link.

### Presence rules

| Link | Present when |
|---|---|
| `self` | always |
| `first` | `page > 1` (not the first page) |
| `prev` | `page > 1` (same condition as `first` — points to `page - 1`) |
| `next` | `page < totalPages` (not the last page — points to `page + 1`) |
| `last` | `page < totalPages` (same condition as `next` — points to `totalPages`) |

This mirrors `PagedResult<T>.HasPreviousPage`/`HasNextPage` exactly, so the presence rule is
defined in exactly one place (`PagedResult`) and reused, not duplicated between the data model and
the link builder.

### Worked example

`GET /api/todos?userId=...&page=2&page-size=25` against 100 total records (4 pages):

```json
{
  "data": [ /* 25 items */ ],
  "links": {
    "self":  "https://api.example.com/api/todos?userId=...&page=2&page-size=25",
    "first": "https://api.example.com/api/todos?userId=...&page=1&page-size=25",
    "prev":  "https://api.example.com/api/todos?userId=...&page=1&page-size=25",
    "next":  "https://api.example.com/api/todos?userId=...&page=3&page-size=25",
    "last":  "https://api.example.com/api/todos?userId=...&page=4&page-size=25"
  },
  "meta": { "totalRecords": 100, "totalPages": 4 }
}
```

### Reverse proxy / forwarded headers

**EXISTING.** `PaginationLinkBuilder` reads `HttpRequest.Scheme`/`Host`, which
`ForwardedHeadersMiddleware` rewrites in place when the app runs behind a reverse
proxy/load balancer. `Program.cs` now registers it:

```csharp
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
});
...
app.UseForwardedHeaders();   // before MapEndpoints / any middleware that reads Scheme or Host
```

Without this, links generated behind a typical reverse-proxy deployment (Docker, Kubernetes
ingress, a load balancer terminating TLS) would report the internal scheme/host (e.g.
`http://web-api:8080`) instead of the externally visible one (`https://api.example.com`). This is
the one small addition to `Program.cs` this feature required beyond the endpoint/contract code —
justified directly by the "considere reverse proxy, forwarded headers" requirement, not scope
creep.

### Non-paginated links

**REQUIRED**: a non-paginated success response has `links.self` only — no `first`/`prev`/`next`/
`last`. `ApiResponseFactory.Ok<TData>(TData, HttpRequest)` (the non-paginated overload) never adds
them; only the `PagedResult<TItem>` overload does.

---

## 8. Reference feature — `Todos/Get`, adapted end to end

**EXISTING.** `GET /todos` was chosen because it already existed, already returned a collection,
and is exactly the shape Open Banking Brasil's pagination convention targets. No fictitious
feature was created.

```
Query
  Application/Todos/Get/GetTodosQuery.cs
    public sealed record GetTodosQuery(Guid UserId, PaginationRequest Pagination)
      : IQuery<PagedResult<TodoResponse>>;
↓
Handler
  Application/Todos/Get/GetTodosQueryHandler.cs
    context.TodoItems.Where(...).OrderBy(...).ThenBy(...) → ToPagedResultAsync(...) → PagedResult<TodoResponse>
↓
Endpoint
  Web.Api/Endpoints/Todos/Get.cs
    PaginationRequest.Create(page, pageSize) → Result<PaginationRequest>
      failure → CustomResults.Problem(paginationResult)   // 422, before the handler even runs
      success → handler.Handle(new GetTodosQuery(userId, pagination), ct)
                  → result.Match(pagedResult => ApiResponseFactory.Ok(pagedResult, request), CustomResults.Problem)
```

Before/after summary:

| | Before | After |
|---|---|---|
| `GetTodosQuery` | `(Guid UserId) : IQuery<List<TodoResponse>>` | `(Guid UserId, PaginationRequest Pagination) : IQuery<PagedResult<TodoResponse>>` |
| Handler return | `List<TodoResponse>` (all rows) | `PagedResult<TodoResponse>` (one page) |
| Ordering | none | `OrderBy(CreatedAt).ThenBy(Id)` — now required, see §6.4 |
| Endpoint response | `Results.Ok(list)` — bare array | `{ data, links, meta }` envelope via `ApiResponseFactory.Ok` |

No other endpoint was changed — see §9 for why this stays scoped to one feature.

---

## 9. Differences from Open Banking Brasil, and why

Documented per the explicit instruction to record every deliberate deviation:

1. **No FAPI/OAuth security profile, no consent flow, no resource-specific regulated payloads.**
   Out of scope by design — this is a generic template, not a regulated Open Banking participant.
2. **`meta` is serialized as `"meta": null` when absent, not omitted from the JSON.** This
   template did not configure `JsonSerializerOptions.DefaultIgnoreCondition =
   JsonIgnoreCondition.WhenWritingNull` (no existing DTO in this template relies on null-omission
   either — see `Todos/Get/TodoResponse.cs`'s nullable `DueDate`/`CompletedAt`, which already
   serialize as explicit `null` today). Changing that global JSON option would affect every
   endpoint's output, not just the new envelope, so it was left as-is; if strict payload
   minimalism is required, add `DefaultIgnoreCondition` project-wide as its own deliberate change.
3. **Only `page`/`page-size` are standardized; no `sort`/`fields`/`filter` query convention was
   added.** Open Banking Brasil's Fase 1 docs focus on pagination and payload/error shape, which
   is what was asked for — sorting/filtering conventions are a separate, larger design decision
   left for a future task.
4. **401/403/429 are not part of the `ErrorType` → status table** — see §5.2. Open Banking Brasil
   models these as API-level error responses; this template produces 401/(future)429 from
   ASP.NET Core middleware before a handler ever runs, and models 403 through `Result`/`ErrorType`
   only for the one case that's genuinely a business-rule outcome (§5.3).
5. **No `ErrorType.TooManyRequests` was added.** Adding an enum member (and an unused `Error`
   factory) with no code path that ever produces it would itself be the kind of unnecessary
   abstraction this task explicitly warned against ("não invente abstrações excessivamente
   genéricas"). If/when rate limiting is added, add the mapping alongside it — see §5.2.
6. **`ErrorType.Problem` stays mapped to 400, not reinterpreted as 422.** Business-rule violations
   already using `Error.Problem(...)` (e.g. `TodoItemErrors.AlreadyCompleted`) keep their existing
   400 status — only the *new* pagination-parameter-validation errors use the new `Unprocessable`
   (422) type. Retargeting every existing `Problem` error to 422 would be an unrelated, broader
   behavior change to every existing handler's HTTP contract, out of scope for this feature.
7. **The envelope is opt-in per endpoint, not applied to the whole API in this change.** Per the
   explicit instruction that this must not "transform the whole project" — only `Todos/Get` (§8)
   demonstrates the pattern. `CustomResults.Problem`/`GlobalExceptionHandler`'s error shape change
   (§2, §3) *is* global, because those were already the single centralized translation points for
   every endpoint's failures — updating them once is centralizing, not spreading, the new
   contract; the equivalent centralization doesn't exist for success responses (every endpoint
   builds its own success shape today), so extending it everywhere in one pass was out of scope.

---

## 10. Testing

**RECOMMENDED shape** for a project extending this pattern further (see
[`testing/overview.md`](../testing/overview.md) for this template's overall test-project
situation — no unit test project existed before this feature; see below for what this feature
adds).

| Area | What to cover |
|---|---|
| `PaginationRequest.Create` | default page/page-size, page < 1, page-size < 1, page-size > 1000 (422 + correct `ErrorType`/code), page-size == 1000 (boundary, succeeds), `Skip` computation |
| `PagedResult<T>` | zero records → `totalPages == 0`, exact-multiple and non-exact ceiling division, `HasPreviousPage`/`HasNextPage` on first/middle/last page |
| `PaginationQueryableExtensions.ToPagedResultAsync` (EF Core, real provider) | first/middle/last page slicing, zero records, projection (not raw entities), missing `OrderBy` throws `InvalidOperationException`, cancellation token honored |
| `CustomResults.Problem` | every `ErrorType` → status code, multi-error `ValidationError` → multiple `ApiError`s, throws on a success `Result` |
| `GlobalExceptionHandler` | 500 + generic `ErrorResponse`, exception details never leaked into `Detail` |
| `PaginationLinkBuilder` | first/middle/last page link presence, zero records → only `self`, query parameter preservation, custom scheme/host |
| `ApiResponseFactory` | non-paginated → `meta` is `null`; paginated → `meta`/`links` populated |

Test-project layout used while developing this feature (kept as scaffolding regardless of whether
the test run itself was finished in this pass): `tests/Application.UnitTests` (pure pagination
logic + an EF Core InMemory-backed test of `ToPagedResultAsync`, following the "real provider,
swapped storage" guidance in
[`testing/integration-tests.md`](../testing/integration-tests.md#what-to-swap-vs-what-to-keep-real"))
and `tests/Web.Api.UnitTests` (`CustomResults`, `GlobalExceptionHandler`, `PaginationLinkBuilder`,
`ApiResponseFactory`, using `DefaultHttpContext` to fake `HttpRequest` rather than a full
`WebApplicationFactory`). Architecture-level coverage needs no new test: the existing
`ApplicationLayer_ShouldNotHaveDependencyOn_PresentationLayer` rule in
`tests/ArchitectureTests/Layers/LayerTests.cs` already fails the build if pagination's
Application-side types ever gained an HTTP reference (see §4).
