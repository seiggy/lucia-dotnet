using System.IO;

using Microsoft.Data.Sqlite;

namespace lucia.Data.Sqlite;

/// <summary>
/// Manages SQLite database connections with WAL mode for concurrent read access.
/// </summary>
public sealed class SqliteConnectionFactory : IDisposable
{
    private readonly string _connectionString;
    private readonly string _databasePath;
    private SqliteConnection? _keepAliveConnection;
    private readonly object _initLock = new();
    private bool _initialized;

    public SqliteConnectionFactory(string databasePath)
    {
        _databasePath = databasePath;

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    /// <summary>
    /// Ensures the database directory exists and the keep-alive connection is open
    /// with WAL mode enabled. Called lazily on first use.
    /// </summary>
    private void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;

            var directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _keepAliveConnection = new SqliteConnection(_connectionString);
            _keepAliveConnection.Open();

            using var cmd = _keepAliveConnection.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
            cmd.ExecuteNonQuery();

            _initialized = true;
        }
    }

    /// <summary>
    /// Creates a new open connection to the SQLite database.
    /// Caller is responsible for disposing the connection.
    /// </summary>
    public SqliteConnection CreateConnection()
    {
        EnsureInitialized();
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();

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
