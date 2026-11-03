---
name: add-background-job
description: Add a background job (a hosted BackgroundService) to the Clean Architecture template that runs on a schedule and safely resolves scoped services. Use when the user wants a recurring task, worker, cleanup job, or scheduled processing.
argument-hint: <what the job does, e.g. "delete expired refresh tokens every hour">
---

# Add a Background Job

Recurring work runs as a `BackgroundService` in the **Web.Api** project. A hosted service is a **singleton**, so scoped services (DbContext, handlers) must be resolved inside a scope created per iteration.

## Files to create/modify

1. **The job** — `src/Web.Api/BackgroundJobs/{Name}BackgroundService.cs`

```csharp
using Application.Abstractions.Data;
using Microsoft.EntityFrameworkCore;

namespace Web.Api.BackgroundJobs;

internal sealed class ExpiredRefreshTokensCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<ExpiredRefreshTokensCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                IApplicationDbContext context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

                await context.RefreshTokens
                    .Where(token => token.ExpiresOnUtc < DateTime.UtcNow)
                    .ExecuteDeleteAsync(stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Background job {Job} failed", nameof(ExpiredRefreshTokensCleanupService));
            }
        }
    }
}
```

2. **Registration** — in `src/Web.Api/DependencyInjection.cs` (`AddPresentation`, or a small `AddBackgroundJobs` helper):

```csharp
services.AddHostedService<ExpiredRefreshTokensCleanupService>();
```

## Rules

- A hosted service is a singleton — **never** inject `IApplicationDbContext`, handlers, or other scoped/transient services directly. Resolve them from a per-iteration scope via `IServiceScopeFactory`.
- Wrap each iteration body in try/catch so one failure doesn't stop the loop; log with `ILogger`.
- Use `PeriodicTimer` for the schedule and always honor `stoppingToken`.
- If the work is really a use case, prefer invoking an existing command/handler inside the scope instead of duplicating logic.
- Build + run the tests when done.
