using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    public static async Task MigrateAsync(IServiceProvider sp, CancellationToken ct)
    {
        await using var scope = sp.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        logger.LogInformation("Applying pending EF Core migrations...");
        try
        {
            await db.Database.MigrateAsync(ct);
            logger.LogInformation("Database migrations applied.");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Database migration failed. Aborting startup (ADR-0001).");
            throw;
        }
    }
}
