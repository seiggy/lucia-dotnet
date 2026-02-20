using Microsoft.Extensions.Configuration;

namespace lucia.Agents.Configuration;

/// <summary>
/// Configuration source that creates a MongoConfigurationProvider.
/// Added to the configuration pipeline to layer MongoDB config on top of appsettings.
/// </summary>
public sealed class MongoConfigurationSource : IConfigurationSource
{
    /// <summary>
    /// MongoDB connection string.
    /// </summary>
    public string ConnectionString { get; set; } = default!;

    /// <summary>
    /// Database name to read configuration from.
    /// </summary>
    public string DatabaseName { get; set; } = ConfigEntry.DatabaseName;

    /// <summary>
    /// Polling interval for change detection. Default: 5 seconds.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new MongoConfigurationProvider(ConnectionString, DatabaseName, PollInterval);
    }
}
