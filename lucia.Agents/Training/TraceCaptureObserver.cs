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
    public Task OnRoutingCompletedAsync(AgentChoiceResult result, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Task.CompletedTask;
        }

        _stopwatch.Value = Stopwatch.StartNew();

        _currentTrace.Value = new ConversationTrace
        {
            SessionId = Guid.NewGuid().ToString("N"),
            UserInput = string.Empty,
            Routing = new RoutingDecision
            {
                SelectedAgentId = result.AgentId,
                AdditionalAgentIds = result.AdditionalAgents ?? [],
                Confidence = result.Confidence,
                Reasoning = result.Reasoning
            }
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
    public Task OnResponseAggregatedAsync(string aggregatedResponse, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || _currentTrace.Value is null)
        {
            return Task.CompletedTask;
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

        // Fire-and-forget persist — intentionally not awaited
        _ = PersistTraceAsync(trace);

        // Clear the async-local state
        _currentTrace.Value = null;
        _stopwatch.Value = null;

        return Task.CompletedTask;
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
