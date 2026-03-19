using System.IO;

using Microsoft.Data.Sqlite;

namespace lucia.Data.Sqlite;

/// <summary>
/// Manages SQLite database connections with WAL mode for concurrent read access.
/// </summary>
public sealed class SqliteConnectionFactory : IDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _keepAliveConnection;

    public SqliteConnectionFactory(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();

        // Keep one connection alive to prevent the in-memory schema from being lost
        // and to maintain WAL mode for the lifetime of the application
        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();

        using var cmd = _keepAliveConnection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Creates a new open connection to the SQLite database.
    /// Caller is responsible for disposing the connection.
    /// </summary>
    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>
    /// The connection string for the SQLite database.
    /// </summary>
    public string ConnectionString => _connectionString;

    public void Dispose()
    {
        _keepAliveConnection?.Dispose();
        _keepAliveConnection = null;
    }
}
