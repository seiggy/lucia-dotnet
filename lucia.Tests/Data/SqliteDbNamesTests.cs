using lucia.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace lucia.Tests.Data;

public sealed class SqliteDbNamesTests
{
    [Theory]
    [InlineData(SqliteDbNames.Config, "lucia-config.db")]
    [InlineData(SqliteDbNames.Traces, "lucia-traces.db")]
    [InlineData(SqliteDbNames.Tasks, "lucia-tasks.db")]
    public void GetPath_MapsLogicalDatabaseToSiblingFile(
        string databaseName,
        string expectedFileName)
    {
        var basePath = Path.Combine("data", "lucia.db");

        var result = SqliteDbNames.GetPath(basePath, databaseName);

        Assert.Equal(Path.Combine("data", expectedFileName), result);
    }

    [Fact]
    public void GetConfigPath_LegacyDatabaseOnly_ImportsConfigTables()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"lucia-sqlite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var basePath = Path.Combine(directory, "lucia.db");
        CreateLegacyDatabase(basePath);

        try
        {
            var expectedPath = SqliteDbNames.GetPath(basePath, SqliteDbNames.Config);

            Assert.Equal(expectedPath, SqliteDbNames.GetConfigPath(basePath));
            AssertSelectiveDataCopied(
                expectedPath,
                "configuration",
                "is_sensitive");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(SqliteDbNames.Config, "configuration", "is_sensitive")]
    [InlineData(SqliteDbNames.Traces, "command_traces", "skill_id")]
    [InlineData(SqliteDbNames.Tasks, "scheduled_tasks", "fire_at")]
    public void GetCompatiblePath_LegacyDatabaseOnly_ImportsOwnedTables(
        string databaseName,
        string expectedTable,
        string currentColumn)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"lucia-sqlite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var basePath = Path.Combine(directory, "lucia.db");
        CreateLegacyDatabase(basePath);

