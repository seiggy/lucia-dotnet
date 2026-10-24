using System.Text.Json;
using lucia.Agents.Integration;
using lucia.Agents.Orchestration;
using lucia.Agents.Training;
using lucia.Agents.Training.Models;
using lucia.Data.InMemory;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Training;

public sealed class AgentToolResultPolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptIn_SanitizesResultsFromRequestOrResponse(bool inResponse)
    {
        var (trace, inner) = await RunAsync(new() { CaptureToolResults = true }, inResponse: inResponse);
        var calls = Assert.Single(trace!.AgentExecutions).ToolCalls;
        Assert.Equal("status: offline; [REDACTED]", calls.Single(c => c.CallId == "call-1").Result);
        Assert.Null(calls.Single(c => c.CallId == "call-2").Result);
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(trace));
        if (!inResponse)
            Assert.Contains("SECRET", JsonSerializer.Serialize(inner.CapturedMessages));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureDisabled_DoesNotPersistOnSuccessOrException(bool throws)
    {
        var (trace, inner) = await RunAsync(new() { Enabled = false, CaptureToolResults = true }, throws: throws);
        Assert.Null(trace);
        Assert.Equal(2, inner.InvocationCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultPolicy_OmitsResultsIncludingRawToolMessageText(bool inResponse)
    {
        var (trace, _) = await RunAsync(new(), inResponse: inResponse);
        Assert.All(Assert.Single(trace!.AgentExecutions).ToolCalls, call => Assert.Null(call.Result));
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(trace));
        Assert.DoesNotContain("RAW_TOOL_TEXT", JsonSerializer.Serialize(trace));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnmatchedCallId_DoesNotAttachToAnotherCall(bool inResponse)
    {
        var (trace, _) = await RunAsync(new() { CaptureToolResults = true }, inResponse: inResponse, callId: "unknown");
        var calls = Assert.Single(trace!.AgentExecutions).ToolCalls;
        Assert.Equal(2, calls.Count);
        Assert.All(calls, call => Assert.Null(call.Result));
    }

    [Fact]
    public async Task KnownException_RecordsErrorWithoutCapturingPayloadByDefault()
    {
        var (trace, _) = await RunAsync(new(), knownException: true);
        var call = Assert.Single(trace!.AgentExecutions).ToolCalls.Single(c => c.CallId == "call-1");
        Assert.True(call.ResultIsError);
        Assert.Null(call.Result);
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(trace));
    }

    [Fact]
    public async Task UnknownOutcome_IsNotLabeledSuccessful()
    {
        var (trace, _) = await RunAsync(new());
        Assert.Null(Assert.Single(trace!.AgentExecutions).ToolCalls.Single(c => c.CallId == "call-1").ResultIsError);
    }

    [Fact]
    public async Task BoundedPreview_RecordsTruncation()
    {
        var (trace, _) = await RunAsync(new() { CaptureToolResults = true, MaxToolResultBytes = 6 });
        var call = Assert.Single(trace!.AgentExecutions).ToolCalls.Single(c => c.CallId == "call-1");
        Assert.Equal("status", call.Result);
        Assert.True(call.ResultTruncated);
    }

    [Fact]
    public async Task InferenceException_DoesNotBypassSanitization()
    {
        var (trace, _) = await RunAsync(new() { CaptureToolResults = true }, throws: true);
        Assert.True(trace!.IsErrored);
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(trace));
        Assert.Contains("[REDACTED]", JsonSerializer.Serialize(trace));
    }

    [Fact]
    public async Task Factory_UsesConfiguredCapturePolicy()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITraceRepository>(new InMemoryTraceRepository());
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.Configure<TraceCaptureOptions>(options => options.Enabled = false);
        services.AddSingleton<TracingChatClientFactory>();
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<TracingChatClientFactory>();
        using var client = factory.Wrap(new StubChatClient([
            _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done."))
        ]), "sensor-agent");
        var session = Guid.NewGuid().ToString("N");
        AgentTracingChatClient.SetSessionId(session);
        Assert.Equal("Done.", (await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Check.")])).Text);
        Assert.Empty(await provider.GetRequiredService<ITraceRepository>().GetTracesBySessionIdAsync(session));
    }

    [Fact]
    public async Task DisabledCapture_PreservesLiveToolMetadata()
    {
        var channel = new LiveActivityChannel();
        using var inner = new StubChatClient([
            _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "read_sensor")]))
        ]);
        using var client = new AgentTracingChatClient(inner, "sensor-agent", new InMemoryTraceRepository(),
            NullLogger<AgentTracingChatClient>.Instance, channel, Options.Create(new TraceCaptureOptions { Enabled = false }));
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Check.")]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var events = channel.ReadAllAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await events.MoveNextAsync());
        Assert.Equal("read_sensor", events.Current.ToolName);
    }

    [Fact]
    public async Task DisabledCapture_PreservesCancellation()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        using var inner = new StubChatClient([context => throw new OperationCanceledException(context.CancellationToken)]);
        using var client = new AgentTracingChatClient(inner, "sensor-agent", new InMemoryTraceRepository(),
            NullLogger<AgentTracingChatClient>.Instance, traceOptions: Options.Create(new TraceCaptureOptions { Enabled = false }));
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetResponseAsync([new ChatMessage(ChatRole.User, "Check.")], cancellationToken: canceled.Token));
        Assert.Equal(canceled.Token, error.CancellationToken);
    }

    private static async Task<(ConversationTrace? Trace, StubChatClient Inner)> RunAsync(
        TraceCaptureOptions options, bool inResponse = false, bool throws = false,
        string callId = "call-1", bool knownException = false)
    {
        var repository = new InMemoryTraceRepository();
        var result = new FunctionResultContent(callId, "status: offline; password=SECRET")
        {
            Exception = knownException ? new InvalidOperationException("SECRET exception") : null
        };
        var tool = new ChatMessage(ChatRole.Tool, [result, new TextContent("RAW_TOOL_TEXT")]) { MessageId = "tool-message" };
        using var inner = new StubChatClient([
            _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, [
                new FunctionCallContent("call-1", "read_sensor"), new FunctionCallContent("call-2", "other_sensor")
            ])),
            _ => throws ? throw new InvalidOperationException("Synthetic inference failure") :
                new ChatResponse(inResponse ? [tool, new ChatMessage(ChatRole.Assistant, "Finished.")] :
                    [new ChatMessage(ChatRole.Assistant, "Finished.")])
        ]);
        using var client = new AgentTracingChatClient(inner, "sensor-agent", repository,
            NullLogger<AgentTracingChatClient>.Instance, traceOptions: Options.Create(options));
        var session = Guid.NewGuid().ToString("N");
        AgentTracingChatClient.SetSessionId(session);
        var user = new ChatMessage(ChatRole.User, "Check the sensor.");
        var first = await client.GetResponseAsync([user]);
        var next = new List<ChatMessage> { user, first.Messages.Single() };
        if (!inResponse) next.Add(tool);
        if (throws)
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync(next));
        else
            Assert.EndsWith("Finished.", (await client.GetResponseAsync(next)).Text);
        Assert.Equal("status: offline; password=SECRET", result.Result);
        return ((await repository.GetTracesBySessionIdAsync(session)).SingleOrDefault(), inner);
    }
}
