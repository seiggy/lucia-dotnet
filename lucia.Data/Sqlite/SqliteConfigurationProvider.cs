using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace lucia.Data.Sqlite;

/// <summary>
/// Configuration provider that reads key-value pairs from SQLite.
/// Supports periodic polling for change detection to enable IOptionsMonitor hot-reload.
/// </summary>
public sealed class SqliteConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly TimeSpan _pollInterval;
    private Timer? _pollTimer;
    private string _lastLoadTimestamp = DateTime.MinValue.ToString("O");
    private volatile bool _isPolling;

    public SqliteConfigurationProvider(SqliteConnectionFactory connectionFactory, TimeSpan? pollInterval = null)
    {
        _connectionFactory = connectionFactory;
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
                    updated_at TEXT NOT NULL DEFAULT (datetime('now')),
                    updated_by TEXT NOT NULL DEFAULT 'system',
                    is_sensitive INTEGER NOT NULL DEFAULT 0
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
            _lastLoadTimestamp = DateTime.UtcNow.ToString("O");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[lucia] SqliteConfigurationProvider: Failed to load from SQLite \u2014 {ex.Message}");
        }

        _pollTimer ??= new Timer(PollForChanges, null, _pollInterval, _pollInterval);
    }

    private async void PollForChanges(object? state)
    {
        if (_isPolling) return;
        _isPolling = true;

        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var checkCmd = connection.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*) FROM configuration WHERE updated_at > @since;";
            checkCmd.Parameters.AddWithValue("@since", _lastLoadTimestamp);

            var changedCount = Convert.ToInt32(await checkCmd.ExecuteScalarAsync().ConfigureAwait(false));

            if (changedCount > 0)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT key, value FROM configuration;";

                var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    var key = reader.GetString(0);
                    var value = reader.IsDBNull(1) ? null : reader.GetString(1);
                    data[key] = value;
                }

                Data = data;
                _lastLoadTimestamp = DateTime.UtcNow.ToString("O");
                OnReload();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[lucia] SqliteConfigurationProvider: Poll failed \u2014 {ex.Message}");
        }
        finally
        {
            _isPolling = false;
        }
    }

    public void Dispose()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
    }
}
