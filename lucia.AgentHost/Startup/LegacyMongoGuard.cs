using lucia.Data;

namespace lucia.AgentHost.Startup;

/// <summary>
/// Stops startup when the configuration still points at MongoDB. Old Compose and Kubernetes files never
/// set <c>DataProvider:Store</c> and relied on the former MongoDB default, so without this check the host
/// would quietly start on an empty SQLite file and reopen the setup wizard.
/// </summary>
public static class LegacyMongoGuard
{
    private const string Message =
        "MongoDB is no longer supported. Set DataProvider:Store to 'PostgreSQL' or 'SQLite', replace any " +
        "mongodb:// connection strings, and re-run setup. See RELEASE_NOTES.md for the 1.6.0 upgrade steps.";

    public static void ThrowIfConfigured(IConfiguration configuration)
    {
        var store = configuration[$"{DataProviderOptions.SectionName}:Store"];
        if (string.Equals(store, "MongoDB", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"DataProvider:Store is 'MongoDB'. {Message}");
        }

        foreach (var connection in configuration.GetSection("ConnectionStrings").GetChildren())
        {
            if (connection.Value is { } value
                && (value.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"ConnectionStrings:{connection.Key} is a MongoDB connection string. {Message}");
            }
        }
    }
}