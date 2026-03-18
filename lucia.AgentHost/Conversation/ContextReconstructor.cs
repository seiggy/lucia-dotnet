using lucia.AgentHost.Conversation.Models;

namespace lucia.AgentHost.Conversation;

/// <summary>
/// Rebuilds a system prompt with embedded context from a structured <see cref="ConversationRequest"/>.
/// Used when the command parser doesn't match and the request falls back to the LLM orchestrator.
/// </summary>
/// <remarks>
/// The default template mirrors the format produced by the Python custom component
/// (<c>custom_components/lucia/const.py</c> — <c>DEFAULT_PROMPT</c>) so the LLM
/// orchestrator receives the same context shape it was tuned against.
/// </remarks>
public sealed class ContextReconstructor
{
    // Aligned with custom_components/lucia/const.py DEFAULT_PROMPT.
    // device_capabilities is omitted because ConversationContext does not carry it.
    private const string DefaultTemplate = """
        HOME ASSISTANT CONTEXT:

        REQUEST_CONTEXT:
        {
          "timestamp": "{timestamp}",
          "day_of_week": "{dayOfWeek}",
          "location": "{location}",
          "device": {
            "id": "{deviceId}",
            "area": "{deviceArea}",
            "type": "{deviceType}"
          }
        }

        The user is requesting assistance with their Home Assistant-controlled smart home. Use the entity IDs above to reference specific devices when delegating to specialized agents. Consider the current time and device states when planning actions.
        """;

    /// <summary>
    /// Reconstructs a full prompt string from the structured request for LLM consumption.
    /// </summary>
    /// <param name="request">The parsed conversation request containing user text and device context.</param>
    /// <returns>
    /// A combined string containing the system prompt (with interpolated context) followed by the user text,
    /// ready to be passed to <c>LuciaEngine.ProcessRequestAsync</c>.
    /// </returns>
    public string Reconstruct(ConversationRequest request)
    {
        var template = request.PromptOverride ?? DefaultTemplate;

        var context = request.Context;
        var prompt = template
            .Replace("{timestamp}", context.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"))
            .Replace("{dayOfWeek}", context.Timestamp.ToString("dddd"))
            .Replace("{location}", context.Location ?? "Home")
            .Replace("{deviceId}", context.DeviceId ?? "unknown")
            .Replace("{deviceArea}", context.DeviceArea ?? "unknown")
            .Replace("{deviceType}", context.DeviceType ?? "voice_assistant");

        return $"{prompt}\n\nUser: {request.Text}";
    }
}
