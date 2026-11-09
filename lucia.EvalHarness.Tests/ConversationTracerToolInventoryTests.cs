using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Tests.TestDoubles;
using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Tests;

public sealed class ConversationTracerToolInventoryTests
{
    [Fact]
    public async Task GetResponseAsync_CollectsOfferedToolsAcrossRoundsAndClearsThemOnReset()
    {
        using var tracer = new ConversationTracer(ScriptedChatClient.Returning("done"));
        await tracer.GetResponseAsync([new ChatMessage(ChatRole.User, "first")], new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => "ok", "ListScenes")]
        });
        await tracer.GetResponseAsync([new ChatMessage(ChatRole.User, "second")], new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => "ok", "ActivateScene")]
        });

        Assert.NotNull(tracer.AvailableToolNames);
        Assert.Equal(new[] { "ActivateScene", "ListScenes" }, tracer.AvailableToolNames.Order().ToArray());
        tracer.Reset();
        Assert.Null(tracer.AvailableToolNames);
        Assert.Empty(tracer.Turns);
    }

    [Fact]
    public async Task GetResponseAsync_DistinguishesExplicitEmptyInventoryFromUnknownInventory()
    {
        using var tracer = new ConversationTracer(ScriptedChatClient.Returning("done"));
        await tracer.GetResponseAsync([new ChatMessage(ChatRole.User, "unknown")]);
        Assert.Null(tracer.AvailableToolNames);

        await tracer.GetResponseAsync([new ChatMessage(ChatRole.User, "no tools")], new ChatOptions { Tools = [] });
        Assert.NotNull(tracer.AvailableToolNames);
        Assert.Empty(tracer.AvailableToolNames);
    }
}
