using System.Diagnostics;

namespace lucia.Agents.Orchestration;

/// <summary>
/// OpenTelemetry instrumentation for orchestration components.
/// </summary>
public static class OrchestrationTelemetry
{
    /// <summary>
    /// ActivitySource for orchestration tracing.
    /// </summary>
    public static readonly ActivitySource Source = new("Lucia.Orchestration", "1.0.0");
    
    /// <summary>
    /// Standard tag names for orchestration activities.
    /// </summary>
    public static class Tags
    {
        public const string AgentId = "agent.id";
        public const string Confidence = "agent.confidence";
        public const string TaskId = "task.id";
        public const string SessionId = "session.id";
        public const string ExecutionTime = "execution.time.ms";
        public const string Success = "execution.success";
        public const string ErrorMessage = "error.message";
        public const string RoutingDecision = "routing.decision";
        public const string AgentCount = "agent.count";
    }
}
