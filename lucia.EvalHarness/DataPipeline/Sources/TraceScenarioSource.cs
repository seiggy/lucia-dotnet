using lucia.Agents.Training;
using lucia.Agents.Training.Models;
using lucia.EvalHarness.DataPipeline.Models;
using Microsoft.Extensions.Logging;

namespace lucia.EvalHarness.DataPipeline.Sources;

/// <summary>
/// Converts conversation traces into evaluation scenarios.
/// Extracts conversation flow, routing decisions, tool calls, and agent interactions.
/// </summary>
public sealed class TraceScenarioSource : IEvalScenarioSource
{
    private readonly ITraceRepository _repository;
    private readonly ILogger<TraceScenarioSource> _logger;

    public TraceScenarioSource(
        ITraceRepository repository,
        ILogger<TraceScenarioSource> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<List<EvalScenario>> GetScenariosAsync(ScenarioFilter? filter = null, CancellationToken ct = default)
    {
        var scenarios = new List<EvalScenario>();

        // Build filter criteria for trace retrieval
        var traceFilter = new TraceFilterCriteria
        {
            Page = 1,
            PageSize = filter?.Limit ?? 100
        };

        if (filter?.Agent is not null)
        {
            traceFilter.AgentFilter = filter.Agent;
        }

        if (filter?.ErrorsOnly == true)
        {
            // Only retrieve errored traces for regression testing
            // Note: LabelFilter is for manual labels, so we'll filter errors after retrieval
        }

        var result = await _repository.ListTracesAsync(traceFilter, ct).ConfigureAwait(false);

        foreach (var trace in result.Items)
        {
            if (filter?.ErrorsOnly == true && !trace.IsErrored)
            {
                continue;
            }

            try
            {
                var scenario = ConvertTraceToScenario(trace);
                scenarios.Add(scenario);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to convert trace {TraceId} to scenario", trace.Id);
            }
        }

        _logger.LogInformation("Converted {Count} traces into eval scenarios", scenarios.Count);
        return scenarios;
    }

    private static EvalScenario ConvertTraceToScenario(ConversationTrace trace)
    {
        var category = DetermineCategory(trace);
        var expectedAgent = trace.Routing?.SelectedAgentId ?? "unknown";

        var scenario = new EvalScenario
        {
            Id = $"trace_{trace.Id}",
            Description = trace.IsErrored
                ? $"Regression: Trace that errored with '{trace.ErrorMessage}'"
                : $"Trace from {trace.Timestamp:yyyy-MM-dd}",
            Category = category,
            UserPrompt = trace.UserInput,
            ExpectedAgent = expectedAgent,
            Source = $"trace-{trace.Id}",
            Metadata = new Dictionary<string, string>
            {
                ["trace_id"] = trace.Id,
                ["timestamp"] = trace.Timestamp.ToString("O"),
                ["duration_ms"] = trace.TotalDurationMs.ToString("F2"),
                ["is_errored"] = trace.IsErrored.ToString()
            }
        };

        // Extract tool calls from successful agent executions
        foreach (var execution in trace.AgentExecutions.Where(e => e.Success && e.ToolCalls.Count > 0))
        {
            foreach (var toolCall in execution.ToolCalls)
            {
                scenario.ExpectedToolCalls.Add(new ExpectedToolCall
                {
                    Tool = toolCall.ToolName,
                    Arguments = ParseToolArguments(toolCall.Arguments)
                });
            }
        }

        // For error scenarios, the final response should NOT contain error messages
        if (trace.IsErrored)
        {
            scenario.ResponseMustNotContain.Add("encountered an issue");
            scenario.ResponseMustNotContain.Add("try again");
            scenario.Metadata["is_regression"] = "true";
            scenario.Metadata["original_error"] = trace.ErrorMessage ?? "Unknown error";

            // Add routing info if available
            if (trace.Routing is not null)
            {
                scenario.Criteria.Add($"Routes to {trace.Routing.SelectedAgentId} with confidence >= {trace.Routing.Confidence:F0}%");
            }
        }
        else
        {
            // For successful traces, extract expected response patterns
            if (!string.IsNullOrWhiteSpace(trace.FinalResponse))
            {
                scenario.Criteria.Add("Provides a clear response");
            }
        }

        // Add routing criteria if available
        if (trace.Routing is not null)
        {
            scenario.Criteria.Add($"Routes to {trace.Routing.SelectedAgentId}");
            scenario.Metadata["routing_confidence"] = trace.Routing.Confidence.ToString("F2");
            scenario.Metadata["routing_reasoning"] = trace.Routing.Reasoning ?? "No reasoning provided";
        }

        return scenario;
    }

    private static string DetermineCategory(ConversationTrace trace)
    {
        if (trace.IsErrored)
        {
            return "regression";
        }

        if (trace.Routing?.SelectedAgentId is not null)
        {
            return trace.Routing.SelectedAgentId.Contains("light", StringComparison.OrdinalIgnoreCase)
                ? "light-control"
                : trace.Routing.SelectedAgentId.Contains("climate", StringComparison.OrdinalIgnoreCase)
                ? "climate-control"
                : trace.Routing.SelectedAgentId.Contains("music", StringComparison.OrdinalIgnoreCase)
                ? "music-playback"
                : "conversation";
        }

        return "conversation";
    }

    private static Dictionary<string, string> ParseToolArguments(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return [];
        }

        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(argumentsJson);
            if (parsed is null)
            {
                return [];
            }

            // Convert all values to strings for YAML export
            return parsed.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value?.ToString() ?? string.Empty
            );
        }
        catch
        {
            return [];
        }
    }
}
