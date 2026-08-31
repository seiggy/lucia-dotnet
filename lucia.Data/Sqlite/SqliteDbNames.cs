using Microsoft.Data.Sqlite;

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
    {
        var configPath = GetCompatiblePath(basePath, Config);
        if (File.Exists(basePath) && File.Exists(configPath))
        {
            MergeLegacyConfiguration(basePath, configPath);
        }

        return configPath;
    }

    public static string GetCompatiblePath(
        string basePath,
        string databaseName)
    {
        var splitPath = GetPath(basePath, databaseName);
        if (!File.Exists(splitPath) && File.Exists(basePath))
        {
            CopyDatabase(basePath, splitPath);
        }

        return splitPath;
    }

    private static void CopyDatabase(string sourcePath, string destinationPath)
    {
        var temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var sourceConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = sourcePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString();
            var destinationConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = temporaryPath,
                Pooling = false,
            }.ToString();
            using (var source = new SqliteConnection(sourceConnectionString))
            using (var destination = new SqliteConnection(destinationConnectionString))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
                using var command = destination.CreateCommand();
                command.CommandText = "DROP TABLE IF EXISTS schema_version;";
                command.ExecuteNonQuery();
            }

            File.Move(temporaryPath, destinationPath);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static void MergeLegacyConfiguration(
        string legacyPath,
        string configPath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = configPath,
            Pooling = false,
        }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var attachCommand = connection.CreateCommand();
        attachCommand.CommandText = "ATTACH DATABASE @legacyPath AS legacy;";
        attachCommand.Parameters.AddWithValue("@legacyPath", legacyPath);
        attachCommand.ExecuteNonQuery();

        using var tableCommand = connection.CreateCommand();
        tableCommand.CommandText = """
            SELECT COUNT(*)
            FROM legacy.sqlite_master
            WHERE type = 'table' AND name = 'configuration';
            """;
        if ((long)tableCommand.ExecuteScalar()! == 0)
        {
            return;
        }

        using var transaction = connection.BeginTransaction();
        using var mergeCommand = connection.CreateCommand();
        mergeCommand.Transaction = transaction;
        mergeCommand.CommandText = """
            CREATE TABLE IF NOT EXISTS configuration (
                key TEXT PRIMARY KEY,
                value TEXT,
                section TEXT,
                updated_at TEXT NOT NULL DEFAULT (datetime('now')),
                updated_by TEXT NOT NULL DEFAULT 'system',
                is_sensitive INTEGER NOT NULL DEFAULT 0
            );
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
            """;
        mergeCommand.ExecuteNonQuery();
        transaction.Commit();
    }
}
