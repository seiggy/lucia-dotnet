using Microsoft.Data.Sqlite;

namespace lucia.Data.Sqlite;

/// <summary>
/// Well-known database names for SQLite multi-file service resolution.
/// Mirrors the MongoDB/PostgreSQL three-database pattern.
/// </summary>
public static class SqliteDbNames
{
    private const int LegacyImportMarker = 0x4C554349;
    private static readonly string[] s_configTables =
    [
        "configuration",
        "api_keys",
        "model_providers",
        "agent_definitions",
        "mcp_tool_servers",
        "response_templates",
        "presence_sensor_mappings",
        "presence_config",
        "plugin_repositories",
        "installed_plugins",
        "voice_transcripts",
        "speaker_profiles",
        "model_preferences",
        "user_memories",
    ];
    private static readonly string[] s_traceTables =
    [
        "conversation_traces",
        "dataset_exports",
        "command_traces",
    ];
    private static readonly string[] s_taskTables =
    [
        "scheduled_tasks",
        "alarm_clocks",
        "alarm_sounds",
        "archived_tasks",
    ];

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
        if (File.Exists(basePath))
        {
            ImportLegacyTables(
                basePath,
                splitPath,
                databaseName switch
                {
                    Config => s_configTables,
                    Traces => s_traceTables,
                    Tasks => s_taskTables,
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(databaseName),
                        databaseName,
                        "Unknown SQLite database name.")
                });
        }

        return splitPath;
    }

    private static void ImportLegacyTables(
        string legacyPath,
        string splitPath,
        IReadOnlyList<string> tables)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = splitPath,
            Pooling = false,
        }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        if (GetUserVersion(connection) == LegacyImportMarker)
        {
            return;
        }

        using var attachCommand = connection.CreateCommand();
        attachCommand.CommandText = "ATTACH DATABASE @legacyPath AS legacy;";
        attachCommand.Parameters.AddWithValue("@legacyPath", legacyPath);
        attachCommand.ExecuteNonQuery();

        using var transaction = connection.BeginTransaction();
        foreach (var table in tables)
        {
            using var schemaCommand = connection.CreateCommand();
            schemaCommand.Transaction = transaction;
            schemaCommand.CommandText = """
                SELECT sql
                FROM legacy.sqlite_master
                WHERE type = 'table' AND name = @table;
                """;
            schemaCommand.Parameters.AddWithValue("@table", table);
            if (schemaCommand.ExecuteScalar() is not string createTableSql)
            {
                continue;
            }

            using var tableExistsCommand = connection.CreateCommand();
            tableExistsCommand.Transaction = transaction;
            tableExistsCommand.CommandText = """
                SELECT COUNT(*)
                FROM main.sqlite_master
                WHERE type = 'table' AND name = @table;
                """;
            tableExistsCommand.Parameters.AddWithValue("@table", table);
            if ((long)tableExistsCommand.ExecuteScalar()! == 0)
            {
                using var createCommand = connection.CreateCommand();
                createCommand.Transaction = transaction;
                createCommand.CommandText = createTableSql;
                createCommand.ExecuteNonQuery();
            }

            using var importCommand = connection.CreateCommand();
            importCommand.Transaction = transaction;
            importCommand.CommandText = table == "configuration"
                ? """
                  INSERT INTO configuration
                      (key, value, section, updated_at, updated_by, is_sensitive)
                  SELECT
                      key, value, section, updated_at, updated_by, is_sensitive
                  FROM legacy.configuration
                  WHERE true
                  ON CONFLICT(key) DO UPDATE SET
                      value = excluded.value,
                      section = excluded.section,
                      updated_at = excluded.updated_at,
                      updated_by = excluded.updated_by,
                      is_sensitive = excluded.is_sensitive
                  WHERE julianday(excluded.updated_at)
                      > julianday(configuration.updated_at);
                  """
                : $"""
                   INSERT OR IGNORE INTO "{table}"
                   SELECT * FROM legacy."{table}";
                   """;
            importCommand.ExecuteNonQuery();
        }

        using var markerCommand = connection.CreateCommand();
        markerCommand.Transaction = transaction;
        markerCommand.CommandText =
            $"PRAGMA user_version = {LegacyImportMarker};";
        markerCommand.ExecuteNonQuery();
        transaction.Commit();
    }

    private static int GetUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    internal static void ArchiveLegacyDatabase(string basePath)
    {
        var allImportsComplete = new[] { Config, Traces, Tasks }
            .Select(databaseName => GetPath(basePath, databaseName))
            .All(path =>
                File.Exists(path)
                && GetUserVersion(path) == LegacyImportMarker);
        if (!allImportsComplete)
        {
            return;
        }

        var archivePath = $"{basePath}.legacy";
        if (File.Exists(archivePath))
        {
            archivePath = $"{archivePath}.{Guid.NewGuid():N}";
        }
        File.Move(basePath, archivePath);
    }

    private static int GetUserVersion(string path)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        return GetUserVersion(connection);
    }
}
