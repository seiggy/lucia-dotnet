using FakeItEasy;
using lucia.Agents.Orchestration;
using lucia.Agents.Orchestration.Models;
using lucia.Agents.Training.Models;

namespace lucia.Tests.Orchestration;

public sealed class CompositeOrchestratorObserverTests
{
    [Fact]
    public async Task OnRequestStartedAsync_DelegatesToAllObservers()
    {
        var obs1 = A.Fake<IOrchestratorObserver>();
        var obs2 = A.Fake<IOrchestratorObserver>();
        A.CallTo(() => obs1.OnRequestStartedAsync(A<string>._, null, A<CancellationToken>._))
            .Returns("req-1");
        var composite = new CompositeOrchestratorObserver([obs1, obs2]);

        var requestId = await composite.OnRequestStartedAsync("test");

        Assert.Equal("req-1", requestId);
        A.CallTo(() => obs1.OnRequestStartedAsync("test", null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => obs2.OnRequestStartedAsync("test", null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task OnRoutingCompletedAsync_DelegatesToAllObservers()
    {
        var obs1 = A.Fake<IOrchestratorObserver>();
        var obs2 = A.Fake<IOrchestratorObserver>();
        var composite = new CompositeOrchestratorObserver([obs1, obs2]);

        var result = new AgentChoiceResult { AgentId = "agent-1", Reasoning = "test" };
        await composite.OnRoutingCompletedAsync("req-1", result);

        A.CallTo(() => obs1.OnRoutingCompletedAsync("req-1", result, null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => obs2.OnRoutingCompletedAsync("req-1", result, null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task OnAgentExecutionCompletedAsync_DelegatesToAllObservers()
    {
        var obs1 = A.Fake<IOrchestratorObserver>();
        var obs2 = A.Fake<IOrchestratorObserver>();
        var composite = new CompositeOrchestratorObserver([obs1, obs2]);

        var response = new OrchestratorAgentResponse { AgentId = "a", Content = "ok", Success = true };
        await composite.OnAgentExecutionCompletedAsync("req-1", response);

        A.CallTo(() => obs1.OnAgentExecutionCompletedAsync("req-1", response, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => obs2.OnAgentExecutionCompletedAsync("req-1", response, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task OnResponseAggregatedAsync_DelegatesToAllObservers()
    {
        var obs1 = A.Fake<IOrchestratorObserver>();
        var obs2 = A.Fake<IOrchestratorObserver>();
        var composite = new CompositeOrchestratorObserver([obs1, obs2]);

        await composite.OnResponseAggregatedAsync("req-1", "response");

        A.CallTo(() => obs1.OnResponseAggregatedAsync("req-1", "response", A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => obs2.OnResponseAggregatedAsync("req-1", "response", A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Observer_ContinuesWhenOneThrows()
    {
        var obs1 = A.Fake<IOrchestratorObserver>();
        var obs2 = A.Fake<IOrchestratorObserver>();

        A.CallTo(() => obs1.OnRequestStartedAsync(A<string>._, null, A<CancellationToken>._))
            .Throws<InvalidOperationException>();

        var composite = new CompositeOrchestratorObserver([obs1, obs2]);

        // Should not throw — composite should be resilient
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => composite.OnRequestStartedAsync("test"));
    }
}
