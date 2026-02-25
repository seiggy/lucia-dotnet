namespace lucia.AgentHost.Extensions;

/// <summary>
/// Request body for creating a new alarm clock.
/// </summary>
public sealed class CreateAlarmRequest
{
    public string? Name { get; set; }
    public string? TargetEntity { get; set; }
    public string? AlarmSoundId { get; set; }
    public string? CronSchedule { get; set; }
    public DateTimeOffset? NextFireAt { get; set; }
    public TimeSpan? PlaybackInterval { get; set; }
    public TimeSpan? AutoDismissAfter { get; set; }
    public bool? IsEnabled { get; set; }
}
