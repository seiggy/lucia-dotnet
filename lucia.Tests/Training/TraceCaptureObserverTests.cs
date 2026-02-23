using FakeItEasy;
using lucia.Agents.Orchestration.Models;
using lucia.Agents.Training;
using lucia.Agents.Training.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Training;

public sealed class TraceCaptureObserverTests
{
    private readonly ITraceRepository _repository;
    private readonly ILogger<TraceCaptureObserver> _logger;

    public TraceCaptureObserverTests()
    {
        _repository = A.Fake<ITraceRepository>();
        _logger = A.Fake<ILogger<TraceCaptureObserver>>();
    }

    private TraceCaptureObserver CreateObserver(TraceCaptureOptions? options = null)
    {
        var opts = Options.Create(options ?? new TraceCaptureOptions());
        return new TraceCaptureObserver(_repository, opts, _logger);
    }

    [Fact]
    public async Task OnRoutingCompletedAsync_WhenDisabled_DoesNothing()
    {
        var observer = CreateObserver(new TraceCaptureOptions { Enabled = false });

        var result = new AgentChoiceResult
        {
            AgentId = "agent-1",
            Reasoning = "test"
        };

        await observer.OnRoutingCompletedAsync(result);

        A.CallTo(() => _repository.InsertTraceAsync(A<ConversationTrace>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task OnRoutingCompletedAsync_CreatesTraceWithCorrectRoutingDecision()
    {
        var observer = CreateObserver();

        await observer.OnRequestStartedAsync("test request");

        var result = new AgentChoiceResult
        {
            AgentId = "home-assistant",
            Reasoning = "home intent detected",
            Confidence = 0.92,
            AdditionalAgents = ["music-agent"]
        };

        await observer.OnRoutingCompletedAsync(result);

        // Complete the lifecycle to persist the trace
        await observer.OnResponseAggregatedAsync("final response");

        // Allow fire-and-forget to complete
        await Task.Delay(200);

        A.CallTo(() => _repository.InsertTraceAsync(
            A<ConversationTrace>.That.Matches(t =>
                t.Routing != null &&
                t.Routing.SelectedAgentId == "home-assistant" &&
                t.Routing.Confidence == 0.92 &&
                t.Routing.Reasoning == "home intent detected" &&
                t.Routing.AdditionalAgentIds.Contains("music-agent")),
            A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task OnAgentExecutionCompletedAsync_AddsRecordWithCorrectAgentIdAndContent()
    {
        var observer = CreateObserver();

        await observer.OnRequestStartedAsync("test request");

        await observer.OnRoutingCompletedAsync(new AgentChoiceResult
        {
            AgentId = "agent-1",
            Reasoning = "test"
        });

        var response = new OrchestratorAgentResponse
        {
            AgentId = "agent-1",
            Content = "Lights are now on.",
            Success = true,
            ExecutionTimeMs = 50
        };

        await observer.OnAgentExecutionCompletedAsync(response);
        await observer.OnResponseAggregatedAsync("Lights are now on.");
        await Task.Delay(200);

        A.CallTo(() => _repository.InsertTraceAsync(
            A<ConversationTrace>.That.Matches(t =>
                t.AgentExecutions.Count == 2 &&
                t.AgentExecutions[1].AgentId == "agent-1" &&
                t.AgentExecutions[1].ResponseContent == "Lights are now on." &&
                t.AgentExecutions[1].Success),
            A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task OnAgentExecutionCompletedAsync_SetsIsErrored_WhenResponseNotSuccess()
    {
        var observer = CreateObserver();

        await observer.OnRequestStartedAsync("test request");

        await observer.OnRoutingCompletedAsync(new AgentChoiceResult
        {
            AgentId = "agent-1",
            Reasoning = "test"
        });

        var response = new OrchestratorAgentResponse
        {
            AgentId = "agent-1",
            Content = string.Empty,
            Success = false,
            ErrorMessage = "Agent timed out"
        };

        await observer.OnAgentExecutionCompletedAsync(response);
        await observer.OnResponseAggregatedAsync("Error occurred");
        await Task.Delay(200);

        A.CallTo(() => _repository.InsertTraceAsync(
            A<ConversationTrace>.That.Matches(t =>
                t.IsErrored &&
                t.ErrorMessage == "Agent timed out"),
            A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task OnResponseAggregatedAsync_CallsInsertTraceAsync()
    {
        var observer = CreateObserver();

        await observer.OnRequestStartedAsync("test request");

        await observer.OnRoutingCompletedAsync(new AgentChoiceResult
        {
            AgentId = "agent-1",
            Reasoning = "test"
        });

        await observer.OnResponseAggregatedAsync("aggregated result");
        await Task.Delay(200);

        A.CallTo(() => _repository.InsertTraceAsync(A<ConversationTrace>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task OnResponseAggregatedAsync_AppliesRedactionPatterns()
    {
        var options = new TraceCaptureOptions
        {
            RedactionPatterns = [@"secret-key-\d+"]
        };

        var observer = CreateObserver(options);

        await observer.OnRequestStartedAsync("test request");

        await observer.OnRoutingCompletedAsync(new AgentChoiceResult
        {
            AgentId = "agent-1",
            Reasoning = "test"
        });

        await observer.OnResponseAggregatedAsync("The key is secret-key-123 here");
        await Task.Delay(200);

        A.CallTo(() => _repository.InsertTraceAsync(
            A<ConversationTrace>.That.Matches(t =>
                t.FinalResponse != null &&
                t.FinalResponse.Contains("[REDACTED]") &&
                !t.FinalResponse.Contains("secret-key-123")),
            A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task OnAgentExecutionCompletedAsync_BeforeRouting_DoesNotThrow()
    {
        var observer = CreateObserver();

        var response = new OrchestratorAgentResponse
        {
            AgentId = "agent-1",
            Content = "content",
            Success = true
        };

        // Should not throw when no trace is in flight
        var exception = await Record.ExceptionAsync(() =>
            observer.OnAgentExecutionCompletedAsync(response));

        Assert.Null(exception);

        // No repository calls should have been made
        A.CallTo(() => _repository.InsertTraceAsync(A<ConversationTrace>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task OnRequestStartedAsync_CapturesConversationHistory()
    {
        var observer = CreateObserver();

        var history = new List<TracedMessage>
        {
            new() { Role = "user", Content = "turn on the lights" },
            new() { Role = "assistant", Content = "Done, the lights are on." },
            new() { Role = "user", Content = "now dim them to 50%" }
        };

        await observer.OnRequestStartedAsync("now dim them to 50%", history);

        await observer.OnRoutingCompletedAsync(new AgentChoiceResult
        {
            AgentId = "light-agent",
            Reasoning = "dimming request"
        });

        await observer.OnResponseAggregatedAsync("Dimmed to 50%.");
        await Task.Delay(200);

        A.CallTo(() => _repository.InsertTraceAsync(
            A<ConversationTrace>.That.Matches(t =>
                t.ConversationHistory.Count == 3 &&
                t.ConversationHistory[0].Role == "user" &&
                t.ConversationHistory[0].Content == "turn on the lights" &&
                t.ConversationHistory[2].Content == "now dim them to 50%"),
            A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task OnRoutingCompletedAsync_CapturesSystemPrompt()
    {
        var observer = CreateObserver();

        await observer.OnRequestStartedAsync("set fan to nature mode");

        var systemPrompt = "You are a routing agent. Available agents: climate-agent, light-agent.";

        await observer.OnRoutingCompletedAsync(
            new AgentChoiceResult
            {
                AgentId = "climate-agent",
                Reasoning = "fan mode request"
            },
            systemPrompt);

        await observer.OnResponseAggregatedAsync("Fan set to nature mode.");
        await Task.Delay(200);

        A.CallTo(() => _repository.InsertTraceAsync(
            A<ConversationTrace>.That.Matches(t =>
                t.SystemPrompt == systemPrompt &&
                t.Routing != null &&
                t.Routing.SelectedAgentId == "climate-agent"),
            A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task OnRequestStartedAsync_WithNullHistory_SetsEmptyList()
    {
        var observer = CreateObserver();

        await observer.OnRequestStartedAsync("single turn request");

        await observer.OnRoutingCompletedAsync(new AgentChoiceResult
        {
            AgentId = "agent-1",
            Reasoning = "test"
        });

        await observer.OnResponseAggregatedAsync("response");
        await Task.Delay(200);

        A.CallTo(() => _repository.InsertTraceAsync(
            A<ConversationTrace>.That.Matches(t =>
                t.ConversationHistory.Count == 0 &&
                t.SystemPrompt == null),
            A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }
}
