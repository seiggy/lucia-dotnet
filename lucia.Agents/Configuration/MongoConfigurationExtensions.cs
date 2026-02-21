using Microsoft.Extensions.Configuration;

namespace lucia.Agents.Configuration;

/// <summary>
/// Extensions to add MongoDB-backed configuration for application-specific settings.
/// MongoDB config has highest priority so admin UI changes override appsettings.json,
/// but only app-level sections are stored (not ports, connection strings, or infrastructure).
/// </summary>
public static class MongoConfigurationExtensions
{
    /// <summary>
    /// Adds MongoDB configuration from the luciaconfig database.
    /// Must be called after other configuration sources are added so it can read
    /// the MongoDB connection string from them. MongoDB config has highest priority.
    /// </summary>
    public static IConfigurationBuilder AddMongoConfiguration(
        this IConfigurationBuilder builder,
        string connectionName = "luciaconfig",
        TimeSpan? pollInterval = null)
    {
        var tempConfig = builder.Build();
        var connectionString = tempConfig.GetConnectionString(connectionName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine($"[lucia] MongoConfiguration: Connection string '{connectionName}' not found — skipping MongoDB config source.");
            return builder;
        }

        Console.WriteLine($"[lucia] MongoConfiguration: Adding MongoDB config source from '{connectionName}'.");

        builder.Add(new MongoConfigurationSource
        {
            ConnectionString = connectionString,
            DatabaseName = ConfigEntry.DatabaseName,
            PollInterval = pollInterval ?? TimeSpan.FromSeconds(5)
        });

        return builder;
    }
}
