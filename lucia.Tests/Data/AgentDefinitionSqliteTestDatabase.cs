using lucia.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace lucia.Tests.Data;

internal sealed class AgentDefinitionSqliteTestDatabase : IDisposable
{
    private readonly string _databasePath;

    public AgentDefinitionSqliteTestDatabase()
    {
        _databasePath = Path.Combine(
            AppContext.BaseDirectory,
            $"agent-definition-{Guid.NewGuid():N}.db");
        ConnectionFactory = new SqliteConnectionFactory(_databasePath);

        using var connection = ConnectionFactory.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE agent_definitions (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                enabled INTEGER NOT NULL DEFAULT 1,
                data TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();

        Repository = new SqliteAgentDefinitionRepository(ConnectionFactory);
    }

    public SqliteConnectionFactory ConnectionFactory { get; }

    public SqliteAgentDefinitionRepository Repository { get; }

    public void Dispose()
    {
        ConnectionFactory.Dispose();
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
        File.Delete($"{_databasePath}-shm");
        File.Delete($"{_databasePath}-wal");
    }
}