        try
        {
            var expectedPath = SqliteDbNames.GetPath(basePath, databaseName);

            Assert.Equal(
                expectedPath,
                SqliteDbNames.GetCompatiblePath(basePath, databaseName));
            AssertSelectiveDataCopied(
                expectedPath,
                expectedTable,
                currentColumn);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GetConfigPath_BothFilesExist_MergesLegacyConfigurationByTimestamp()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"lucia-sqlite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var basePath = Path.Combine(directory, "lucia.db");
        var splitPath = SqliteDbNames.GetPath(basePath, SqliteDbNames.Config);
        CreateConfigurationDatabase(
            basePath,
            [
                ("legacy-only", "legacy", "2026-01-01T00:00:00Z"),
                ("legacy-newer", "legacy", "2026-02-01T00:00:00Z"),
                ("split-newer", "legacy", "2026-01-01T00:00:00Z"),
            ]);
        CreateConfigurationDatabase(
            splitPath,
            [
                ("legacy-newer", "split", "2026-01-01T00:00:00Z"),
                ("split-newer", "split", "2026-02-01T00:00:00Z"),
            ]);

        try
        {
            Assert.Equal(splitPath, SqliteDbNames.GetConfigPath(basePath));
            Assert.Equal("legacy", ReadConfiguration(splitPath, "legacy-only"));
            Assert.Equal("legacy", ReadConfiguration(splitPath, "legacy-newer"));
            Assert.Equal("split", ReadConfiguration(splitPath, "split-newer"));
            DeleteConfiguration(splitPath, "legacy-only");

            _ = SqliteDbNames.GetConfigPath(basePath);

            Assert.Null(ReadConfiguration(splitPath, "legacy-only"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ArchiveLegacyDatabase_AllImportsComplete_ArchivesLegacyDatabase()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"lucia-sqlite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var basePath = Path.Combine(directory, "lucia.db");
        CreateLegacyDatabase(basePath);

        try
        {
            _ = SqliteDbNames.GetCompatiblePath(basePath, SqliteDbNames.Config);
            _ = SqliteDbNames.GetCompatiblePath(basePath, SqliteDbNames.Traces);
            _ = SqliteDbNames.GetCompatiblePath(basePath, SqliteDbNames.Tasks);
            SqliteDbNames.ArchiveLegacyDatabase(basePath);
            SqliteDbNames.ArchiveLegacyDatabase(basePath);

            Assert.False(File.Exists(basePath));
            Assert.True(File.Exists($"{basePath}.legacy"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CreateLegacyDatabase(string path)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false,
            }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE configuration (
                key TEXT PRIMARY KEY,
                value TEXT,
                section TEXT,
                updated_at TEXT NOT NULL,
                updated_by TEXT NOT NULL,
                is_sensitive INTEGER NOT NULL
            );
            INSERT INTO configuration
                (key, value, section, updated_at, updated_by, is_sensitive)
            VALUES ('config-key', 'preserved', 'test', '2026-01-01T00:00:00Z', 'test', 0);
            CREATE TABLE command_traces (
                id TEXT PRIMARY KEY,
                timestamp TEXT NOT NULL,
                clean_text TEXT NOT NULL,
                outcome TEXT NOT NULL,
                confidence REAL NOT NULL,
                total_duration_ms REAL NOT NULL,
                data TEXT NOT NULL
            );
            INSERT INTO command_traces
                (id, timestamp, clean_text, outcome, confidence, total_duration_ms, data)
            VALUES ('trace-1', '2026-01-01T00:00:00Z', 'test', 'handled', 1, 1, '{}');
            CREATE TABLE scheduled_tasks (
                id TEXT PRIMARY KEY,
                status TEXT NOT NULL,
                data TEXT NOT NULL
            );
            INSERT INTO scheduled_tasks (id, status, data)
            VALUES ('task-1', 'pending', '{}');
            CREATE TABLE unrelated_data (value TEXT NOT NULL);
            INSERT INTO unrelated_data (value) VALUES ('do not copy');
            CREATE TABLE schema_version (
                id INTEGER PRIMARY KEY,
                version INTEGER NOT NULL
            );
            INSERT INTO schema_version (id, version) VALUES (1, 3);
            """;
        command.ExecuteNonQuery();
    }

    private static void AssertSelectiveDataCopied(
        string path,
        string expectedTable,
        string currentColumn)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false,
            }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table' AND name = @table;
            """;
        command.Parameters.AddWithValue("@table", expectedTable);
        Assert.Equal(1L, command.ExecuteScalar());
        command.Parameters.Clear();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table' AND name = 'unrelated_data';
            """;
        Assert.Equal(0L, command.ExecuteScalar());
        command.CommandText =
            $"SELECT COUNT(*) FROM pragma_table_info('{expectedTable}') WHERE name = @column;";
        command.Parameters.AddWithValue("@column", currentColumn);
        Assert.Equal(1L, command.ExecuteScalar());
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table' AND name = 'schema_version';
            """;
        Assert.Equal(0L, command.ExecuteScalar());
    }

    private static void CreateConfigurationDatabase(
        string path,
        IEnumerable<(string Key, string Value, string UpdatedAt)> entries)
    {
        using var connection = OpenConnection(path);
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE configuration (
                key TEXT PRIMARY KEY,
                value TEXT,
                section TEXT,
                updated_at TEXT NOT NULL,
                updated_by TEXT NOT NULL,
                is_sensitive INTEGER NOT NULL
            );
            """;
        command.ExecuteNonQuery();
        foreach (var entry in entries)
        {
            command.CommandText = """
                INSERT INTO configuration
                    (key, value, section, updated_at, updated_by, is_sensitive)
                VALUES (@key, @value, 'test', @updatedAt, 'test', 0);
                """;
            command.Parameters.Clear();
            command.Parameters.AddWithValue("@key", entry.Key);
            command.Parameters.AddWithValue("@value", entry.Value);
            command.Parameters.AddWithValue("@updatedAt", entry.UpdatedAt);
            command.ExecuteNonQuery();
        }
    }

    private static string? ReadConfiguration(string path, string key)
    {
        using var connection = OpenConnection(path);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT value FROM configuration WHERE key = @key;";
        command.Parameters.AddWithValue("@key", key);
        return command.ExecuteScalar() as string;
    }

    private static void DeleteConfiguration(string path, string key)
    {
        using var connection = OpenConnection(path);
        using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM configuration WHERE key = @key;";
        command.Parameters.AddWithValue("@key", key);
        command.ExecuteNonQuery();
    }

    private static SqliteConnection OpenConnection(string path)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false,
            }.ToString());
        connection.Open();
        return connection;
    }
}
