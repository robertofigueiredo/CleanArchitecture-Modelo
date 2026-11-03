# OpenAPI / Swagger

Swashbuckle (`Swashbuckle.AspNetCore`), configured for JWT bearer auth and enabled only in
`Development`.

## Setup

```csharp
// src/Web.Api/Extensions/ServiceCollectionExtensions.cs — AddSwaggerGenWithAuth
services.AddSwaggerGen(o =>
{
    o.CustomSchemaIds(id => id.FullName!.Replace('+', '-'));   // Login+Request -> Login-Request

    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "JWT Authentication",
        Description = "Enter your JWT token in this field",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = JwtBearerDefaults.AuthenticationScheme,
        BearerFormat = "JWT"
    };

    o.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, securityScheme);
    o.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme, document), [] }
    });
});
```

`Web.Api/DependencyInjection.cs`'s `AddPresentation()` also calls `AddEndpointsApiExplorer()` +
`AddSwaggerGen()` (the plain, non-auth-configured registration used by minimal APIs' endpoint
metadata) — both registrations are present; `AddSwaggerGenWithAuth()` is the one that actually
adds the JWT security scheme and is called from `Program.cs`.

`CustomSchemaIds` flattens nested type names (`Login+Request` → `Login-Request`) so the nested
`Request` DTO pattern used by every endpoint (see [api/endpoints.md](./endpoints.md)) doesn't
collide/break schema generation — required because many endpoints declare a `Request` type with
the same simple name inside different classes.

## Enabling the UI

```csharp
// Program.cs
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerWithUi();  // UseSwagger() + UseSwaggerUI()
    app.ApplyMigrations();
}
```

Swagger UI is **not exposed outside `Development`** — this is intentional; don't move this call
out of the `IsDevelopment()` guard without a specific reason (and without adding its own
authorization if you do, since Swagger UI would otherwise expose the full API surface publicly).

## Response typing

No endpoint currently calls `.Produces<T>()`, `.ProducesProblem(...)`, `.WithName(...)`, or
`.WithSummary(...)` — response typing in the generated spec comes from the minimal API's inferred
return type only (which is `Task<IResult>`, so Swagger's automatic inference is limited). This is
an accepted gap in the sample, not a rule against adding richer annotations: adding
`.Produces<TResponse>(StatusCodes.Status200OK)` / `.ProducesProblem(StatusCodes.Status404NotFound)`
etc. on new endpoints is safe and doesn't conflict with the existing style — just not required to
match current conventions.

## Tags

`.WithTags(Tags.<Aggregate>)` on every endpoint (`Web.Api/Endpoints/Tags.cs`) groups the Swagger
UI by aggregate — one tag per aggregate, no sub-tagging. Add a new `const string` when a new
aggregate's first endpoint is created.
