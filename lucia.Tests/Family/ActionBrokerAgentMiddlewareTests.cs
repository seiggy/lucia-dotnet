using lucia.Agents.Family;
using lucia.Data.Sqlite;
using lucia.Tests.Data.Providers;
using lucia.Tests.TestDoubles;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Family;

public sealed class ActionBrokerAgentMiddlewareTests : IDisposable
{
    private readonly SqliteStoreEnvironment _environment;
    private readonly ActionBroker _broker;
    private readonly FamilyMember _parent = new() { Id = "parent", DisplayName = "Alex", Role = FamilyRole.Adult };
    private readonly FamilyMember _child = new() { Id = "kid", DisplayName = "Sam", Role = FamilyRole.Child, GuardianIds = ["parent"] };
    private int _toolCalls;

    public ActionBrokerAgentMiddlewareTests()
    {
        _environment = SqliteStoreEnvironment.CreateAsync().GetAwaiter().GetResult();
        var members = new SqliteFamilyMemberStore(_environment.Config);
        members.UpsertAsync(_parent).GetAwaiter().GetResult();
        members.UpsertAsync(_child).GetAwaiter().GetResult();
        _broker = new ActionBroker(
            new SqliteActionRecordStore(_environment.Config),
            members,
            TimeProvider.System,
            NullLogger<ActionBroker>.Instance);
    }

    public void Dispose() => _environment.Dispose();

    private (AIAgent Agent, StubChatClient Client) CreateAgent(string agentId = "climate-agent", bool withFunctionCalling = true)
    {
        var tool = AIFunctionFactory.Create(
            (int temperature) =>
            {
                _toolCalls++;
                return $"set to {temperature}";
            },
            "SetClimateTemperature");
        var client = new StubChatClient([
            _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, [
                new FunctionCallContent("call-1", "SetClimateTemperature", new Dictionary<string, object?> { ["temperature"] = 24 }),
            ])),
            context => new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                string.Join(" ", context.Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Select(r => r.Result?.ToString())))),
        ]);
        var agent = new ChatClientAgent(client, new ChatClientAgentOptions
        {
            Id = agentId,
            Name = agentId,
            UseProvidedChatClientAsIs = !withFunctionCalling,
            ChatOptions = new ChatOptions { Tools = [tool] },
        });
        return (agent.AsBuilder().UseLuciaActionBroker().Build(), client);
    }

    [Fact]
    public async Task WithoutAMemberScope_ToolsRunAsBefore()
    {
        var (agent, _) = CreateAgent();

        var response = await agent.RunAsync("make it warmer");

        Assert.Equal(1, _toolCalls);
        Assert.Contains("set to 24", response.Text);
    }

    [Fact]
    public async Task UnrestrictedMember_ToolsRun()
    {
        var (agent, _) = CreateAgent();

        using (MemberActionScope.Begin(_parent, _broker))
        {
            await agent.RunAsync("make it warmer");
        }

        Assert.Equal(1, _toolCalls);
    }

    [Fact]
    public async Task ChildWriteTool_IsHeldForAGuardianInsteadOfRunning()
    {
        var (agent, _) = CreateAgent();

        AgentResponse response;
        using (MemberActionScope.Begin(_child, _broker))
        {
            response = await agent.RunAsync("make it warmer");
        }

        Assert.Equal(0, _toolCalls);
        Assert.Contains("guardian", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeniedAgent_RefusesTheWholeRunWithoutCallingTheModel()
    {
        var (agent, client) = CreateAgent("security-agent");

        AgentResponse response;
        using (MemberActionScope.Begin(_child, _broker))
        {
            response = await agent.RunAsync("unlock the door");
        }

        Assert.Equal(0, client.InvocationCount);
        Assert.Equal(0, _toolCalls);
        Assert.False(string.IsNullOrWhiteSpace(response.Text));
    }

    [Fact]
    public async Task AgentWithoutMediatedTools_RefusesRestrictedMembersOnly()
    {
        var (restricted, restrictedClient) = CreateAgent(withFunctionCalling: false);
        using (MemberActionScope.Begin(_child, _broker))
        {
            await restricted.RunAsync("make it warmer");
        }
        Assert.Equal(0, restrictedClient.InvocationCount);

        var (open, openClient) = CreateAgent(withFunctionCalling: false);
        using (MemberActionScope.Begin(_parent, _broker))
        {
            await open.RunAsync("make it warmer");
        }
        Assert.Equal(1, openClient.InvocationCount);
    }

    [Fact]
    public async Task StreamingRun_IsAlsoRefusedForADeniedAgent()
    {
        var (agent, client) = CreateAgent("security-agent");

        var updates = new List<AgentResponseUpdate>();
        using (MemberActionScope.Begin(_child, _broker))
        {
            await foreach (var update in agent.RunStreamingAsync("unlock the door"))
            {
                updates.Add(update);
            }
        }

        Assert.Equal(0, client.InvocationCount);
        Assert.NotEmpty(updates);
    }
}
