using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace lucia.Data.Telemetry;

/// <summary>
/// Publishes data store health gauges (<c>lucia.datastore.*</c>). Probes run every
/// <see cref="Interval"/> and only while a metrics exporter is listening, so the databases
/// are not queried when telemetry is off.
/// </summary>
public sealed partial class DataStoreHealthMonitor : BackgroundService
{
    public const string MeterName = "lucia.DataStore";

    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly IDataStoreHealthProbe _probe;
    private readonly ILogger<DataStoreHealthMonitor> _logger;
    private readonly ObservableGauge<int> _up;
    private IReadOnlyList<DataStoreHealthSample> _samples = [];

    public DataStoreHealthMonitor(
        IDataStoreHealthProbe probe,
        IMeterFactory meterFactory,
        ILogger<DataStoreHealthMonitor> logger)
    {
        _probe = probe;
        _logger = logger;
        var meter = meterFactory.Create(MeterName);
        _up = meter.CreateObservableGauge(
            "lucia.datastore.up",
            () => Observe(sample => sample.Up ? 1 : 0),
            description: "1 when the database answered its health probe, otherwise 0.");
        meter.CreateObservableGauge(
            "lucia.datastore.probe.duration",
            () => Observe(sample => sample.ProbeSeconds),
            unit: "s",
            description: "Time taken by the most recent health probe.");
        meter.CreateObservableGauge(
            "lucia.datastore.size",
            () => Observe(sample => sample.SizeBytes),
            unit: "By",
            description: "On-disk size of the database.");
        meter.CreateObservableGauge(
            "lucia.datastore.wal.size",
            () => Observe(sample => sample.WalBytes),
            unit: "By",
            description: "Size of the SQLite write-ahead log.");
        meter.CreateObservableGauge(
            "lucia.datastore.connections",
            () => Observe(sample => sample.Connections),
            unit: "{connection}",
            description: "Backends connected to the PostgreSQL database.");
    }

    /// <summary>Probes once and keeps the result for the gauges. Skips the probe when nothing is listening.</summary>
    internal async Task SampleAsync(CancellationToken cancellationToken)
    {
        if (!_up.Enabled)
        {
            return;
        }
        _samples = await _probe.ProbeAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await SampleAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogProbeFailed(exception, _probe.System);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private IEnumerable<Measurement<T>> Observe<T>(Func<DataStoreHealthSample, T?> select)
        where T : struct
    {
        foreach (var sample in _samples)
        {
            if (select(sample) is { } value)
            {
                yield return new Measurement<T>(
                    value,
                    new KeyValuePair<string, object?>("db.system.name", _probe.System),
                    new KeyValuePair<string, object?>("db.namespace", sample.Database));
            }
        }
    }

    private IEnumerable<Measurement<T>> Observe<T>(Func<DataStoreHealthSample, T> select)
        where T : struct
        => Observe<T>(sample => (T?)select(sample));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Data store health probe for {System} failed.")]
    private partial void LogProbeFailed(Exception exception, string system);
}
