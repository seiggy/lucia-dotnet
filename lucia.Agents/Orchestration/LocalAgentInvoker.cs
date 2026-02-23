using lucia.Agents.Orchestration.Models;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Invokes an in-process agent via <see cref="AIHostAgent"/>, providing
/// session persistence and proper NeedsInput detection identical to the
/// A2A endpoint path — but without any HTTP overhead.
/// </summary>
public sealed class LocalAgentInvoker : IAgentInvoker
{
    private readonly AIHostAgent _hostAgent;
    private readonly ILogger _logger;
    private readonly AgentInvokerOptions _options;
    private readonly TimeProvider _timeProvider;

    public string AgentId { get; }

    public LocalAgentInvoker(
        string agentId,
        AIAgent agent,
        AgentSessionStore sessionStore,
        ILogger logger,
        IOptions<AgentInvokerOptions> options,
        TimeProvider? timeProvider = null)
    {
        AgentId = agentId;
        ArgumentNullException.ThrowIfNull(agent);
        _hostAgent = new AIHostAgent(agent, sessionStore ?? new NoopAgentSessionStore());
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<OrchestratorAgentResponse> InvokeAsync(
        ChatMessage message,
        CancellationToken cancellationToken)
    {
        var startTimestamp = _timeProvider.GetTimestamp();
        using var linkedCts = CreateTimeoutCts(cancellationToken);

        try
        {
            var text = ExtractText(message);
            _logger.LogInformation("[Diag] Agent {AgentId}: invoking RunAsync. Input length={Len}",
                AgentId, text.Length);

            var contextId = Guid.NewGuid().ToString("N");

            // Propagate a2a.contextId from the incoming message for multi-turn continuity
            if (message.AdditionalProperties?.TryGetValue("a2a.contextId", out var ctxVal) == true
                && ctxVal is string existingCtx && existingCtx.Length > 0)
            {
                contextId = existingCtx;
            }

            var session = await _hostAgent.GetOrCreateSessionAsync(contextId, linkedCts.Token)
                .ConfigureAwait(false);

            var chatMessages = new List<ChatMessage>
            {
                new(ChatRole.User, text)
                {
                    AdditionalProperties = new AdditionalPropertiesDictionary
                    {
                        ["a2a.contextId"] = contextId
                    }
                }
            };

            var response = await _hostAgent.RunAsync(
                chatMessages,
                session: session,
                cancellationToken: linkedCts.Token).ConfigureAwait(false);

            await _hostAgent.SaveSessionAsync(contextId, session, linkedCts.Token)
                .ConfigureAwait(false);

            // Detect NeedsInput via the same property the A2A endpoint uses
            var needsInput = response.Messages
                .Any(m => m.AdditionalProperties?.TryGetValue("lucia.needsInput", out var val) == true
                          && val is true);

            var content = string.Join(' ',
                response.Messages.Select(m => m.Text).Where(t => !string.IsNullOrEmpty(t)));

            _logger.LogInformation(
                "[Diag] Agent {AgentId}: response text={Text}, success=True, needsInput={NeedsInput}",
                AgentId,
                content.Length > 100 ? content[..100] : content,
                needsInput);

            return new OrchestratorAgentResponse
            {
                AgentId = AgentId,
                Content = content,
                Success = true,
                ExecutionTimeMs = ElapsedMs(startTimestamp),
                NeedsInput = needsInput
            };
        }
        catch (OperationCanceledException oce) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(oce, "Agent {AgentId} execution timed out after {Timeout}.", AgentId, _options.Timeout);
            return new OrchestratorAgentResponse
            {
                AgentId = AgentId,
                Content = string.Empty,
                Success = false,
                ErrorMessage = $"Agent execution timed out after {_options.Timeout.TotalMilliseconds:F0}ms.",
                ExecutionTimeMs = ElapsedMs(startTimestamp)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent {AgentId} execution failed.", AgentId);
            return new OrchestratorAgentResponse
            {
                AgentId = AgentId,
                Content = string.Empty,
                Success = false,
                ErrorMessage = ex.Message,
                ExecutionTimeMs = ElapsedMs(startTimestamp)
            };
        }
    }

    private long ElapsedMs(long startTimestamp)
        => (long)_timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds;

    private CancellationTokenSource CreateTimeoutCts(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_options.Timeout > TimeSpan.Zero)
        {
            cts.CancelAfter(_options.Timeout);
        }
        return cts;
    }

    private static string ExtractText(ChatMessage message)
    {
        if (message.Contents is { Count: > 0 })
        {
            var pieces = message.Contents.OfType<TextContent>().Select(t => t.Text);
            return string.Join(' ', pieces);
        }

        return message.ToString();
    }
}
