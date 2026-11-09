using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Family;
using lucia.Agents.Harness;
using lucia.Tests.TestDoubles;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Family;

public sealed class AssistantHarnessFactoryTests
{
    private static readonly ActionBroker Broker = new(
        A.Fake<IActionRecordStore>(), A.Fake<IFamilyMemberStore>(), TimeProvider.System, NullLogger<ActionBroker>.Instance);

    private static ChatClientAgent Capability(string id, StubChatClient client) => new(client, new ChatClientAgentOptions
    {
        Id = id,
        Name = id,
        Description = $"Handles {id} requests.",
    });

    private static Func<StubChatClient.InvocationContext, ChatResponse> Text(string text) =>
        _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, text));

    private static Func<StubChatClient.InvocationContext, ChatResponse> Call(string function, string query) =>
        _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, [
            new FunctionCallContent("call-1", function, new Dictionary<string, object?> { ["query"] = query }),
        ]));

    [Fact]
    public async Task Coordinator_OffersOnlyLuciaCapabilities()
    {
        using var coordinator = new StubChatClient([Text("done")]);
        using var lights = new StubChatClient([]);
        using var climate = new StubChatClient([]);
        using var orchestrator = new StubChatClient([]);
        var harness = AssistantHarnessFactory.Create(
            coordinator,
            [Capability("light-agent", lights), Capability("climate-agent", climate), Capability("orchestrator", orchestrator)],
            contextProviders: [],
            NullLoggerFactory.Instance);

        await harness.RunAsync("hello");

        var tools = coordinator.CapturedOptions[0]!.Tools!.Select(tool => tool.Name).Order().ToList();
        Assert.Equal(["climate_agent", "light_agent"], tools);
        Assert.Equal(AssistantHarnessFactory.AgentId, harness.Id);
    }

    [Fact]
    public async Task Coordinator_DelegatesToACapabilityAgent()
    {
        using var coordinator = new StubChatClient([Call("light_agent", "turn on the kitchen"), Text("The kitchen is on.")]);
        using var lights = new StubChatClient([Text("Kitchen lights are on.")]);
        var harness = AssistantHarnessFactory.Create(coordinator, [Capability("light-agent", lights)], [], NullLoggerFactory.Instance);

        var response = await harness.RunAsync("turn on the kitchen");

        Assert.Equal(1, lights.InvocationCount);
        Assert.Equal("The kitchen is on.", response.Text);
    }

    [Fact]
    public async Task DelegationForAChild_IsNotHeldForAGuardian_ButDeniedAgentsStillRefuse()
    {
        using var coordinator = new StubChatClient([
            Call("light_agent", "turn on the kitchen"),
            Call("security_agent", "unlock the door"),
            context => new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                string.Join(" | ", context.Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Select(r => r.Result?.ToString())))),
        ]);
        using var lights = new StubChatClient([Text("Kitchen lights are on.")]);
        using var security = new StubChatClient([Text("Door unlocked.")]);
        var capabilities = new AIAgent[]
        {
            Capability("light-agent", lights).AsBuilder().UseLuciaActionBroker().Build(),
            Capability("security-agent", security).AsBuilder().UseLuciaActionBroker().Build(),
        };
        var harness = AssistantHarnessFactory.Create(coordinator, capabilities, [], NullLoggerFactory.Instance);
        var child = new FamilyMember { Id = "kid", DisplayName = "Sam", Role = FamilyRole.Child, GuardianIds = ["parent"] };

        AgentResponse response;
        using (MemberActionScope.Begin(child, Broker))
        {
            response = await harness.RunAsync("turn on the kitchen and unlock the door");
        }

        Assert.Equal(1, lights.InvocationCount);
        Assert.Equal(0, security.InvocationCount);
        Assert.Contains("Kitchen lights are on.", response.Text);
        Assert.Contains("isn't allowed", response.Text);
    }
}
