using System.Diagnostics.Metrics;
using lucia.Agents.Skills;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Skills;

public sealed class ToolRunnerTests
{
    [Fact]
    public async Task RunAsync_Success_ReturnsTheBodyResultAndSkipsFailureHandling()
    {
        var failures = 0;

        var result = await ToolRunner.RunAsync(
            NullLogger.Instance, "Error doing it", () => Task.FromResult("done"), onFailure: () => failures++);

        Assert.Equal("done", result);
        Assert.Equal(0, failures);
    }

    [Fact]
    public async Task RunAsync_Failure_ReturnsTheFailureTextAndCountsIt()
    {
        var failures = 0;

        var result = await ToolRunner.RunAsync(
            NullLogger.Instance,
            "Error getting state for 'x'",
            () => throw new InvalidOperationException("boom"),
            onFailure: () => failures++);

        Assert.Equal("Error getting state for 'x': boom", result);
        Assert.Equal(1, failures);
    }

    [Fact]
    public async Task RunAsync_Failure_LogsTheExceptionAtErrorLevel()
    {
        var logger = new RecordingLogger();
        var exception = new InvalidOperationException("boom");

        await ToolRunner.RunAsync(logger, "Error doing it", () => throw exception, detail: "kitchen");

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(exception, entry.Exception);
        Assert.Contains("kitchen", entry.Message);
    }

    [Fact]
    public async Task RunAsync_WithoutTheExceptionMessage_ReturnsTheFailureTextAlone()
    {
        var logger = new RecordingLogger();

        var result = await ToolRunner.RunAsync(
            logger,
            "Unable to query sensor data. Please try again.",
            () => throw new InvalidOperationException("secret connection string"),
            includeExceptionMessage: false);

        Assert.Equal("Unable to query sensor data. Please try again.", result);
        Assert.Contains(logger.Entries, entry => entry.Exception?.Message == "secret connection string");
    }

    [Fact]
    public async Task RunAsync_CallerCancellation_Propagates()
    {
        await Assert.ThrowsAsync<OperationCanceledException>(() => ToolRunner.RunAsync(
            NullLogger.Instance, "Error doing it", () => throw new OperationCanceledException()));
    }

    [Fact]
    public async Task RunAsync_TimeoutShapedCancellation_IsAFailure()
    {
        var result = await ToolRunner.RunAsync(
            NullLogger.Instance,
            "Error doing it",
            () => throw new TaskCanceledException("timed out", new TimeoutException("request timed out")));

        Assert.StartsWith("Error doing it: ", result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_RecordsTheDurationWhetherOrNotTheBodyFails(bool fails)
    {
        using var meter = new Meter("tool-runner-tests");
        var histogram = meter.CreateHistogram<double>("tool.duration");
        var recorded = new List<double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter == meter)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, value, _, _) => recorded.Add(value));
        listener.Start();

        await ToolRunner.RunAsync(
            NullLogger.Instance,
            "Error doing it",
            () => fails ? throw new InvalidOperationException("boom") : Task.FromResult("ok"),
            duration: histogram);

        Assert.Single(recorded);
    }
}
