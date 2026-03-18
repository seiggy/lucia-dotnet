using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace lucia.AgentHost.Conversation;

/// <summary>
/// Telemetry instrumentation for the /api/conversation endpoint.
/// Tracks command parser vs LLM fallback routing and execution performance.
/// </summary>
public sealed class ConversationTelemetry
{
    private readonly Counter<long> _commandParsedCounter;
    private readonly Counter<long> _llmFallbackCounter;
    private readonly Counter<long> _commandErrorCounter;
    private readonly Histogram<double> _commandParsedDuration;
    private readonly Histogram<double> _llmFallbackDuration;
    private readonly ActivitySource _activitySource;

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
    }

    public void RecordLlmFallback(double durationMs)
    {
        _llmFallbackCounter.Add(1);
        _llmFallbackDuration.Record(durationMs);
    }

    public void RecordCommandError(string skillId, string action)
    {
        _commandErrorCounter.Add(1,
            new KeyValuePair<string, object?>("skillId", skillId),
            new KeyValuePair<string, object?>("action", action));
    }
}
