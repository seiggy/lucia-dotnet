using Npgsql;

namespace lucia.Data.PostgreSQL;

/// <summary>
/// Manages PostgreSQL database connections for lightweight repository implementations.
/// </summary>
public sealed class PostgresConnectionFactory : IAsyncDisposable, IDisposable
{
    private readonly string _connectionString;
    private readonly NpgsqlDataSource _dataSource;

    public PostgresConnectionFactory(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _connectionString = connectionString;
        _dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
    }

    /// <summary>
    /// Creates a new open connection to the PostgreSQL database.
    /// </summary>
    public async ValueTask<NpgsqlConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        return await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a new open connection to the PostgreSQL database.
    /// </summary>
    public NpgsqlConnection CreateConnection()
    {
        return _dataSource.OpenConnection();
    }

    /// <summary>
    /// The connection string for the PostgreSQL database.
    /// </summary>
    public string ConnectionString => _connectionString;

    public ValueTask DisposeAsync()
    {
        return _dataSource.DisposeAsync();
    }

    public void Dispose()
    {
        _dataSource.Dispose();
    }
}
