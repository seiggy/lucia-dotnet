using lucia.EvalHarness.Optimization;
using lucia.EvalHarness.Tests.TestDoubles;
using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Tests;

public sealed class OptimizerAvailabilityTests
{
    [Fact]
    public async Task OptimizeAsync_UnavailableScores_AreNotSynthesizedAsZero()
    {
        var client = new ScriptedChatClient(_ => Task.FromResult(new ChatResponse(
            new ChatMessage(ChatRole.Assistant, """{"analysis":"ok","suggestions":[]}"""))));
        var optimizer = new PromptOptimizer(client);

        var result = await optimizer.OptimizeAsync(
            "agent",
            "model",
            "prompt",
            [EvalResultFactory.Create(null)],
            [EvalResultFactory.Create(null)]);

        Assert.Null(result.CurrentScore);
        Assert.Null(result.BaselineScore);
    }
}
