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

    public static string GetPath(string basePath, string databaseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);

        var suffix = databaseName switch
        {
            Config => "config",
            Traces => "traces",
            Tasks => "tasks",
            _ => throw new ArgumentOutOfRangeException(
                nameof(databaseName),
                databaseName,
                "Unknown SQLite database name.")
        };
        var directory = Path.GetDirectoryName(basePath);
        var fileName = Path.GetFileNameWithoutExtension(basePath);
        var extension = Path.GetExtension(basePath);
        var databaseFileName = $"{fileName}-{suffix}{extension}";

        return string.IsNullOrEmpty(directory)
            ? databaseFileName
            : Path.Combine(directory, databaseFileName);
    }

    public static string GetConfigPath(string basePath)
        => GetCompatiblePath(basePath, Config);

    public static string GetCompatiblePath(
        string basePath,
        string databaseName)
    {
        var splitPath = GetPath(basePath, databaseName);
        return !File.Exists(splitPath) && File.Exists(basePath)
            ? basePath
            : splitPath;
    }
}
