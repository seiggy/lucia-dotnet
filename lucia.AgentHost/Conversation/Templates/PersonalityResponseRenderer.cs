using lucia.Agents.Abstractions;
using lucia.Agents.Orchestration;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.AgentHost.Conversation.Templates;

/// <summary>
/// Rephrases a canned fast-path response through an LLM using the configured personality prompt.
/// Falls back to the original canned response when the LLM call fails or the prompt is not configured.
/// </summary>
public sealed partial class PersonalityResponseRenderer : IPersonalityResponseRenderer
{
    private readonly IChatClientResolver _chatClientResolver;
    private readonly IOptionsMonitor<PersonalityPromptOptions> _options;
    private readonly ILogger<PersonalityResponseRenderer> _logger;

    public PersonalityResponseRenderer(
        IChatClientResolver chatClientResolver,
        IOptionsMonitor<PersonalityPromptOptions> options,
        ILogger<PersonalityResponseRenderer> logger)
    {
        _chatClientResolver = chatClientResolver;
        _options = options;
        _logger = logger;
    }

    public async Task<string> RenderAsync(
        string skillId,
        string action,
        string cannedResponse,
        IReadOnlyDictionary<string, string> captures,
        CancellationToken ct = default)
    {
        var opts = _options.CurrentValue;

        if (string.IsNullOrWhiteSpace(opts.Instructions))
        {
            // UsePersonalityResponses is true (caller already checked) but the prompt is empty —
            // this is a misconfiguration that should be visible, not hidden at Debug level
            LogPersonalityPromptMissing(skillId, action);
            return cannedResponse;
        }

        try
        {
            var chatClient = await _chatClientResolver
                .ResolveAsync(opts.ModelConnectionName, ct)
                .ConfigureAwait(false);

            var actionDescription = BuildActionDescription(skillId, action, captures);

            var voiceTagInstruction = opts.SupportVoiceTags
                ? "Include SSML voice tags in your response for text-to-speech rendering. Use <break>, <emphasis>, and prosody tags where natural."
                : "Do not include any markup or tags in your response. Plain text only.";

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, opts.Instructions),
                new(ChatRole.User,
                    $"Rephrase this home automation action result in your voice. Be brief and natural.\n" +
                    $"{voiceTagInstruction}\n" +
                    $"Action: {actionDescription}\n" +
                    $"Result: {cannedResponse}"),
            };

            LogPersonalityCallStart(skillId, action);

            var response = await chatClient
                .GetResponseAsync(messages, cancellationToken: ct)
                .ConfigureAwait(false);

            var rewritten = response.Text;

            if (!string.IsNullOrWhiteSpace(rewritten))
            {
                LogPersonalityCallSuccess(skillId, action, rewritten.Length);
                return rewritten;
            }

            LogPersonalityEmptyResponse(skillId, action);
            return cannedResponse;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogPersonalityCallFailed(skillId, action, ex);
            return cannedResponse;
        }
    }

    private static string BuildActionDescription(
        string skillId, string action, IReadOnlyDictionary<string, string> captures)
    {
        var entity = captures.GetValueOrDefault("entity", "unknown device");
        var actionValue = captures.GetValueOrDefault("action", action);

        return skillId switch
        {
            "LightControlSkill" => $"{actionValue} {entity} lights",
            "ClimateControlSkill" => $"{action} climate for {entity}",
            "SceneControlSkill" => $"activate scene {entity}",
            _ => $"{action} on {entity}",
        };
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "UsePersonalityResponses is enabled but Instructions is null/empty for {SkillId}/{Action} — falling back to canned response. Set PersonalityPrompt:Instructions to enable personality rewriting.")]
    private partial void LogPersonalityPromptMissing(string skillId, string action);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Calling personality LLM for {SkillId}/{Action}")]
    private partial void LogPersonalityCallStart(string skillId, string action);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Personality rewrite for {SkillId}/{Action} produced {Length} characters")]
    private partial void LogPersonalityCallSuccess(string skillId, string action, int length);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Personality LLM returned empty response for {SkillId}/{Action}, falling back to canned response")]
    private partial void LogPersonalityEmptyResponse(string skillId, string action);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Personality LLM call failed for {SkillId}/{Action}, falling back to canned response")]
    private partial void LogPersonalityCallFailed(string skillId, string action, Exception exception);
}
