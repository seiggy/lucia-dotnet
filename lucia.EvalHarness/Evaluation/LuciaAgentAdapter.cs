using AgentEval.Core;
using AgentEval.MAF;
using lucia.Agents.Abstractions;
using Microsoft.Agents.AI;

namespace lucia.EvalHarness.Evaluation;

/// <summary>
/// Wraps a real <see cref="ILuciaAgent"/> as an AgentEval <see cref="IEvaluableAgent"/>
/// by extracting the underlying <see cref="AIAgent"/> and wrapping it with
/// <see cref="MAFAgentAdapter"/>.
/// </summary>
public static class LuciaAgentAdapter
{
    /// <summary>
    /// Creates an evaluable agent from a real lucia agent instance.
    /// </summary>
    public static IEvaluableAgent FromLuciaAgent(ILuciaAgent agent)
    {
        return new MAFAgentAdapter(agent.GetAIAgent());
    }
}
