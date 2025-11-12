using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AgentCard = A2A.AgentCard;
using lucia.Agents.Orchestration.Models;
using lucia.Agents.Registry;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Executes routing decisions by invoking an LLM via <see cref="IChatClient"/> and emitting an <see cref="AgentChoiceResult"/>.
/// </summary>
public sealed class RouterExecutor : ReflectingExecutor<RouterExecutor>, IMessageHandler<ChatMessage, AgentChoiceResult>
{
    public const string ExecutorId = "RouterExecutor";

    /// <summary>
    /// Shared serializer options for parsing structured router responses.
    /// </summary>
    public static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IChatClient _chatClient;
    private readonly IAgentRegistry _agentRegistry;
    private readonly ILogger<RouterExecutor> _logger;
    private readonly RouterExecutorOptions _options;
    private readonly JsonElement _schema;

    public RouterExecutor(
        IChatClient chatClient,
        IAgentRegistry agentRegistry,
        ILogger<RouterExecutor> logger,
        IOptions<RouterExecutorOptions> options)
        : base(ExecutorId)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        if (_options.MaxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "RouterExecutorOptions.MaxAttempts must be at least 1.");
        }

        _schema = AIJsonUtilities.CreateJsonSchema(typeof(AgentChoiceResult));
    }

    public async ValueTask<AgentChoiceResult> HandleAsync(ChatMessage message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var availableAgents = await FetchAgentsAsync(cancellationToken).ConfigureAwait(false);
        if (availableAgents.Count == 0)
        {
            _logger.LogWarning("RouterExecutor invoked with no registered agents; falling back.");
            return CreateFallbackResult("No registered agents available for routing.");
        }

        var userRequest = ExtractUserText(message);
        var chatMessages = BuildChatMessages(userRequest, availableAgents);
        var chatOptions = BuildChatOptions();

        AgentChoiceResult? parsed = null;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= _options.MaxAttempts && parsed is null; attempt++)
        {
            try
            {
                var response = await _chatClient.GetResponseAsync(chatMessages, chatOptions, cancellationToken).ConfigureAwait(false);
                var payload = ExtractAssistantResponse(response);
                parsed = DeserializeChoice(payload);
                if (parsed is null)
                {
                    throw new JsonException("RouterExecutor deserialization returned null.");
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                lastError = ex;
                _logger.LogWarning(ex, "RouterExecutor attempt {Attempt} returned malformed structured output.", attempt);
            }
        }

        if (parsed is null)
        {
            var reason = lastError?.Message ?? "Unknown structured output failure.";
            return CreateFallbackResult(string.Format(CultureInfo.InvariantCulture,
                "Routing model failed after {0} attempts: {1}", _options.MaxAttempts, reason));
        }

        if (!IsKnownAgent(parsed.AgentId, availableAgents))
        {
            _logger.LogWarning("RouterExecutor returned unknown agent '{AgentId}'. Falling back.", parsed.AgentId);
            return CreateFallbackResult(string.Format(CultureInfo.InvariantCulture,
                "Model suggested unknown agent '{0}'.", parsed.AgentId));
        }

        NormalizeAdditionalAgents(parsed, availableAgents);

        if (parsed.Confidence < _options.ConfidenceThreshold)
        {
            _logger.LogInformation(
                "RouterExecutor confidence {Confidence} was below threshold {Threshold}. Returning clarification.",
                parsed.Confidence,
                _options.ConfidenceThreshold);
            return CreateClarificationResult(parsed, availableAgents, userRequest);
        }

        return parsed;
    }

    public ValueTask<AgentChoiceResult> HandleAsync(ChatMessage message, IWorkflowContext context)
        => HandleAsync(message, context, CancellationToken.None);

    private async Task<List<AgentCard>> FetchAgentsAsync(CancellationToken cancellationToken)
    {
        var results = new List<AgentCard>();
        await foreach (var agent in _agentRegistry.GetEnumerableAgentsAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(agent);
        }

        return results;
    }

    private IReadOnlyList<ChatMessage> BuildChatMessages(string userRequest, IReadOnlyList<AgentCard> agents)
    {
        var agentCatalog = BuildAgentCatalog(agents);

        var userPromptTemplate = string.IsNullOrWhiteSpace(_options.UserPromptTemplate)
            ? RouterExecutorOptions.DefaultUserPromptTemplate
            : _options.UserPromptTemplate!;

        var payload = userPromptTemplate
            .Replace("<<USER_REQUEST>>", userRequest)
            .Replace("<<AGENT_CATALOG>>", agentCatalog);

        return new[]
        {
            new ChatMessage(ChatRole.System, _options.SystemPrompt ?? RouterExecutorOptions.DefaultSystemPrompt),
            new ChatMessage(ChatRole.User, payload)
        };
    }

    private string BuildAgentCatalog(IReadOnlyList<AgentCard> agents)
    {
        var builder = new StringBuilder();
        builder.AppendLine(_options.AgentCatalogHeader ?? RouterExecutorOptions.DefaultAgentCatalogHeader);
        builder.AppendLine();

        foreach (var agent in agents)
        {
            if (agent.Name == "orchestrator")
            {
                // Skip self-reference
                continue;
            }
            builder.Append("- ");
            builder.Append(agent.Name);
            builder.Append(": ");
            builder.AppendLine(agent.Description);

            if (_options.IncludeAgentCapabilities && agent.Capabilities is not null)
            {
                var tags = new List<string>();
                if (agent.Capabilities.PushNotifications)
                {
                    tags.Add("push");
                }
                if (agent.Capabilities.Streaming)
                {
                    tags.Add("streaming");
                }
                if (agent.Capabilities.StateTransitionHistory)
                {
                    tags.Add("state-history");
                }

                if (tags.Count > 0)
                {
                    builder.Append("  capabilities: ");
                    builder.AppendLine(string.Join(", ", tags));
                }
            }

            if (_options.IncludeSkillExamples && agent.Skills is not null && agent.Skills.Count > 0)
            {
                var examples = agent.Skills
                    .Where(skill => skill.Examples is not null)
                    .SelectMany(skill => skill.Examples!);

                foreach (var example in examples)
                {
                    builder.Append("  example: ");
                    builder.AppendLine(example);
                }
            }
        }

        return builder.ToString();
    }

    private ChatOptions BuildChatOptions()
    {
        var options = new ChatOptions
        {
            Temperature = (float?)_options.Temperature,
            MaxOutputTokens = _options.MaxOutputTokens,
        };

        options.ResponseFormat = new ChatResponseFormatJson(_schema);
        return options;
    }

    private static string ExtractAssistantResponse(ChatResponse response)
    {
        if (response is null)
        {
            throw new InvalidDataException("Chat response was null.");
        }

        var builder = new StringBuilder();

        foreach (var message in response.Messages)
        {
            if (message.Contents is { Count: > 0 })
            {
                foreach (var content in message.Contents)
                {
                    if (content is TextContent text && !string.IsNullOrWhiteSpace(text.Text))
                    {
                        if (builder.Length > 0)
                        {
                            builder.AppendLine();
                        }
                        builder.Append(text.Text);
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(message.Text))
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }
                builder.Append(message.Text);
            }
        }

        var result = builder.ToString().Trim();
        if (string.IsNullOrEmpty(result))
        {
            throw new InvalidDataException("Chat response did not contain any text content to parse.");
        }

        return result;
    }

    private static AgentChoiceResult? DeserializeChoice(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        return JsonSerializer.Deserialize<AgentChoiceResult>(payload, JsonSerializerOptions);
    }

    private static string ExtractUserText(ChatMessage message)
    {
        if (message.Contents is { Count: > 0 })
        {
            var textParts = message.Contents.OfType<TextContent>().Select(tc => tc.Text).Where(t => !string.IsNullOrWhiteSpace(t));
            var combined = string.Join(" ", textParts).Trim();
            if (!string.IsNullOrEmpty(combined))
            {
                return combined;
            }
        }

        return message.Text ?? string.Empty;
    }

    private static bool IsKnownAgent(string agentId, IReadOnlyList<AgentCard> agents)
    {
        return agents.Any(agent => string.Equals(agent.Name, agentId, StringComparison.OrdinalIgnoreCase));
    }

    private void NormalizeAdditionalAgents(AgentChoiceResult result, IReadOnlyList<AgentCard> agents)
    {
        if (result.AdditionalAgents is null || result.AdditionalAgents.Count == 0)
        {
            return;
        }

        var knownAgents = new HashSet<string>(agents.Select(a => a.Name), StringComparer.OrdinalIgnoreCase);
        knownAgents.Remove(result.AgentId);

        var filtered = result.AdditionalAgents
            .Where(agentId => knownAgents.Contains(agentId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        result.AdditionalAgents = filtered.Count > 0 ? filtered : null;
    }

    private AgentChoiceResult CreateClarificationResult(AgentChoiceResult original, IReadOnlyList<AgentCard> agents, string userRequest)
    {
        var options = _options.ClarificationPromptTemplate ?? RouterExecutorOptions.DefaultClarificationPromptTemplate;
        var candidates = string.Join(", ", agents.Select(a => a.Name));
        var reasoning = string.Format(CultureInfo.InvariantCulture, options, candidates, userRequest, original.AgentId);

        return new AgentChoiceResult
        {
            AgentId = _options.ClarificationAgentId ?? RouterExecutorOptions.DefaultClarificationAgentId,
            Confidence = original.Confidence,
            Reasoning = reasoning,
            AdditionalAgents = null
        };
    }

    private AgentChoiceResult CreateFallbackResult(string reason)
    {
        var template = _options.FallbackReasonTemplate ?? RouterExecutorOptions.DefaultFallbackReasonTemplate;
        var reasoning = string.Format(CultureInfo.InvariantCulture, template, reason);

        return new AgentChoiceResult
        {
            AgentId = _options.FallbackAgentId ?? RouterExecutorOptions.DefaultFallbackAgentId,
            Confidence = 0,
            Reasoning = reasoning,
            AdditionalAgents = null
        };
    }
}
