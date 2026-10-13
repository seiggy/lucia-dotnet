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
    /// <param name="failure">Start of the message returned on failure, for example "Error getting state for 'x'".</param>
    /// <param name="body">The tool's work. Returns the success text.</param>
    /// <param name="onFailure">Runs on failure, for failure counters.</param>
    /// <param name="duration">Records how long the tool took, whether or not it failed.</param>
    /// <param name="detail">Extra context for the log only, such as the search term.</param>
    public static async Task<string> RunAsync(
        ILogger logger,
        string failure,
        Func<Task<string>> body,
        Action? onFailure = null,
        Histogram<double>? duration = null,
        string? detail = null)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            return await body().ConfigureAwait(false);
        }
        catch (Exception ex) when (!ex.IsCancellation())
        {
            LogFailure(logger, ex, failure, detail);
            onFailure?.Invoke();
            return $"{failure}: {ex.Message}";
        }
        finally
        {
            duration?.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{Failure} (detail: {Detail})")]
    private static partial void LogFailure(ILogger logger, Exception exception, string failure, string? detail);
}
