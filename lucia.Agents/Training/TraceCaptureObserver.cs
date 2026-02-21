using System.Diagnostics;
using System.Text.RegularExpressions;
using lucia.Agents.Orchestration;
using lucia.Agents.Orchestration.Models;
using lucia.Agents.Training.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Agents.Training;

/// <summary>
/// Scoped observer that captures a single orchestrator request lifecycle
/// into a <see cref="ConversationTrace"/> and fire-and-forget persists it to MongoDB.
/// </summary>
public sealed class TraceCaptureObserver : IOrchestratorObserver
{
    private readonly ITraceRepository _repository;
    private readonly TraceCaptureOptions _options;
    private readonly ILogger<TraceCaptureObserver> _logger;
    private readonly Regex[] _redactionRegexes;

    private readonly AsyncLocal<ConversationTrace?> _currentTrace = new();
    private readonly AsyncLocal<Stopwatch?> _stopwatch = new();

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
    public Task OnRequestStartedAsync(string userRequest, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Task.CompletedTask;
        }

        _stopwatch.Value = Stopwatch.StartNew();

        var sessionId = Guid.NewGuid().ToString("N");

        _currentTrace.Value = new ConversationTrace
        {
            SessionId = sessionId,
            UserInput = userRequest,
            Metadata =
            {
                ["traceType"] = "orchestrator"
            }
        };

        // Add an orchestration-level record so "orchestration" appears in the agents column
        _currentTrace.Value.AgentExecutions.Add(new AgentExecutionRecord
        {
            AgentId = "orchestration",
            Success = true
        });

        // Share the session ID with the per-agent tracing chat client
        // so individual agent traces are correlated with this orchestrator trace.
        AgentTracingChatClient.SetSessionId(sessionId);

        _logger.LogDebug("Trace capture started for request: {Request}", userRequest);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnRoutingCompletedAsync(AgentChoiceResult result, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || _currentTrace.Value is null)
        {
            return Task.CompletedTask;
        }

        // Mutate the existing trace object (do NOT reassign _currentTrace.Value)
        // so that changes are visible in both parent and child async contexts.
        _currentTrace.Value.Routing = new RoutingDecision
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
    public Task OnAgentExecutionCompletedAsync(OrchestratorAgentResponse response, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || _currentTrace.Value is null)
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

        _currentTrace.Value.AgentExecutions.Add(record);

        if (!response.Success)
        {
            _currentTrace.Value.IsErrored = true;
            _currentTrace.Value.ErrorMessage ??= response.ErrorMessage;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task OnResponseAggregatedAsync(string aggregatedResponse, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || _currentTrace.Value is null)
        {
            return;
        }

        _stopwatch.Value?.Stop();

        var trace = _currentTrace.Value;
        trace.FinalResponse = ApplyRedaction(aggregatedResponse);
        trace.TotalDurationMs = _stopwatch.Value?.Elapsed.TotalMilliseconds ?? 0;
        trace.UserInput = ApplyRedaction(trace.UserInput);

        // Redact agent response content
        foreach (var execution in trace.AgentExecutions)
        {
            if (execution.ResponseContent is not null)
            {
                execution.ResponseContent = ApplyRedaction(execution.ResponseContent);
            }
        }

        // Persist trace — await to surface any errors
        try
        {
            await PersistTraceAsync(trace);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Trace persistence failed for {TraceId}", trace.Id);
        }

        // Clear the async-local state
        _currentTrace.Value = null;
        _stopwatch.Value = null;

        return;
    }

    private async Task PersistTraceAsync(ConversationTrace trace)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            using var activity = TraceCaptureTelemetry.Source.StartActivity("PersistTrace");
            activity?.SetTag(TraceCaptureTelemetry.TagTraceId, trace.Id);

            await _repository.InsertTraceAsync(trace);

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
}
