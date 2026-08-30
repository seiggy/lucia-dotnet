using lucia.Data.Sqlite;

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
    public void GetConfigPath_LegacyDatabaseOnly_UsesLegacyPath()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"lucia-sqlite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var basePath = Path.Combine(directory, "lucia.db");
        File.WriteAllText(basePath, string.Empty);

        try
        {
            Assert.Equal(basePath, SqliteDbNames.GetConfigPath(basePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
