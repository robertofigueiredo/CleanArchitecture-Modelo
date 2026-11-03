using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace Application.UnitTests.Abstractions;

public abstract class BaseHandlerTest
{
    protected static TestDbContext CreateDbContext()
    {
        DbContextOptions<TestDbContext> options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase($"clean-architecture-{Guid.NewGuid()}")
            .Options;

        return new TestDbContext(options);
    }

    protected static HybridCache CreateCache()
    {
        var services = new ServiceCollection();

#pragma warning disable EXTEXP0018
        services.AddHybridCache();
#pragma warning restore EXTEXP0018

        return services.BuildServiceProvider().GetRequiredService<HybridCache>();
    }
}
