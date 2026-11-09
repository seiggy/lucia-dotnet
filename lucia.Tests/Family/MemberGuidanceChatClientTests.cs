using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Family;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Family;

public sealed class MemberGuidanceChatClientTests
{
    private static readonly ActionBroker Broker = new(
        A.Fake<IActionRecordStore>(), A.Fake<IFamilyMemberStore>(), TimeProvider.System, NullLogger<ActionBroker>.Instance);

    [Fact]
    public async Task MemberGuidance_IsAppendedToTheAgentsInstructions()
    {
        using var inner = new StubChatClient([_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))]);
        var client = new MemberGuidanceChatClient(inner);
        var member = new FamilyMember
        {
            Id = "kid",
            DisplayName = "Sam",
            Role = FamilyRole.Child,
            LlmControls = new MemberLlmControls { SystemPrompt = "Use simple words.", BlockedTopics = ["horror movies"] },
        };
        var options = new ChatOptions { Instructions = "You control lights." };

        using (MemberActionScope.Begin(member, Broker))
        {
            await client.GetResponseAsync("hi", options);
        }

        var sent = inner.CapturedOptions[0]!.Instructions!;
        Assert.StartsWith("You control lights.", sent);
        Assert.Contains("Use simple words.", sent);
        Assert.Contains("horror movies", sent);
        Assert.Equal("You control lights.", options.Instructions);
    }

    [Fact]
    public async Task WithoutGuidance_OptionsAreUntouched()
    {
        using var inner = new StubChatClient([_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))]);
        var client = new MemberGuidanceChatClient(inner);
        var options = new ChatOptions { Instructions = "You control lights." };

        await client.GetResponseAsync("hi", options);

        Assert.Same(options, inner.CapturedOptions[0]);
    }
}
