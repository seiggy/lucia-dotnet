namespace lucia.Agents.Orchestration;

/// <summary>
/// Well-known service keys for keyed dependency injection of orchestrator dependencies.
/// Use these constants with <see cref="Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute"/>
/// and <c>AddKeyedSingleton</c>/<c>AddKeyedChatClient</c> registrations.
/// </summary>
public static class OrchestratorServiceKeys
{
    /// <summary>
    /// Keyed service key for the <see cref="Microsoft.Extensions.AI.IChatClient"/>
    /// used by the orchestrator's router to select agents.
    /// </summary>
    public const string RouterModel = "router-model";

    /// <summary>
    /// Keyed service key for the <see cref="Microsoft.Extensions.AI.IChatClient"/>
    /// used by the light control agent.
    /// </summary>
    public const string LightModel = "light-model";

    /// <summary>
    /// Keyed service key for the <see cref="Microsoft.Extensions.AI.IChatClient"/>
    /// used by the music playback agent.
    /// </summary>
    public const string MusicModel = "music-model";

    /// <summary>
    /// Keyed service key for the <see cref="Microsoft.Extensions.AI.IChatClient"/>
    /// used by the general knowledge agent.
    /// </summary>
    public const string GeneralModel = "general-model";

    /// <summary>
    /// Keyed service key for the <see cref="Microsoft.Extensions.AI.IChatClient"/>
    /// used by the timer/reminder agent.
    /// </summary>
    public const string TimerModel = "timer-model";

    /// <summary>
    /// All agent-specific model keys, used for bulk default-forwarding registration.
    /// </summary>
    public static readonly string[] AllAgentModelKeys =
    [
        RouterModel,
        LightModel,
        MusicModel,
        GeneralModel,
        TimerModel
    ];
}
