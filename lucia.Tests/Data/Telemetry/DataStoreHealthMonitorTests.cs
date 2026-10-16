using System.Diagnostics.Metrics;
using lucia.Data.Telemetry;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Data.Telemetry;

public sealed class DataStoreHealthMonitorTests
{
    [Fact]
    public async Task SampleAsync_PublishesHealthGaugesPerDatabase()
    {
        var probe = new FakeDataStoreHealthProbe(
            "sqlite",
            new DataStoreHealthSample("luciaconfig", Up: true, ProbeSeconds: 0.002, SizeBytes: 4096, WalBytes: 512),
            new DataStoreHealthSample("luciatraces", Up: false, ProbeSeconds: 0.5, SizeBytes: 8192, WalBytes: 0));
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var monitor = new DataStoreHealthMonitor(
            probe,
            services.GetRequiredService<IMeterFactory>(),
            NullLogger<DataStoreHealthMonitor>.Instance);
        var measurements = new List<(string Name, double Value, string? Database, string? System)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == DataStoreHealthMonitor.MeterName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<int>((i, value, tags, _) =>
            measurements.Add((i.Name, value, Tag(tags, "db.namespace"), Tag(tags, "db.system.name"))));
        listener.SetMeasurementEventCallback<long>((i, value, tags, _) =>
            measurements.Add((i.Name, value, Tag(tags, "db.namespace"), Tag(tags, "db.system.name"))));
        listener.SetMeasurementEventCallback<double>((i, value, tags, _) =>
            measurements.Add((i.Name, value, Tag(tags, "db.namespace"), Tag(tags, "db.system.name"))));
        listener.Start();

        await monitor.SampleAsync(CancellationToken.None);
        listener.RecordObservableInstruments();

        Assert.Equal(1, probe.ProbeCount);
        Assert.Contains(("lucia.datastore.up", 1d, "luciaconfig", "sqlite"), measurements);
        Assert.Contains(("lucia.datastore.up", 0d, "luciatraces", "sqlite"), measurements);
        Assert.Contains(("lucia.datastore.size", 4096d, "luciaconfig", "sqlite"), measurements);
        Assert.Contains(("lucia.datastore.wal.size", 512d, "luciaconfig", "sqlite"), measurements);
        Assert.Contains(("lucia.datastore.probe.duration", 0.5d, "luciatraces", "sqlite"), measurements);
        Assert.DoesNotContain(measurements, m => m.Name == "lucia.datastore.connections");
    }

    [Fact]
    public async Task SampleAsync_SkipsTheProbeWhenNothingIsListening()
    {
        var probe = new FakeDataStoreHealthProbe(
            "postgresql",
            new DataStoreHealthSample("luciaconfig", Up: true, ProbeSeconds: 0.01));
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var monitor = new DataStoreHealthMonitor(
            probe,
            services.GetRequiredService<IMeterFactory>(),
            NullLogger<DataStoreHealthMonitor>.Instance);

        await monitor.SampleAsync(CancellationToken.None);

        Assert.Equal(0, probe.ProbeCount);
    }

    private static string? Tag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == key)
            {
                return tag.Value?.ToString();
            }
        }
        return null;
    }
}
