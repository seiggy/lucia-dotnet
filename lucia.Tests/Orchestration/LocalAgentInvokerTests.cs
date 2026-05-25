using lucia.Agents;
using lucia.Agents.Orchestration;
using lucia.Agents.Orchestration.Models;
using lucia.Tests.TestDoubles;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Orchestration;

public sealed class LocalAgentInvokerTests
{
    [Fact]
    public void AgentInvokerOptions_DefaultTimeout_IsThirtySeconds()
    {
        var options = new AgentInvokerOptions();

        Assert.Equal(TimeSpan.FromSeconds(30), options.Timeout);
    }

    [Fact]
    public async Task InvokeAsync_WhenAgentRunsPastTimeout_ReturnsGracefulTimeoutError()
    {
        using var telemetry = new AgentsTelemetrySource();
        var invoker = CreateInvoker(TimeSpan.FromMilliseconds(50), telemetry);

        var response = await invoker.InvokeAsync(new ChatMessage(ChatRole.User, "play flash by queen"), CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal("the request timed out after 50ms before the agent finished.", response.ErrorMessage);
    }

    [Fact]
    public async Task InvokeAsync_WhenCallerCancelsRequest_ReturnsGracefulCancellationError()
    {
        using var telemetry = new AgentsTelemetrySource();
        var invoker = CreateInvoker(TimeSpan.FromSeconds(30), telemetry);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var response = await invoker.InvokeAsync(new ChatMessage(ChatRole.User, "play flash by queen"), cts.Token);

        Assert.False(response.Success);
        Assert.Equal("the request was interrupted before the agent finished.", response.ErrorMessage);
    }

    private static LocalAgentInvoker CreateInvoker(TimeSpan timeout, AgentsTelemetrySource telemetrySource)
    {
        var chatClient = new DelayedChatClient(TimeSpan.FromSeconds(5));
        var agent = new ChatClientAgent(
            chatClient,
            new ChatClientAgentOptions
            {
                Id = "music-agent",
                Name = "music-agent",
                Description = "Test agent",
                ChatOptions = new ChatOptions
                {
                    Instructions = "Keep responses brief."
                }
            },
            NullLoggerFactory.Instance);

        return new LocalAgentInvoker(
            "music-agent",
            agent,
            null!,
            NullLogger.Instance,
            telemetrySource,
            Options.Create(new AgentInvokerOptions { Timeout = timeout }));
    }
}
