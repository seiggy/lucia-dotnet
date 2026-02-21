using Microsoft.Extensions.Logging;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Compile-time structured logging for orchestration components.
/// </summary>
public static partial class OrchestrationLogMessages
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Router selected agent {AgentId} with confidence {Confidence:F2}")]
    public static partial void AgentSelected(this ILogger logger, string agentId, double confidence);
    
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Low confidence routing: selected agent {AgentId} with confidence {Confidence:F2}, reason: {Reasoning}")]
    public static partial void LowConfidenceRouting(
        this ILogger logger, 
        string agentId, 
        double confidence, 
        string reasoning);
    
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Agent execution timeout for {AgentId} after {TimeoutMs}ms")]
    public static partial void AgentTimeout(this ILogger logger, string agentId, int timeoutMs);
    
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Agent execution failed: agent {AgentId}, task {TaskId}")]
    public static partial void AgentExecutionFailure(
        this ILogger logger, 
        Exception ex,
        string agentId, 
        string taskId);
    
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Routing decision completed in {DurationMs}ms for request")]
    public static partial void RoutingCompleted(this ILogger logger, long durationMs);
    
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Aggregating {Count} agent responses")]
    public static partial void AggregatingResponses(this ILogger logger, int count);
    
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Partial agent failure: {SuccessCount} succeeded, {FailureCount} failed")]
    public static partial void PartialAgentFailure(
        this ILogger logger, 
        int successCount, 
        int failureCount);
    
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Context extracted from history: location={Location}, previousAgents={PreviousAgents}")]
    public static partial void ContextExtracted(
        this ILogger logger, 
        string? location, 
        string previousAgents);
}
