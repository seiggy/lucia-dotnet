using FakeItEasy;
using lucia.AgentHost.Conversation.Templates;
using lucia.Agents.Abstractions;
using lucia.Agents.Orchestration;
using lucia.Agents.Orchestration.Models;
using lucia.Tests.TestDoubles;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Orchestration;

public sealed class PersonalityOutputBudgetTests : TestBase
{
    private const string Original = "The door could not be locked. Please check it manually.";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingBudget_Uses4096TokenFallback(bool fastPath)
    {
        using var client = Reply(ChatFinishReason.Stop);
        await RewriteAsync(client, fastPath);
        Assert.Equal(4096, Assert.Single(client.CapturedOptions)?.MaxOutputTokens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TruncatedRewrite_PreservesOriginalFailureInsteadOfPartialReply(bool fastPath)
    {
        using var client = Reply(ChatFinishReason.Length);
        Assert.Equal(Original, await RewriteAsync(client, fastPath));
    }

    [Theory]
    [InlineData(false, 128)]
    [InlineData(true, 128)]
    [InlineData(false, 8192)]
    [InlineData(true, 8192)]
    public async Task ExplicitBudget_OverridesFallbackForBothPaths(bool fastPath, int configured)
    {
        using var client = Reply(ChatFinishReason.Stop);
        await RewriteAsync(client, fastPath, configured);
        Assert.Equal(configured, Assert.Single(client.CapturedOptions)?.MaxOutputTokens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteLongRewrite_IsReturnedWithoutCharacterClipping(bool fastPath)
    {
        var reply = new string('x', 600);
        using var client = new StubChatClient([
            _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)) { FinishReason = ChatFinishReason.Stop }
        ]);
        Assert.Equal(reply, await RewriteAsync(client, fastPath));
        Assert.Equal(4096, Assert.Single(client.CapturedOptions)?.MaxOutputTokens);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DirectAggregator_InvalidLimitIsRejected(int configured)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResultAggregatorExecutor(
            CreateLogger<ResultAggregatorExecutor>(), Options.Create(new ResultAggregatorOptions()),
            personalityMaxOutputTokens: configured));
    }

    private async Task<string> RewriteAsync(StubChatClient client, bool fastPath, int? budget = null)
    {
        if (fastPath)
        {
            var resolver = A.Fake<IChatClientResolver>();
            A.CallTo(() => resolver.ResolveAsync(A<string?>._, A<CancellationToken>._)).Returns(client);
            var options = A.Fake<IOptionsMonitor<PersonalityPromptOptions>>();
            A.CallTo(() => options.CurrentValue).Returns(new PersonalityPromptOptions
            {
                Instructions = "Speak briefly and preserve the exact outcome.", MaxOutputTokens = budget
            });
            var renderer = new PersonalityResponseRenderer(resolver, options, CreateLogger<PersonalityResponseRenderer>());
            return await renderer.RenderAsync("SecurityControlSkill", "lock", Original, new Dictionary<string, string>());
        }

        var aggregator = new ResultAggregatorExecutor(CreateLogger<ResultAggregatorExecutor>(), Options.Create(new ResultAggregatorOptions()),
            client, "Speak briefly and preserve the exact outcome.", personalityMaxOutputTokens: budget);
        var result = await aggregator.HandleAsync([new OrchestratorAgentResponse
        {
            AgentId = "synthetic-agent", Content = Original, Success = true
        }], A.Fake<IWorkflowContext>(), CancellationToken.None);
        return result.Text!;
    }

    private static StubChatClient Reply(ChatFinishReason reason) => new([
        _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "The door is")) { FinishReason = reason }
    ]);
}
