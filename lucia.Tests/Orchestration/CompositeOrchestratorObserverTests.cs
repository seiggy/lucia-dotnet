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
        var composite = new CompositeOrchestratorObserver([obs1, obs2]);

        await composite.OnRequestStartedAsync("test");

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
        await composite.OnRoutingCompletedAsync(result);

        A.CallTo(() => obs1.OnRoutingCompletedAsync(result, null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => obs2.OnRoutingCompletedAsync(result, null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task OnAgentExecutionCompletedAsync_DelegatesToAllObservers()
    {
        var obs1 = A.Fake<IOrchestratorObserver>();
        var obs2 = A.Fake<IOrchestratorObserver>();
        var composite = new CompositeOrchestratorObserver([obs1, obs2]);

        var response = new OrchestratorAgentResponse { AgentId = "a", Content = "ok", Success = true };
        await composite.OnAgentExecutionCompletedAsync(response);

        A.CallTo(() => obs1.OnAgentExecutionCompletedAsync(response, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => obs2.OnAgentExecutionCompletedAsync(response, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task OnResponseAggregatedAsync_DelegatesToAllObservers()
    {
        var obs1 = A.Fake<IOrchestratorObserver>();
        var obs2 = A.Fake<IOrchestratorObserver>();
        var composite = new CompositeOrchestratorObserver([obs1, obs2]);

        await composite.OnResponseAggregatedAsync("response");

        A.CallTo(() => obs1.OnResponseAggregatedAsync("response", A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => obs2.OnResponseAggregatedAsync("response", A<CancellationToken>._))
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
