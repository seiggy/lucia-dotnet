using System.Diagnostics;
using lucia.Data.Telemetry;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace lucia.Data.PostgreSQL;

/// <summary>
/// Probes each PostgreSQL database for its size and connected backends. Npgsql's own
/// <c>Npgsql</c> meter and activity source cover connection pools and queries.
/// </summary>
public sealed partial class PostgresDataStoreHealthProbe(
    IReadOnlyDictionary<string, PostgresConnectionFactory> databases,
    ILogger<PostgresDataStoreHealthProbe> logger) : IDataStoreHealthProbe
{
    private const string Query = """
        SELECT pg_database_size(current_database()),
               (SELECT numbackends FROM pg_stat_database WHERE datname = current_database());
        """;

    public string System => "postgresql";

    public async Task<IReadOnlyList<DataStoreHealthSample>> ProbeAsync(CancellationToken cancellationToken)
    {
        var samples = new List<DataStoreHealthSample>(databases.Count);
        foreach (var (database, factory) in databases)
        {
            samples.Add(await ProbeAsync(database, factory, cancellationToken).ConfigureAwait(false));
        }
        return samples;
    }

    private async Task<DataStoreHealthSample> ProbeAsync(
        string database,
        PostgresConnectionFactory factory,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await using var connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = Query;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            return new DataStoreHealthSample(
                database,
                Up: true,
                Stopwatch.GetElapsedTime(started).TotalSeconds,
                SizeBytes: reader.GetInt64(0),
                Connections: reader.IsDBNull(1) ? null : reader.GetInt32(1));
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            LogProbeFailed(exception, database);
            return new DataStoreHealthSample(database, Up: false, Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "PostgreSQL health probe for {Database} failed.")]
    private partial void LogProbeFailed(Exception exception, string database);
}
