using System.Diagnostics;
using lucia.Data.Telemetry;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace lucia.Data.Sqlite;

/// <summary>
/// Probes each SQLite database with <c>SELECT 1</c> and reads the database and write-ahead log
/// file sizes. Microsoft.Data.Sqlite has no built-in metrics, so this is the only SQLite telemetry.
/// </summary>
public sealed partial class SqliteDataStoreHealthProbe(
    IReadOnlyDictionary<string, SqliteConnectionFactory> databases,
    ILogger<SqliteDataStoreHealthProbe> logger) : IDataStoreHealthProbe
{
    public string System => "sqlite";

    public Task<IReadOnlyList<DataStoreHealthSample>> ProbeAsync(CancellationToken cancellationToken)
    {
        var samples = new List<DataStoreHealthSample>(databases.Count);
        foreach (var (database, factory) in databases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            samples.Add(Probe(database, factory));
        }
        return Task.FromResult<IReadOnlyList<DataStoreHealthSample>>(samples);
    }

    private DataStoreHealthSample Probe(string database, SqliteConnectionFactory factory)
    {
        var path = new SqliteConnectionStringBuilder(factory.ConnectionString).DataSource;
        var started = Stopwatch.GetTimestamp();
        bool up;
        try
        {
            using var connection = factory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            command.ExecuteScalar();
            up = true;
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            LogProbeFailed(exception, database);
            up = false;
        }
        return new DataStoreHealthSample(
            database,
            up,
            Stopwatch.GetElapsedTime(started).TotalSeconds,
            SizeBytes: FileLength(path),
            WalBytes: FileLength(path + "-wal") ?? (up ? 0 : null));
    }

    private static long? FileLength(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? file.Length : null;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "SQLite health probe for {Database} failed.")]
    private partial void LogProbeFailed(Exception exception, string database);
}
