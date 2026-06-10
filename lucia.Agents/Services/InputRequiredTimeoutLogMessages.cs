using Microsoft.Extensions.Logging;

namespace lucia.Agents.Services;

/// <summary>
/// Compile-time structured log messages for <see cref="InputRequiredTimeoutService"/>.
/// </summary>
public static partial class InputRequiredTimeoutLogMessages
{
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Auto-cancelling task {TaskId}: no input received for {ElapsedSeconds}s (configured timeout: {Timeout})")]
    public static partial void TaskAutoCancel(
        this ILogger logger,
        string taskId,
        long elapsedSeconds,
        TimeSpan timeout);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "InputRequired timeout sweep: auto-cancelled {CancelledCount} of {TotalChecked} InputRequired tasks")]
    public static partial void SweepCompleted(
        this ILogger logger,
        int cancelledCount,
        int totalChecked);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to process timeout check for task {TaskId} — skipping")]
    public static partial void TaskCheckFailed(
        this ILogger logger,
        Exception ex,
        string taskId);
}
