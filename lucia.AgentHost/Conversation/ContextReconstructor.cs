using lucia.AgentHost.Conversation.Models;
using lucia.Agents.Services;

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

    private readonly UserContextProvider? _userContextProvider;
    private readonly ChatHistoryProvider? _chatHistoryProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContextReconstructor"/> class.
    /// </summary>
    public ContextReconstructor(UserContextProvider? userContextProvider = null, ChatHistoryProvider? chatHistoryProvider = null)
    {
        _userContextProvider = userContextProvider;
        _chatHistoryProvider = chatHistoryProvider;
    }

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
        return BuildPrompt(request, string.Empty, []);
    }

    /// <summary>
    /// Reconstructs a full prompt string enriched with stored user context and recent chat history.
    /// </summary>
    public async Task<string> ReconstructAsync(ConversationRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Context.UserId))
        {
            return Reconstruct(request);
        }

        var userContextTask = _userContextProvider is not null
            ? _userContextProvider.GetUserContextAsync(request.Context.UserId, ct)
            : Task.FromResult(string.Empty);
        var historyTask = _chatHistoryProvider is not null
            ? _chatHistoryProvider.GetRecentHistoryAsync(request.Context.UserId, ct: ct)
            : Task.FromResult<IReadOnlyList<string>>([]);

        await Task.WhenAll(userContextTask, historyTask).ConfigureAwait(false);

        return BuildPrompt(
            request,
            await userContextTask.ConfigureAwait(false),
            await historyTask.ConfigureAwait(false));
    }

    private static string BuildPrompt(ConversationRequest request, string userContext, IReadOnlyList<string> recentHistory)
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

        var speakerLine = context.SpeakerId is not null
            ? $"\n[Speaker: {context.SpeakerId}]"
            : string.Empty;
        var promptSections = new List<string> { $"{prompt}{speakerLine}" };

        if (!string.IsNullOrWhiteSpace(userContext))
        {
            promptSections.Add(userContext);
        }

        if (recentHistory.Count > 0)
        {
            promptSections.Add($"RECENT CHAT HISTORY:\n{string.Join("\n\n", recentHistory)}");
        }

        return $"{string.Join("\n\n", promptSections)}\n\nUser: {request.Text}";
    }
}
