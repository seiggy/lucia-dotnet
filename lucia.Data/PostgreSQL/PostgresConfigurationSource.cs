using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace lucia.Data.PostgreSQL;

/// <summary>
/// Configuration source that creates a <see cref="PostgresConfigurationProvider"/>.
/// Added to the configuration pipeline to layer PostgreSQL config on top of appsettings.
/// </summary>
public sealed class PostgresConfigurationSource : IConfigurationSource
{
    /// <summary>
    /// PostgreSQL connection factory for accessing the database.
    /// </summary>
    public PostgresConnectionFactory ConnectionFactory { get; set; } = default!;

    /// <summary>
    /// Polling interval for change detection. Default: 5 seconds.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Optional logger factory for structured logging during configuration loading.
    /// Falls back to NullLoggerFactory when not provided.
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; set; }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new PostgresConfigurationProvider(ConnectionFactory, LoggerFactory, PollInterval);
    }
}
