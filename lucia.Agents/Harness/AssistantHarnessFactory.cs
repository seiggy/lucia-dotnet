using System.Text.RegularExpressions;
using lucia.Agents.Family;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Harness;

/// <summary>
/// Builds the V2 assistant: one Agent Framework <see cref="HarnessAgent"/> coordinator whose
/// only capabilities are Lucia's own agents. Every ambient Harness feature that could read
/// files, browse, run skills or scripts, or approve tools on its own is disabled, and the
/// action broker governs the coordinator and every delegated agent.
/// </summary>
public static partial class AssistantHarnessFactory
{
    public const string AgentId = "lucia-assistant";

    public const string TelemetrySourceName = "lucia.Harness";

    public const int MaximumIterations = 8;

    private const string Instructions = """
        You are Lucia, a household assistant. Answer directly when you can.
        Use the available agents to read or change the home; pass each one a clear, self-contained request.
        Report what the agents actually did. If an agent says something was not done, needs approval, or is
        not allowed, tell the user that plainly and do not try another way to do it.
        """;

    /// <summary>
    /// The V1 orchestrator is an alternative to this coordinator, not a capability of it.
    /// </summary>
    private const string V1OrchestratorName = "orchestrator";

    public static AIAgent Create(
        IChatClient chatClient,
        IReadOnlyList<AIAgent> capabilities,
        IEnumerable<AIContextProvider> contextProviders,
        ILoggerFactory loggerFactory)
    {
        var tools = capabilities
            .Where(agent => !string.Equals(agent.Id, AgentId, StringComparison.Ordinal)
                && !string.Equals(agent.Name, V1OrchestratorName, StringComparison.Ordinal))
            .Select(AsCapability)
            .ToList<AITool>();

        var options = new HarnessAgentOptions
        {
            Id = AgentId,
            Name = AgentId,
            Description = "Lucia's personal assistant coordinator.",
            HarnessInstructions = Instructions,
            ChatOptions = new ChatOptions { Tools = tools },
            AIContextProviders = contextProviders,
            MaximumIterationsPerRequest = MaximumIterations,
            OpenTelemetrySourceName = TelemetrySourceName,
            DisableFileMemory = true,
            DisableWebSearch = true,
            DisableAgentSkillsProvider = true,
            DisableToolAutoApproval = true,
            DisableTodoProvider = true,
            DisableAgentModeProvider = true,
            DisableCompaction = true,
        };

        return new HarnessAgent(chatClient, options, loggerFactory)
            .AsBuilder()
            .UseLuciaActionBroker()
            .Build();
    }

    private static AIFunction AsCapability(AIAgent agent) =>
        agent.AsAIFunction(new AIFunctionFactoryOptions
        {
            Name = FunctionName().Replace(agent.Id, "_"),
            Description = agent.Description ?? agent.Name ?? agent.Id,
            AdditionalProperties = new Dictionary<string, object?>
            {
                [ActionBrokerAgentBuilderExtensions.DelegatedAgentProperty] = agent.Id,
            },
        });

    [GeneratedRegex("[^A-Za-z0-9_]")]
    private static partial Regex FunctionName();
}
