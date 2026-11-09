using lucia.Agents.Abstractions;
using lucia.Agents.Family;
using lucia.Agents.Harness;
using lucia.Agents.Integration;
using lucia.Agents.Orchestration;
using Microsoft.Agents.AI;
using Microsoft.FeatureManagement;

namespace lucia.AgentHost.Harness;

/// <summary>
/// Runs the V2 assistant for members who opted in while the <see cref="FeatureName"/> flag is
/// on. Everyone else stays on the V1 orchestrator. A failed V2 run is reported, never replayed
/// through V1, so an action cannot run twice.
/// </summary>
public sealed class AssistantHarnessRunner(
    IFeatureManager features,
    WorkflowFactory workflows,
    IChatClientResolver clients,
    TracingChatClientFactory tracing,
    ILoggerFactory loggerFactory)
{
    public const string FeatureName = "AssistantHarness";

    public async Task<bool> IsEnabledForAsync(FamilyMember? member) =>
        member is { UseAssistantHarness: true }
        && await features.IsEnabledAsync(FeatureName).ConfigureAwait(false);

    public async Task<AgentResponse> RunAsync(string prompt, CancellationToken cancellationToken)
    {
        var capabilities = await workflows.ResolveAgentsAsync(cancellationToken).ConfigureAwait(false);
        var client = tracing.Wrap(
            await clients.ResolveAsync(null, cancellationToken).ConfigureAwait(false),
            AssistantHarnessFactory.AgentId);
        var agent = AssistantHarnessFactory.Create(client, capabilities, tracing.AIContextProviders, loggerFactory);
        return await agent.RunAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
