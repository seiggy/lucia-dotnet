using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using lucia.Agents.Orchestration.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Invokes an in-process <see cref="AIAgent"/> and returns a structured response.
/// </summary>
public sealed class LocalAgentInvoker : IAgentInvoker
{
    private readonly AIAgent _agent;
    private readonly ILogger _logger;
    private readonly AgentInvokerOptions _options;
    private readonly TimeProvider _timeProvider;

    public string AgentId { get; }

    public LocalAgentInvoker(
        string agentId,
        AIAgent agent,
        ILogger logger,
        IOptions<AgentInvokerOptions> options,
        TimeProvider? timeProvider = null)
    {
        AgentId = agentId;
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
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
            _logger.LogInformation("[Diag] Agent {AgentId}: invoking RunAsync. Input length={Len}",
                AgentId, ExtractText(message).Length);

            var response = await _agent.RunAsync(message, session: null, options: null, linkedCts.Token)
                .WaitAsync(linkedCts.Token)
                .ConfigureAwait(false);

            // Diagnostic: log tool calls
            if (response.Messages is { Count: > 0 })
            {
                foreach (var msg in response.Messages)
                {
                    var functionCalls = msg.Contents.OfType<FunctionCallContent>().ToList();
                    var functionResults = msg.Contents.OfType<FunctionResultContent>().ToList();
                    if (functionCalls is { Count: > 0 })
                        _logger.LogInformation("[Diag] Agent {AgentId}: tool calls in response: {Calls}",
                            AgentId, string.Join(", ", functionCalls.Select(fc => fc.Name)));
                    if (functionResults is { Count: > 0 })
                        _logger.LogInformation("[Diag] Agent {AgentId}: tool results in response: {Results}",
                            AgentId, string.Join(", ", functionResults.Select(fr => $"{fr.CallId}={fr.Result?.ToString()?[..Math.Min(100, fr.Result?.ToString()?.Length ?? 0)]}")));
                }
            }

            _logger.LogInformation("[Diag] Agent {AgentId}: response text={Text}, messageCount={Count}",
                AgentId, response.Text?[..Math.Min(100, response.Text?.Length ?? 0)] ?? "(null)", response.Messages.Count);

            return new OrchestratorAgentResponse
            {
                AgentId = AgentId,
                Content = response.Text ?? string.Empty,
                Success = true,
                ExecutionTimeMs = ElapsedMs(startTimestamp),
                NeedsInput = IsResponseAskingForInput(response.Text)
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

    /// <summary>
    /// Detects when a local agent's response is asking the user for clarification.
    /// Agent system prompts instruct agents to end responses with '?' when they need more info.
    /// </summary>
    private static bool IsResponseAskingForInput(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Agent instructions say: "If you need clarification, end your response with '?'"
        return text.TrimEnd().EndsWith('?');
    }
}
