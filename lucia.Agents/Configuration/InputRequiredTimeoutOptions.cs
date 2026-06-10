namespace lucia.Agents.Configuration;

/// <summary>
/// Configuration for <see cref="Services.InputRequiredTimeoutService"/>.
/// Tasks that enter <c>InputRequired</c> state and receive no follow-up input
/// within <see cref="Timeout"/> are automatically transitioned to <c>Canceled</c>.
/// </summary>
public sealed class InputRequiredTimeoutOptions
{
    public const string SectionName = "InputRequiredTimeout";

    /// <summary>
    /// How long a task may remain in <c>InputRequired</c> state before it is
    /// auto-cancelled. Default: 1 minute.
    /// </summary>
    /// <remarks>
    /// 1 minute matches the voice-engine deployment context: a human voice
    /// response after an LLM prompt typically arrives within 6–10 seconds,
    /// and even long speech-to-text utterances rarely exceed 15–20 seconds.
    /// 1 minute provides a comfortable safety margin without leaving sessions
    /// hung for too long. Adjust via configuration for non-voice deployments.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How often the background sweeper wakes to check for timed-out tasks.
    /// Default: 10 seconds.
    /// </summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(10);
}
