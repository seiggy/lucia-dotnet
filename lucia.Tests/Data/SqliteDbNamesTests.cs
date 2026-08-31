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
    public void GetConfigPath_LegacyDatabaseOnly_CopiesToSplitPath()
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
            AssertLegacyDataCopied(expectedPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(SqliteDbNames.Config)]
    [InlineData(SqliteDbNames.Traces)]
    [InlineData(SqliteDbNames.Tasks)]
    public void GetCompatiblePath_LegacyDatabaseOnly_CopiesToSplitPath(
        string databaseName)
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
            AssertLegacyDataCopied(expectedPath);
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
            CREATE TABLE legacy_data (value TEXT NOT NULL);
            INSERT INTO legacy_data (value) VALUES ('preserved');
            CREATE TABLE schema_version (
                id INTEGER PRIMARY KEY,
                version INTEGER NOT NULL
            );
            INSERT INTO schema_version (id, version) VALUES (1, 3);
            """;
        command.ExecuteNonQuery();
    }

    private static void AssertLegacyDataCopied(string path)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false,
            }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM legacy_data;";
        Assert.Equal("preserved", command.ExecuteScalar());
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
