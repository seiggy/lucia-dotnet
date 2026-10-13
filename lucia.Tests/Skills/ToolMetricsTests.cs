using System.Diagnostics;
using System.Diagnostics.Metrics;
using lucia.Agents.Skills;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Skills;

public sealed class ToolMetricsTests
{
    private sealed record Measurement(string Instrument, double Value, string? Tool);

    private static List<Measurement> Listen(Meter meter)
    {
        var measurements = new List<Measurement>();
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter == meter)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((i, value, tags, _) => measurements.Add(new(i.Name, value, ToolTag(tags))));
        listener.SetMeasurementEventCallback<double>((i, value, tags, _) => measurements.Add(new(i.Name, value, ToolTag(tags))));
        listener.Start();
        return measurements;
    }

    private static string? ToolTag(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == "tool")
            {
                return tag.Value?.ToString();
            }
        }

        return null;
    }

    [Fact]
    public async Task RunAsync_CountsTheRequestAndRecordsTheDurationPerTool()
    {
        using var meter = new Meter("tool-metrics-success");
        var measurements = Listen(meter);
        var metrics = ToolMetrics.Create(meter, "demo.lookup", "demo lookup").ForTool("GetThing");

        await ToolRunner.RunAsync(NullLogger.Instance, null, metrics, "Error doing it", () => Task.FromResult("ok"));

        Assert.Equal(["demo.lookup.requests", "demo.lookup.duration"], measurements.Select(m => m.Instrument).ToArray());
        Assert.All(measurements, m => Assert.Equal("GetThing", m.Tool));
    }

    [Fact]
    public async Task RunAsync_Failure_CountsTheFailure()
    {
        using var meter = new Meter("tool-metrics-failure");
        var measurements = Listen(meter);
        var metrics = ToolMetrics.Create(meter, "demo.lookup", "demo lookup").ForTool("GetThing");

        await ToolRunner.RunAsync(
            NullLogger.Instance, null, metrics, "Error doing it", () => throw new InvalidOperationException("boom"));

        Assert.Contains(measurements, m => m.Instrument == "demo.lookup.failures" && m.Value == 1 && m.Tool == "GetThing");
    }

    [Fact]
    public async Task RunAsync_CallerCancellation_RecordsTheDurationButNoFailure()
    {
        using var meter = new Meter("tool-metrics-cancel");
        var measurements = Listen(meter);
        var metrics = ToolMetrics.Create(meter, "demo.lookup", "demo lookup").ForTool("GetThing");

        await Assert.ThrowsAsync<OperationCanceledException>(() => ToolRunner.RunAsync(
            NullLogger.Instance, null, metrics, "Error doing it", () => throw new OperationCanceledException()));

        Assert.DoesNotContain(measurements, m => m.Instrument == "demo.lookup.failures");
        Assert.Contains(measurements, m => m.Instrument == "demo.lookup.duration");
    }

    [Fact]
    public void ForTool_SharesTheInstrumentsOfItsGroup()
    {
        using var meter = new Meter("tool-metrics-share");
        var measurements = Listen(meter);
        var group = ToolMetrics.Create(meter, "demo.lookup", "demo lookup");

        group.ForTool("A").Started();
        group.ForTool("B").Started();

        Assert.Equal(new string?[] { "A", "B" }, measurements.Select(m => m.Tool).ToArray());
        Assert.Single(measurements.Select(m => m.Instrument).Distinct());
    }
}
