using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace lucia.Data.PostgreSQL;

/// <summary>
/// Configuration provider that reads key-value pairs from a PostgreSQL <c>configuration</c> table.
/// Supports periodic polling for change detection to enable IOptionsMonitor hot-reload.
/// </summary>
public sealed class PostgresConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly PostgresConnectionFactory _connectionFactory;
    private readonly ILogger _logger;
    private readonly TimeSpan _pollInterval;
    private Timer? _pollTimer;
    private long _lastLoadRowVersion;
    private int _isPolling;

    public PostgresConfigurationProvider(
        PostgresConnectionFactory connectionFactory,
        ILoggerFactory? loggerFactory = null,
        TimeSpan? pollInterval = null)
    {
        _connectionFactory = connectionFactory;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<PostgresConfigurationProvider>();
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(5);
    }

    public override void Load()
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();

            // Ensure the configuration table exists (may be called before migration runner)
            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = """
                CREATE TABLE IF NOT EXISTS configuration (
                    key TEXT PRIMARY KEY,
                    value TEXT,
                    section TEXT,
                    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                    updated_by TEXT NOT NULL DEFAULT 'system',
                    is_sensitive BOOLEAN NOT NULL DEFAULT FALSE
                );
                """;
            createCmd.ExecuteNonQuery();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT key, value FROM configuration;";

            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var key = reader.GetString(0);
                var value = reader.IsDBNull(1) ? null : reader.GetString(1);
                data[key] = value;
            }

            Data = data;

            // Track row count as a simple change-detection proxy
            using var countCmd = connection.CreateCommand();
            countCmd.CommandText = "SELECT COUNT(*) FROM configuration;";
            _lastLoadRowVersion = Convert.ToInt64(countCmd.ExecuteScalar());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load configuration from PostgreSQL");
        }

        _pollTimer ??= new Timer(PollForChanges, null, _pollInterval, _pollInterval);
    }

    private async void PollForChanges(object? state)
    {
        if (Interlocked.CompareExchange(ref _isPolling, 1, 0) != 0) return;

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync().ConfigureAwait(false);

            await using var checkCmd = connection.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*), COALESCE(MAX(updated_at)::text, '') FROM configuration;";
            await using var checkReader = await checkCmd.ExecuteReaderAsync().ConfigureAwait(false);

            if (!await checkReader.ReadAsync().ConfigureAwait(false))
            {
                return;
            }

            var currentCount = checkReader.GetInt64(0);
            var maxUpdatedAt = checkReader.GetString(1);
            var currentVersion = currentCount ^ maxUpdatedAt.GetHashCode();

            // Close the first reader before issuing a second query on the same connection
            await checkReader.CloseAsync().ConfigureAwait(false);

            if (currentVersion != _lastLoadRowVersion)
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT key, value FROM configuration;";

                var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    var key = reader.GetString(0);
                    var value = reader.IsDBNull(1) ? null : reader.GetString(1);
                    data[key] = value;
                }

                Data = data;
                _lastLoadRowVersion = currentVersion;
                OnReload();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PostgreSQL configuration poll failed");
        }
        finally
        {
            Interlocked.Exchange(ref _isPolling, 0);
        }
    }

    public void Dispose()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
    }
}
