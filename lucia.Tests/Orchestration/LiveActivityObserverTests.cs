using lucia.Agents.Orchestration;
using lucia.Agents.Orchestration.Models;
using Microsoft.Extensions.Logging;
using FakeItEasy;

namespace lucia.Tests.Orchestration;

public sealed class LiveActivityObserverTests
{
    private readonly LiveActivityChannel _channel;
    private readonly LiveActivityObserver _observer;

    public LiveActivityObserverTests()
    {
        _channel = new LiveActivityChannel();
        var logger = A.Fake<ILogger<LiveActivityObserver>>();
        _observer = new LiveActivityObserver(_channel, logger);
    }

    [Fact]
    public async Task OnRequestStartedAsync_EmitsRequestStartEvent()
    {
        await _observer.OnRequestStartedAsync("What is the weather?");

        var events = await DrainAsync(1);
        Assert.Single(events);
        Assert.Equal(LiveEvent.Types.RequestStart, events[0].Type);
        Assert.Equal("orchestrator", events[0].AgentName);
        Assert.Equal(LiveEvent.States.ProcessingPrompt, events[0].State);
    }

    [Fact]
    public async Task OnRequestStartedAsync_TruncatesLongMessages()
    {
        var longMessage = new string('x', 200);
        await _observer.OnRequestStartedAsync(longMessage);

        var events = await DrainAsync(1);
        Assert.Equal(101, events[0].Message!.Length); // 100 chars + ellipsis
    }

    [Fact]
    public async Task OnRoutingCompletedAsync_EmitsRoutingAndAgentStartEvents()
    {
        var result = new AgentChoiceResult
        {
            AgentId = "home-assistant",
            Confidence = 0.95,
            Reasoning = "Home domain detected",
        };

        await _observer.OnRoutingCompletedAsync(result);

        var events = await DrainAsync(2);
        Assert.Equal(LiveEvent.Types.Routing, events[0].Type);
        Assert.Equal("home-assistant", events[0].AgentName);
        Assert.Equal(0.95, events[0].Confidence);

        Assert.Equal(LiveEvent.Types.AgentStart, events[1].Type);
        Assert.Equal("home-assistant", events[1].AgentName);
    }

    [Fact]
    public async Task OnRoutingCompletedAsync_EmitsStartForAdditionalAgents()
    {
        var result = new AgentChoiceResult
        {
            AgentId = "primary",
            Reasoning = "Multi-domain request",
            AdditionalAgents = ["secondary", "tertiary"],
        };

        await _observer.OnRoutingCompletedAsync(result);

        var events = await DrainAsync(4);
        // routing + primary start + secondary start + tertiary start
        Assert.Equal(4, events.Count);
        Assert.Equal("secondary", events[2].AgentName);
        Assert.Equal("tertiary", events[3].AgentName);
    }

    [Fact]
    public async Task OnAgentExecutionCompletedAsync_EmitsCompleteOnSuccess()
    {
        var response = new OrchestratorAgentResponse
        {
            AgentId = "home-assistant",
            Content = "Done",
            Success = true,
            ExecutionTimeMs = 1500,
        };

        await _observer.OnAgentExecutionCompletedAsync(response);

        var events = await DrainAsync(1);
        Assert.Equal(LiveEvent.Types.AgentComplete, events[0].Type);
        Assert.Equal(LiveEvent.States.GeneratingResponse, events[0].State);
        Assert.Equal(1500, events[0].DurationMs);
    }

    [Fact]
    public async Task OnAgentExecutionCompletedAsync_EmitsErrorOnFailure()
    {
        var response = new OrchestratorAgentResponse
        {
            AgentId = "broken-agent",
            Content = "",
            Success = false,
            ErrorMessage = "Timeout",
        };

        await _observer.OnAgentExecutionCompletedAsync(response);

        var events = await DrainAsync(1);
        Assert.Equal(LiveEvent.Types.Error, events[0].Type);
        Assert.Equal(LiveEvent.States.Error, events[0].State);
        Assert.Equal("Timeout", events[0].ErrorMessage);
    }

    [Fact]
    public async Task OnResponseAggregatedAsync_EmitsRequestCompleteEvent()
    {
        await _observer.OnResponseAggregatedAsync("Done!");

        var events = await DrainAsync(1);
        Assert.Equal(LiveEvent.Types.RequestComplete, events[0].Type);
        Assert.Equal(LiveEvent.States.Idle, events[0].State);
    }

    private async Task<List<LiveEvent>> DrainAsync(int expected)
    {
        var events = new List<LiveEvent>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await foreach (var evt in _channel.ReadAllAsync(cts.Token))
        {
            events.Add(evt);
            if (events.Count >= expected) break;
        }
        return events;
    }
}
