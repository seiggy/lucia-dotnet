using System.Diagnostics;
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
            NullLogger.Instance, null, "Error doing it", () => Task.FromResult("done"), onFailure: () => failures++);

        Assert.Equal("done", result);
        Assert.Equal(0, failures);
    }

    [Fact]
    public async Task RunAsync_Failure_ReturnsOnlyTheFailureTextAndCountsIt()
    {
        var failures = 0;

        var result = await ToolRunner.RunAsync(
            NullLogger.Instance, null,
            "Unable to get the state of 'x'. Please try again.",
            () => throw new InvalidOperationException("secret connection string"),
            onFailure: () => failures++);

        Assert.Equal("Unable to get the state of 'x'. Please try again.", result);
        Assert.Equal(1, failures);
    }

    [Fact]
    public async Task RunAsync_Failure_LogsTheExceptionAtErrorLevel()
    {
        var logger = new RecordingLogger();
        var exception = new InvalidOperationException("secret connection string");

        await ToolRunner.RunAsync(logger, null, "Error doing it", () => throw exception, detail: "kitchen");

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(exception, entry.Exception);
        Assert.Contains("kitchen", entry.Message);
    }

    [Fact]
    public async Task RunAsync_CallerCancellation_Propagates()
    {
        await Assert.ThrowsAsync<OperationCanceledException>(() => ToolRunner.RunAsync(
            NullLogger.Instance, null, "Error doing it", () => throw new OperationCanceledException()));
    }

    [Fact]
    public async Task RunAsync_TimeoutShapedCancellation_IsAFailure()
    {
        var result = await ToolRunner.RunAsync(
            NullLogger.Instance, null,
            "Error doing it",
            () => throw new TaskCanceledException("timed out", new TimeoutException("request timed out")));

        Assert.StartsWith("Error doing it", result);
    }

    [Fact]
    public async Task RunAsync_WithAToken_PropagatesCancellationOnlyWhenThatTokenIsCancelled()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => ToolRunner.RunAsync(
            NullLogger.Instance,
            null,
            "Error doing it",
            () => throw new OperationCanceledException(cancelled.Token),
            cancellationToken: cancelled.Token));
    }

    [Fact]
    public async Task RunAsync_WithAToken_TreatsAnInternalTimeoutAsAFailure()
    {
        using var caller = new CancellationTokenSource();
        using var internalTimeout = new CancellationTokenSource();
        await internalTimeout.CancelAsync();
        var failures = 0;

        var result = await ToolRunner.RunAsync(
            NullLogger.Instance,
            null,
            "Error doing it",
            () => throw new OperationCanceledException(internalTimeout.Token),
            onFailure: () => failures++,
            cancellationToken: caller.Token);

        Assert.Equal("Error doing it", result);
        Assert.Equal(1, failures);
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
            NullLogger.Instance, null,
            "Error doing it",
            () => fails ? throw new InvalidOperationException("boom") : Task.FromResult("ok"),
            duration: histogram);

        Assert.Single(recorded);
    }

    [Fact]
    public async Task RunAsync_TagsTheDuration()
    {
        using var meter = new Meter("tool-runner-tag-tests");
        var histogram = meter.CreateHistogram<double>("tool.duration");
        var tags = new List<string?>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter == meter)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, _, measurementTags, _) =>
        {
            foreach (var tag in measurementTags)
            {
                tags.Add($"{tag.Key}={tag.Value}");
            }
        });
        listener.Start();

        await ToolRunner.RunAsync(
            NullLogger.Instance, null,
            "Error doing it",
            () => Task.FromResult("ok"),
            duration: histogram,
            durationTag: new("operation", "arm_alarm"));

        Assert.Equal(["operation=arm_alarm"], tags);
    }

    private static ActivityListener ListenTo(ActivitySource source)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = s => s == source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    [Fact]
    public async Task RunAsync_Failure_MarksTheActivityAsErroredAndTagsTheElapsedTime()
    {
        using var source = new ActivitySource("tool-runner-activity-failure");
        using var listener = ListenTo(source);
        using var activity = source.StartActivity("tool");

        await ToolRunner.RunAsync(
            NullLogger.Instance, activity, "Error doing it", () => throw new InvalidOperationException("boom"));

        Assert.NotNull(activity);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("boom", activity.StatusDescription);
        Assert.Contains(activity.TagObjects, tag => tag.Key == "elapsed_ms");
    }

    [Fact]
    public async Task RunAsync_Success_TagsTheElapsedTimeAndLeavesTheStatusAlone()
    {
        using var source = new ActivitySource("tool-runner-activity-success");
        using var listener = ListenTo(source);
        using var activity = source.StartActivity("tool");

        await ToolRunner.RunAsync(NullLogger.Instance, activity, "Error doing it", () => Task.FromResult("ok"));

        Assert.NotNull(activity);
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        Assert.Contains(activity.TagObjects, tag => tag.Key == "elapsed_ms");
    }
}
