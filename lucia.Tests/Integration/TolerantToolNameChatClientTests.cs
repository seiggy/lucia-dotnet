using System.Linq;
using lucia.Agents.Integration;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Integration;

public sealed class TolerantToolNameChatClientTests
{
    private const string FullGetLiveContext = "homeassistant__GetLiveContext";
    private static readonly string[] KnownTools = [FullGetLiveContext, "llm__GetDateTime"];

    private static TolerantToolNameChatClient CreateClient(IChatClient inner) =>
        new(inner, KnownTools, NullLogger<TolerantToolNameChatClient>.Instance);

    private static StubChatClient WithToolCallResponse(string callId, string name, IDictionary<string, object?>? arguments = null) =>
        new([_ => new ChatResponse(
            [new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, name, arguments)])])]);

    [Fact]
    public async Task BareNameWithUniqueSuffix_IsRewrittenToFullName()
    {
        var inner = WithToolCallResponse("call_1", "GetLiveContext", new Dictionary<string, object?> { ["domain"] = "vacuum" });
        var client = CreateClient(inner);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Is the vacuum cleaning?")]);

        var call = Assert.IsType<FunctionCallContent>(response.Messages!.Single().Contents.Single());
        Assert.Equal(FullGetLiveContext, call.Name);
        Assert.Equal("call_1", call.CallId);
        Assert.Equal("vacuum", call.Arguments!["domain"]?.ToString());
    }

    [Fact]
    public async Task ExactFullName_PassesThroughUnchanged()
    {
        var inner = WithToolCallResponse("call_1", FullGetLiveContext);
        var client = CreateClient(inner);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Is the vacuum cleaning?")]);

        var call = Assert.IsType<FunctionCallContent>(response.Messages!.Single().Contents.Single());
        Assert.Equal(FullGetLiveContext, call.Name);
    }

    [Fact]
    public async Task AmbiguousSuffix_LeftUnchanged()
    {
        var inner = WithToolCallResponse("call_1", "GetState");
        var client = new TolerantToolNameChatClient(
            inner, ["ha__GetState", "other__GetState"], NullLogger<TolerantToolNameChatClient>.Instance);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "what state?")]);

        var call = Assert.IsType<FunctionCallContent>(response.Messages!.Single().Contents.Single());
        Assert.Equal("GetState", call.Name);
    }

    [Fact]
    public async Task UnknownName_LeftUnchanged()
    {
        var inner = WithToolCallResponse("call_1", "NoSuchTool");
        var client = CreateClient(inner);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "do it")]);

        var call = Assert.IsType<FunctionCallContent>(response.Messages!.Single().Contents.Single());
        Assert.Equal("NoSuchTool", call.Name);
    }

    [Fact]
    public async Task LowercaseBareName_ResolvesToCanonicalFullName()
    {
        var inner = WithToolCallResponse("call_1", "getlivecontext");
        var client = CreateClient(inner);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Is the vacuum cleaning?")]);

        var call = Assert.IsType<FunctionCallContent>(response.Messages!.Single().Contents.Single());
        Assert.Equal(FullGetLiveContext, call.Name);
    }

    [Fact]
    public async Task TextOnlyResponse_Unchanged()
    {
        var inner = new StubChatClient([_ => new ChatResponse([new ChatMessage(ChatRole.Assistant, "Hello.")])]);
        var client = CreateClient(inner);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal("Hello.", response.Messages!.Single().Text);
        Assert.Empty(response.Messages.Single().Contents.OfType<FunctionCallContent>());
    }

    [Fact]
    public async Task StreamingToolCall_IsNormalized()
    {
        var inner = new ScriptedStreamingChatClient(
        [
            new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("call_1", "GetLiveContext")])
        ]);
        var client = CreateClient(inner);

        var updates = await client
            .GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Is the vacuum cleaning?")])
            .ToListAsync();

        var call = Assert.IsType<FunctionCallContent>(updates.Single().Contents.Single());
        Assert.Equal(FullGetLiveContext, call.Name);
        Assert.Equal("call_1", call.CallId);
    }
}
