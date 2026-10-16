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

    [Fact]
    public async Task Rewrite_PreservesResponseAndMessageMetadata()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var message = new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call_1", "GetLiveContext")])
        {
            AuthorName = "test-model"
        };
        var original = new ChatResponse([message])
        {
            ModelId = "gemini-2.5-flash-lite",
            ResponseId = "resp_42",
            ConversationId = "conv_7",
            CreatedAt = createdAt,
            FinishReason = ChatFinishReason.Stop
        };
        var inner = new StubChatClient([_ => original]);
        var client = CreateClient(inner);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Is the vacuum cleaning?")]);

        Assert.Same(original, response);
        Assert.Equal("resp_42", response.ResponseId);
        Assert.Equal("conv_7", response.ConversationId);
        Assert.Equal(createdAt, response.CreatedAt);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);
        Assert.Equal("gemini-2.5-flash-lite", response.ModelId);

        var normalizedMessage = response.Messages!.Single();
        Assert.Same(message, normalizedMessage);
        Assert.Equal("test-model", normalizedMessage.AuthorName);

        var call = Assert.IsType<FunctionCallContent>(normalizedMessage.Contents.Single());
        Assert.Equal(FullGetLiveContext, call.Name);
        Assert.Equal("call_1", call.CallId);
    }

    [Fact]
    public async Task StreamingRewrite_PreservesUpdateMetadata()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var original = new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("call_1", "GetLiveContext")])
        {
            AuthorName = "test-model",
            ModelId = "gemini-2.5-flash-lite",
            ResponseId = "resp_42",
            ConversationId = "conv_7",
            MessageId = "msg_9",
            CreatedAt = createdAt,
            FinishReason = ChatFinishReason.Stop
        };
        var inner = new ScriptedStreamingChatClient([original]);
        var client = CreateClient(inner);

        var updates = await client
            .GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Is the vacuum cleaning?")])
            .ToListAsync();

        var update = updates.Single();
        Assert.Same(original, update);
        Assert.Equal("resp_42", update.ResponseId);
        Assert.Equal("conv_7", update.ConversationId);
        Assert.Equal("msg_9", update.MessageId);
        Assert.Equal("test-model", update.AuthorName);
        Assert.Equal(createdAt, update.CreatedAt);
        Assert.Equal(ChatFinishReason.Stop, update.FinishReason);

        var call = Assert.IsType<FunctionCallContent>(update.Contents.Single());
        Assert.Equal(FullGetLiveContext, call.Name);
        Assert.Equal("call_1", call.CallId);
    }
}
