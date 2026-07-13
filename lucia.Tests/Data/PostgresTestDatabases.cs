using lucia.Data.PostgreSQL;
using Npgsql;

namespace lucia.Tests.Data;

public sealed class PostgresTestDatabases : IAsyncDisposable
{
    private PostgresTestDatabases(
        PostgresConnectionFactory config,
        PostgresConnectionFactory traces,
        PostgresConnectionFactory tasks)
    {
        Config = config;
        Traces = traces;
        Tasks = tasks;
    }

    public PostgresConnectionFactory Config { get; }

    public PostgresConnectionFactory Traces { get; }

    public PostgresConnectionFactory Tasks { get; }

    public static async Task<PostgresTestDatabases> CreateAsync(string adminConnectionString)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var names = new[] { $"config_{suffix}", $"traces_{suffix}", $"tasks_{suffix}" };

        await using (var connection = new NpgsqlConnection(adminConnectionString))
        {
            await connection.OpenAsync();
            foreach (var name in names)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE DATABASE \"{name}\";";
                await command.ExecuteNonQueryAsync();
            }
        }

        return new(
            CreateFactory(adminConnectionString, names[0]),
            CreateFactory(adminConnectionString, names[1]),
            CreateFactory(adminConnectionString, names[2]));
    }

    public async ValueTask DisposeAsync()
    {
        await Config.DisposeAsync();
        await Traces.DisposeAsync();
        await Tasks.DisposeAsync();
    }

    private static PostgresConnectionFactory CreateFactory(string connectionString, string database)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = database,
            Pooling = false,
        };
        return new(builder.ConnectionString);
    }
}
