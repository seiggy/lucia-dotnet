namespace lucia.AgentHost.Conversation.Templates;

/// <summary>
/// Rephrases a canned skill execution result through an LLM personality prompt.
/// Returns the personality-styled text, or falls back to the original on failure.
/// </summary>
public interface IPersonalityResponseRenderer
{
    /// <summary>
    /// Passes <paramref name="cannedResponse"/> through the configured personality prompt
    /// to produce a natural-sounding response in the assistant's voice.
    /// </summary>
    /// <param name="skillId">The skill that produced the result (e.g., "LightControlSkill").</param>
    /// <param name="action">The action that was executed (e.g., "toggle").</param>
    /// <param name="cannedResponse">The template-rendered canned response text.</param>
    /// <param name="captures">Captured values from the command pattern (entity, action, etc.).</param>
    /// <param name="skillResultText">The actual result text from the skill execution (e.g., "'Kitchen Light' turned on."), or null if unavailable.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Personality-styled response text, or the original <paramref name="cannedResponse"/> on failure.</returns>
    Task<string> RenderAsync(
        string skillId,
        string action,
        string cannedResponse,
        IReadOnlyDictionary<string, string> captures,
        string? skillResultText = null,
        CancellationToken ct = default);
}
