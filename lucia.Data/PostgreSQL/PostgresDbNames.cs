namespace lucia.Data.PostgreSQL;

/// <summary>
/// Well-known database names for PostgreSQL keyed service resolution.
/// Mirrors the MongoDB three-database pattern (luciaconfig, luciatraces, luciatasks).
/// </summary>
public static class PostgresDbNames
{
    /// <summary>Configuration, model providers, agent definitions, API keys, presence, plugins, memories.</summary>
    public const string Config = "luciaconfig";

    /// <summary>Conversation traces, command traces, dataset exports.</summary>
    public const string Traces = "luciatraces";

    /// <summary>Scheduled tasks, alarm clocks, task archive.</summary>
    public const string Tasks = "luciatasks";
}
