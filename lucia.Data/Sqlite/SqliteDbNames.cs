namespace lucia.Data.Sqlite;

/// <summary>
/// Well-known database names for SQLite multi-file service resolution.
/// Mirrors the MongoDB/PostgreSQL three-database pattern.
/// </summary>
public static class SqliteDbNames
{
    /// <summary>Configuration, model providers, agent definitions, API keys, presence, plugins, memories.</summary>
    public const string Config = "luciaconfig";

    /// <summary>Conversation traces, command traces, dataset exports.</summary>
    public const string Traces = "luciatraces";

    /// <summary>Scheduled tasks, alarm clocks, task archive.</summary>
    public const string Tasks = "luciatasks";
}
