using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace lucia.Data.Configuration;

/// <summary>
/// Configuration source that reads key-value pairs from the EF Core database.
/// </summary>
public sealed class EfConfigurationSource : IConfigurationSource
{
    /// <summary>
    /// Factory function to create the DbContextOptions for reading config.
    /// </summary>
    public required Func<DbContextOptions<LuciaDbContext>> OptionsFactory { get; init; }

    /// <summary>
    /// Polling interval for change detection. Default: 5 seconds.
    /// </summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

    public IConfigurationProvider Build(IConfigurationBuilder builder)
        => new EfConfigurationProvider(OptionsFactory, PollInterval);
}
