using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace lucia.Data;

/// <summary>
/// Ensures the EF Core database schema exists on application startup.
/// </summary>
public sealed class DatabaseInitializer(
    IDbContextFactory<LuciaDbContext> dbFactory,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Database schema ensured for provider: {Provider}",
                db.Database.ProviderName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize database schema");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
