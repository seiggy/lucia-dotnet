namespace lucia.TimerAgent.ScheduledTasks;

/// <summary>
/// Discriminator for polymorphic dispatch of scheduled task types.
/// </summary>
public enum ScheduledTaskType
{
    /// <summary>Countdown → TTS announce on expiry.</summary>
    Timer,

    /// <summary>Wall-clock fire → play alarm sound, loop until dismissed.</summary>
    Alarm,

    /// <summary>Wall-clock or countdown → replay orchestrator request with captured context.</summary>
    AgentTask
}
