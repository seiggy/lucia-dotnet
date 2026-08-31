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
}
