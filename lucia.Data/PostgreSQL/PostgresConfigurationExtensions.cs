using Microsoft.Extensions.Configuration;

namespace lucia.Data.PostgreSQL;

/// <summary>
/// Extensions to add PostgreSQL-backed configuration for application-specific settings.
/// PostgreSQL config has highest priority so admin UI changes override appsettings.json.
/// </summary>
public static class PostgresConfigurationExtensions
{
    /// <summary>
    /// Adds PostgreSQL configuration from the lucia database.
    /// The <see cref="PostgresConnectionFactory"/> must already be constructed with the correct connection string.
    /// </summary>
    public static IConfigurationBuilder AddPostgresConfiguration(
        this IConfigurationBuilder builder,
        PostgresConnectionFactory connectionFactory,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);

        builder.Add(new PostgresConfigurationSource
        {
            ConnectionFactory = connectionFactory,
            PollInterval = pollInterval ?? TimeSpan.FromSeconds(5)
        });

        return builder;
    }
}
