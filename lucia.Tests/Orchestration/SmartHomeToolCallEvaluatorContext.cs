#pragma warning disable AIEVAL001 // Microsoft.Extensions.AI.Evaluation is experimental

using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Provides tool definitions to <see cref="SmartHomeToolCallEvaluator"/> so the
/// judge can assess whether the agent used the correct tools.
/// </summary>
public sealed class SmartHomeToolCallEvaluatorContext : EvaluationContext
{
    public const string ContextName = "SmartHomeToolCallEvaluatorContext";

    /// <summary>
    /// The set of tool definitions that were available to the agent during execution.
    /// </summary>
    public IReadOnlyList<AITool> ToolDefinitions { get; }

    public SmartHomeToolCallEvaluatorContext(IReadOnlyList<AITool> toolDefinitions)
        : base(ContextName, [new TextContent($"Tool count: {toolDefinitions.Count}")])
    {
        ToolDefinitions = toolDefinitions;
    }

    public SmartHomeToolCallEvaluatorContext(params AITool[] toolDefinitions)
        : this((IReadOnlyList<AITool>)toolDefinitions)
    {
    }
}
