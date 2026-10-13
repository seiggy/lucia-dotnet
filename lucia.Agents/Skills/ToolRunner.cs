using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Skills;

/// <summary>
/// Runs the body of a skill tool and turns a failure into the text the model sees. One place decides what
/// leaves the process when a tool fails. Caller cancellation propagates. A timeout counts as a failure.
/// </summary>
public static partial class ToolRunner
{
    /// <param name="logger">Receives the full exception.</param>
    /// <param name="activity">
    /// The tool's span, or null when nothing listens. A failure sets its status to Error with the exception
    /// message, and it always gets the elapsed time as the <c>elapsed_ms</c> tag. Telemetry keeps the detail
    /// the model does not see.
    /// </param>
    /// <param name="failure">
    /// The complete message returned to the model on failure. It must not carry exception details, which
    /// can hold connection strings, paths and addresses. The log gets the exception.
    /// </param>
    /// <param name="body">The tool's work. Returns the success text.</param>
    /// <param name="onFailure">Runs on failure, for failure counters.</param>
    /// <param name="duration">Records how long the tool took, whether or not it failed.</param>
    /// <param name="detail">Extra context for the log only, such as the search term.</param>
    /// <param name="durationTag">Tag added to the duration measurement, such as the operation name.</param>
    /// <param name="cancellationToken">
    /// The caller's token. With one, only a cancellation of that token propagates, and any other cancellation,
    /// such as an internal timeout, is a failure. Without one, <see cref="ExceptionExtensions.IsCancellation"/> decides.
    /// </param>
    /// <param name="metrics">Counts the request and the failure and records the duration, all tagged with the tool.</param>
    public static async Task<string> RunAsync(
        ILogger logger,
        Activity? activity,
        string failure,
        Func<Task<string>> body,
        Action? onFailure = null,
        Histogram<double>? duration = null,
        string? detail = null,
        KeyValuePair<string, object?>? durationTag = null,
        ToolMetrics? metrics = null,
        CancellationToken cancellationToken = default)
    {
        metrics?.Started();
        var start = Stopwatch.GetTimestamp();
        try
        {
            return await body().ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
        {
            LogFailure(logger, ex, failure, detail);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            onFailure?.Invoke();
            metrics?.Failed();
            return failure;
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            activity?.SetTag("elapsed_ms", elapsedMs);
            metrics?.RecordDuration(elapsedMs);
            if (durationTag is { } tag)
            {
                duration?.Record(elapsedMs, tag);
            }
            else
            {
                duration?.Record(elapsedMs);
            }
        }
    }

    /// <summary>Runs a tool that reports through <see cref="ToolMetrics"/> and has no other callbacks.</summary>
    public static Task<string> RunAsync(
        ILogger logger,
        Activity? activity,
        ToolMetrics metrics,
        string failure,
        Func<Task<string>> body,
        string? detail = null,
        CancellationToken cancellationToken = default)
        => RunAsync(logger, activity, failure, body, detail: detail, metrics: metrics, cancellationToken: cancellationToken);

    private static bool IsCallerCancellation(Exception exception, CancellationToken cancellationToken)
        => cancellationToken.CanBeCanceled
            ? exception is OperationCanceledException && cancellationToken.IsCancellationRequested
            : exception.IsCancellation();

    [LoggerMessage(Level = LogLevel.Error, Message = "{Failure} (detail: {Detail})")]
    private static partial void LogFailure(ILogger logger, Exception exception, string failure, string? detail);
}
