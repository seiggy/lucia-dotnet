using lucia.Data.Sqlite;

namespace lucia.Tests.Data;

public sealed class SqliteConnectionFactoryTests : IDisposable
{
    private readonly string _dbPath;

    public SqliteConnectionFactoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"lucia-connfactory-{Guid.NewGuid():N}.db");
    }

    [Fact]
    public void Constructor_CreatesDirectoryIfNeeded()
    {
        var nestedPath = Path.Combine(Path.GetTempPath(), $"lucia-nested-{Guid.NewGuid():N}", "sub", "test.db");

        using var factory = new SqliteConnectionFactory(nestedPath);

        Assert.True(Directory.Exists(Path.GetDirectoryName(nestedPath)));

        // Cleanup nested directory
        Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(nestedPath))!, true);
    }

    [Fact]
    public void CreateConnection_ReturnsOpenConnection()
    {
        using var factory = new SqliteConnectionFactory(_dbPath);

        using var connection = factory.CreateConnection();

        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
    }

    [Fact]
    public void CreateConnection_MultipleConnectionsCanBeCreated()
    {
        using var factory = new SqliteConnectionFactory(_dbPath);

        using var conn1 = factory.CreateConnection();
        using var conn2 = factory.CreateConnection();

        Assert.Equal(System.Data.ConnectionState.Open, conn1.State);
        Assert.Equal(System.Data.ConnectionState.Open, conn2.State);
    }

    [Fact]
    public void ConnectionString_IsNotEmpty()
    {
        using var factory = new SqliteConnectionFactory(_dbPath);

        Assert.False(string.IsNullOrWhiteSpace(factory.ConnectionString));
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var factory = new SqliteConnectionFactory(_dbPath);

        var exception = Record.Exception(() => factory.Dispose());

        Assert.Null(exception);
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { /* best-effort cleanup */ }
    }
}
