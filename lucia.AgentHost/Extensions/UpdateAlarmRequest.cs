namespace lucia.AgentHost.Extensions;

/// <summary>
/// Request body for updating an existing alarm clock.
/// Only non-null fields are applied.
/// </summary>
public sealed class UpdateAlarmRequest
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
