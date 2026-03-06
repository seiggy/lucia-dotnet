using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace lucia.Data.Configuration;

/// <summary>
/// Extension methods for adding EF Core-based configuration.
/// </summary>
public static class EfConfigurationExtensions
{
    /// <summary>
    /// Adds EF Core database as a configuration source with highest priority.
    /// </summary>
    /// <param name="builder">The configuration builder.</param>
    /// <param name="configureOptions">Action to configure DbContext options (UseSqlite or UseNpgsql).</param>
    /// <param name="pollInterval">Override default 5-second polling interval.</param>
    public static IConfigurationBuilder AddEfConfiguration(
        this IConfigurationBuilder builder,
        Action<DbContextOptionsBuilder<LuciaDbContext>> configureOptions,
        TimeSpan? pollInterval = null)
    {
        builder.Add(new EfConfigurationSource
        {
            OptionsFactory = () =>
            {
                var optionsBuilder = new DbContextOptionsBuilder<LuciaDbContext>();
                configureOptions(optionsBuilder);
                return optionsBuilder.Options;
            },
            PollInterval = pollInterval ?? TimeSpan.FromSeconds(5)
        });

        return builder;
    }
}
