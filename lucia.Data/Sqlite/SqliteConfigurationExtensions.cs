using Microsoft.Extensions.Configuration;

namespace lucia.Data.Sqlite;

/// <summary>
/// Extensions to add SQLite-backed configuration for application-specific settings.
/// SQLite config has highest priority so admin UI changes override appsettings.json.
/// </summary>
public static class SqliteConfigurationExtensions
{
    /// <summary>
    /// Adds SQLite configuration from the lucia database.
    /// The <see cref="SqliteConnectionFactory"/> must already be constructed with the correct database path.
    /// </summary>
    public static IConfigurationBuilder AddSqliteConfiguration(
        this IConfigurationBuilder builder,
        SqliteConnectionFactory connectionFactory,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);

        builder.Add(new SqliteConfigurationSource
        {
            ConnectionFactory = connectionFactory,
            PollInterval = pollInterval ?? TimeSpan.FromSeconds(5)
        });

        return builder;
    }
}
