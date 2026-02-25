using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using lucia.Agents.Orchestration;
using lucia.Agents.Orchestration.Models;
using lucia.Agents.Training.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Agents.Training;

/// <summary>
/// Singleton observer that captures orchestrator request lifecycles into
/// <see cref="ConversationTrace"/> objects and persists them to MongoDB.
/// Uses a <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by request ID
/// instead of <see cref="AsyncLocal{T}"/>, which does not survive the MAF
/// <c>InProcessExecution</c> workflow boundary.
/// </summary>
public sealed class TraceCaptureObserver : IOrchestratorObserver
{
    private readonly ITraceRepository _repository;
    private readonly TraceCaptureOptions _options;
    private readonly ILogger<TraceCaptureObserver> _logger;
    private readonly Regex[] _redactionRegexes;

    /// <summary>
    /// Thread-safe store of in-flight traces keyed by request ID.
    /// Entries are added in <see cref="OnRequestStartedAsync"/> and removed
    /// in <see cref="OnResponseAggregatedAsync"/> after persistence.
    /// </summary>
    private readonly ConcurrentDictionary<string, ActiveTrace> _activeTraces = new(StringComparer.Ordinal);

    public TraceCaptureObserver(
        ITraceRepository repository,
        IOptions<TraceCaptureOptions> options,
        ILogger<TraceCaptureObserver> logger)
    {
        _repository = repository;
        _options = options.Value;
        _logger = logger;
        _redactionRegexes = _options.RedactionPatterns
            .Select(p => new Regex(p, RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            .ToArray();
    }

    /// <inheritdoc />
    public Task<string> OnRequestStartedAsync(
        string userRequest,
        IReadOnlyList<TracedMessage>? conversationHistory = null,
        CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString("N");

        if (!_options.Enabled)
        {
            return Task.FromResult(requestId);
        }

        var trace = new ConversationTrace
        {
            SessionId = requestId,
            UserInput = userRequest,
            ConversationHistory = conversationHistory?.ToList() ?? [],
            Metadata =
            {
                ["traceType"] = "orchestrator"
            }
        };

        // Add an orchestration-level record so "orchestration" appears in the agents column
        trace.AgentExecutions.Add(new AgentExecutionRecord
        {
            AgentId = "orchestration",
            Success = true
        });

        var active = new ActiveTrace(trace, Stopwatch.StartNew());
        _activeTraces[requestId] = active;

        // Share the session ID with the per-agent tracing chat client
        // so individual agent traces are correlated with this orchestrator trace.
        AgentTracingChatClient.SetSessionId(requestId);

        _logger.LogDebug("Trace capture started for request {RequestId}: {Request}", requestId, userRequest);

        return Task.FromResult(requestId);
    }

    /// <inheritdoc />
    public Task OnRoutingCompletedAsync(
        string requestId,
        AgentChoiceResult result,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || !_activeTraces.TryGetValue(requestId, out var active))
        {
            return Task.CompletedTask;
        }

        var trace = active.Trace;
        trace.SystemPrompt = systemPrompt;

        trace.Routing = new RoutingDecision
        {
            SelectedAgentId = result.AgentId,
            AdditionalAgentIds = result.AdditionalAgents ?? [],
            Confidence = result.Confidence,
            Reasoning = result.Reasoning,
            AgentInstructions = result.AgentInstructions?
                .Select(ai => new AgentInstructionRecord
                {
                    AgentId = ai.AgentId,
                    Instruction = ai.Instruction
                }).ToList() ?? []
        };

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnAgentExecutionCompletedAsync(string requestId, OrchestratorAgentResponse response, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || !_activeTraces.TryGetValue(requestId, out var active))
        {
            return Task.CompletedTask;
        }

        var record = new AgentExecutionRecord
        {
            AgentId = response.AgentId,
            ResponseContent = response.Content,
            Success = response.Success,
            ErrorMessage = response.ErrorMessage,
            ExecutionDurationMs = response.ExecutionTimeMs
        };

        lock (active.SyncRoot)
        {
            active.Trace.AgentExecutions.Add(record);

            if (!response.Success)
            {
                active.Trace.IsErrored = true;
                active.Trace.ErrorMessage ??= response.ErrorMessage;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task OnResponseAggregatedAsync(string requestId, string aggregatedResponse, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || !_activeTraces.TryRemove(requestId, out var active))
        {
            return;
        }

        active.Stopwatch.Stop();
        var trace = active.Trace;

        lock (active.SyncRoot)
        {
            trace.FinalResponse = ApplyRedaction(aggregatedResponse);
            trace.TotalDurationMs = active.Stopwatch.Elapsed.TotalMilliseconds;
            trace.UserInput = ApplyRedaction(trace.UserInput);

            // Redact agent response content
            foreach (var execution in trace.AgentExecutions)
            {
                if (execution.ResponseContent is not null)
                {
                    execution.ResponseContent = ApplyRedaction(execution.ResponseContent);
                }
            }
        }

        // Persist trace — await to surface any errors
        try
        {
            await PersistTraceAsync(trace).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Trace persistence failed for {TraceId}", trace.Id);
        }
    }

    private async Task PersistTraceAsync(ConversationTrace trace)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            using var activity = TraceCaptureTelemetry.Source.StartActivity("PersistTrace");
            activity?.SetTag(TraceCaptureTelemetry.TagTraceId, trace.Id);

            await _repository.InsertTraceAsync(trace).ConfigureAwait(false);

            TraceCaptureTelemetry.TracesCaptured.Add(1);

            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            TraceCaptureTelemetry.CaptureLatency.Record(elapsedMs);
        }
        catch (Exception ex)
        {
            TraceCaptureTelemetry.StorageErrors.Add(1);
            _logger.LogError(ex, "Failed to persist conversation trace {TraceId}", trace.Id);
        }
    }

    private string ApplyRedaction(string input)
    {
        if (string.IsNullOrEmpty(input) || _redactionRegexes.Length == 0)
        {
            return input;
        }

        var result = input;
        foreach (var regex in _redactionRegexes)
        {
            result = regex.Replace(result, "[REDACTED]");
        }

        return result;
    }

    /// <summary>
    /// Holds an in-flight trace and its timing information.
    /// <see cref="SyncRoot"/> protects mutation of the <see cref="ConversationTrace"/>
    /// collections that may be accessed from parallel agent dispatch tasks.
    /// </summary>
    private sealed class ActiveTrace(ConversationTrace trace, Stopwatch stopwatch)
    {
        public ConversationTrace Trace { get; } = trace;
        public Stopwatch Stopwatch { get; } = stopwatch;
        public object SyncRoot { get; } = new();
    }
}
