using lucia.Data.Sqlite;

namespace lucia.Tests.Data;

/// <summary>
/// Creates a temporary SQLite database with the full schema for testing.
/// </summary>
public sealed class SqliteTestHelper : IDisposable
{
    public SqliteConnectionFactory ConnectionFactory { get; }
    private readonly string _dbPath;

    public SqliteTestHelper()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"lucia-test-{Guid.NewGuid():N}.db");
        ConnectionFactory = new SqliteConnectionFactory(_dbPath);
        InitializeSchema();
    }

    private void InitializeSchema()
    {
        using var connection = ConnectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = GetSchemaSQL();
        cmd.ExecuteNonQuery();
    }

    private static string GetSchemaSQL()
    {
        return """
            CREATE TABLE IF NOT EXISTS configuration (
                key TEXT PRIMARY KEY,
                value TEXT,
                section TEXT,
                updated_at TEXT NOT NULL DEFAULT (datetime('now')),
                updated_by TEXT NOT NULL DEFAULT 'system',
                is_sensitive INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS api_keys (
                id TEXT PRIMARY KEY,
                key_hash TEXT NOT NULL UNIQUE,
                key_prefix TEXT NOT NULL,
                name TEXT NOT NULL,
                created_at TEXT NOT NULL DEFAULT (datetime('now')),
                last_used_at TEXT,
                expires_at TEXT,
                is_revoked INTEGER NOT NULL DEFAULT 0,
                revoked_at TEXT,
                scopes TEXT NOT NULL DEFAULT '["*"]'
            );
            CREATE INDEX IF NOT EXISTS idx_api_keys_hash ON api_keys(key_hash);
            """;
    }

    public void Dispose()
    {
        ConnectionFactory.Dispose();
        try { File.Delete(_dbPath); } catch { /* best-effort cleanup */ }
    }
}
