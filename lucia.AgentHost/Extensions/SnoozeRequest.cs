namespace lucia.AgentHost.Extensions;

/// <summary>
/// Request body for snoozing an active alarm.
/// </summary>
public sealed class SnoozeRequest
{
    /// <summary>Snooze duration. Defaults to 9 minutes if not specified.</summary>
    public TimeSpan? Duration { get; set; }
}
