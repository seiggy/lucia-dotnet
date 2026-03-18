using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace lucia.AgentHost.Conversation;

/// <summary>
/// Telemetry instrumentation for the /api/conversation endpoint.
/// Tracks command parser vs LLM fallback routing and execution performance.
/// Exposes in-memory counters for the activity summary API.
/// </summary>
public sealed class ConversationTelemetry
{
    private readonly Counter<long> _commandParsedCounter;
    private readonly Counter<long> _llmFallbackCounter;
    private readonly Counter<long> _commandErrorCounter;
    private readonly Histogram<double> _commandParsedDuration;
    private readonly Histogram<double> _llmFallbackDuration;
    private readonly ActivitySource _activitySource;

    private long _commandParsedTotal;
    private long _llmFallbackTotal;
    private long _commandErrorTotal;

    public ConversationTelemetry(AgentHostTelemetrySource telemetrySource)
    {
        _activitySource = telemetrySource.ActivitySource;

        _commandParsedCounter = telemetrySource.Meter.CreateCounter<long>(
            "conversation.command_parsed",
            description: "Commands handled by the pattern parser (no LLM)");

        _llmFallbackCounter = telemetrySource.Meter.CreateCounter<long>(
            "conversation.llm_fallback",
            description: "Requests forwarded to the LLM orchestrator");

        _commandErrorCounter = telemetrySource.Meter.CreateCounter<long>(
            "conversation.command_parsed.errors",
            description: "Command parser execution failures");

        _commandParsedDuration = telemetrySource.Meter.CreateHistogram<double>(
            "conversation.command_parsed.duration_ms",
            unit: "ms",
            description: "Fast-path command execution duration");

        _llmFallbackDuration = telemetrySource.Meter.CreateHistogram<double>(
            "conversation.llm_fallback.duration_ms",
            unit: "ms",
            description: "LLM fallback execution duration");
    }

    public Activity? StartConversationActivity(string text)
        => _activitySource.StartActivity("conversation.process");

    public void RecordCommandParsed(string skillId, string action, double durationMs)
    {
        _commandParsedCounter.Add(1,
            new KeyValuePair<string, object?>("skillId", skillId),
            new KeyValuePair<string, object?>("action", action));
        _commandParsedDuration.Record(durationMs);
        Interlocked.Increment(ref _commandParsedTotal);
    }

    public void RecordLlmFallback(double durationMs)
    {
        _llmFallbackCounter.Add(1);
        _llmFallbackDuration.Record(durationMs);
        Interlocked.Increment(ref _llmFallbackTotal);
    }

    public void RecordCommandError(string skillId, string action)
    {
        _commandErrorCounter.Add(1,
            new KeyValuePair<string, object?>("skillId", skillId),
            new KeyValuePair<string, object?>("action", action));
        Interlocked.Increment(ref _commandErrorTotal);
    }

    /// <summary>
    /// Returns a snapshot of conversation routing stats for the activity summary.
    /// </summary>
    public ConversationStats GetStats()
    {
        var parsed = Interlocked.Read(ref _commandParsedTotal);
        var llm = Interlocked.Read(ref _llmFallbackTotal);
        var errors = Interlocked.Read(ref _commandErrorTotal);
        var total = parsed + llm;

        return new ConversationStats
        {
            CommandParsed = parsed,
            LlmFallback = llm,
            Errors = errors,
            Total = total,
            CommandRate = total > 0 ? (double)parsed / total : 0,
        };
    }
}
