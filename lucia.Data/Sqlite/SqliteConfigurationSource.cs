using Microsoft.Extensions.Configuration;

namespace lucia.Data.Sqlite;

/// <summary>
/// Configuration source that creates a <see cref="SqliteConfigurationProvider"/>.
/// Added to the configuration pipeline to layer SQLite config on top of appsettings.
/// </summary>
public sealed class SqliteConfigurationSource : IConfigurationSource
{
    /// <summary>
    /// SQLite connection factory for accessing the database.
    /// </summary>
    public SqliteConnectionFactory ConnectionFactory { get; set; } = default!;

    /// <summary>
    /// Polling interval for change detection. Default: 5 seconds.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new SqliteConfigurationProvider(ConnectionFactory, PollInterval);
    }
}
