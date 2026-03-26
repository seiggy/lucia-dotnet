using FakeItEasy;
using lucia.AgentHost;
using lucia.AgentHost.Conversation;
using lucia.AgentHost.Conversation.Execution;
using lucia.AgentHost.Conversation.Models;
using lucia.AgentHost.Conversation.Templates;
using lucia.AgentHost.Conversation.Tracing;
using lucia.Agents.CommandTracing;
using lucia.Wyoming.CommandRouting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Conversation;

/// <summary>
/// Tests that commands with temporal, color, or conjunction signals
/// bail to LLM immediately. These are "complex" commands that the
/// fast-path pattern matcher should not attempt to handle.
/// </summary>
public sealed class BailSignalDetectionTests : IDisposable
{
    private readonly ICommandRouter _commandRouter = A.Fake<ICommandRouter>();
    private readonly IDirectSkillExecutor _skillExecutor = A.Fake<IDirectSkillExecutor>();
    private readonly IResponseTemplateRepository _templateRepo = A.Fake<IResponseTemplateRepository>();
    private readonly IServiceProvider _serviceProvider = A.Fake<IServiceProvider>();
    private readonly AgentHostTelemetrySource _telemetrySource = new();
    private readonly ConversationCommandProcessor _processor;

    public BailSignalDetectionTests()
    {
        var templateRenderer = new ResponseTemplateRenderer(
            _templateRepo, A.Fake<ILogger<ResponseTemplateRenderer>>());
        var contextReconstructor = new ContextReconstructor();
        var telemetry = new ConversationTelemetry(_telemetrySource);
        var traceRepository = new InMemoryCommandTraceRepository();
        var routingOptions = A.Fake<IOptionsMonitor<CommandRoutingOptions>>();
        A.CallTo(() => routingOptions.CurrentValue).Returns(new CommandRoutingOptions());

        _processor = new ConversationCommandProcessor(
            _commandRouter,
            _skillExecutor,
            templateRenderer,
            contextReconstructor,
            telemetry,
            traceRepository,
            new CommandTraceChannel(),
            _serviceProvider,
            A.Fake<ILogger<ConversationCommandProcessor>>(),
            routingOptions);
    }

    [Fact(Skip = "Pending: bail signal detection in CommandRouter by Parker/Dallas")]
    public async Task TemporalSignal_InMinutes_BailsToLlm()
    {
        // Arrange — "in 5 minutes" is a temporal modifier; router should bail
        A.CallTo(() => _commandRouter.RouteAsync(
                "turn off the lights in 5 minutes", A<CancellationToken>._))
            .Returns(CommandRouteResult.NoMatch(TimeSpan.FromMilliseconds(1)));

        A.CallTo(() => _serviceProvider.GetService(A<Type>._)).Returns(null);

        // Act
        var result = await _processor.ProcessAsync(
            CreateRequest("turn off the lights in 5 minutes"));

        // Assert — temporal signal detected; bail to LLM
        Assert.Equal(ProcessingKind.LlmFallback, result.Kind);
        A.CallTo(() => _skillExecutor.ExecuteAsync(
                A<CommandRouteResult>._, A<ConversationContext>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact(Skip = "Pending: bail signal detection in CommandRouter by Parker/Dallas")]
    public async Task ColorSignal_SetToBlue_BailsToLlm()
    {
        // Arrange — "set the lights to blue" is a color command; too complex for fast-path
        A.CallTo(() => _commandRouter.RouteAsync(
                "set the lights to blue", A<CancellationToken>._))
            .Returns(CommandRouteResult.NoMatch(TimeSpan.FromMilliseconds(1)));

        A.CallTo(() => _serviceProvider.GetService(A<Type>._)).Returns(null);

        // Act
        var result = await _processor.ProcessAsync(
            CreateRequest("set the lights to blue"));

        // Assert — color signal detected; bail to LLM
        Assert.Equal(ProcessingKind.LlmFallback, result.Kind);
        A.CallTo(() => _skillExecutor.ExecuteAsync(
                A<CommandRouteResult>._, A<ConversationContext>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact(Skip = "Pending: bail signal detection in CommandRouter by Parker/Dallas")]
    public async Task ConjunctionSignal_TurnOnAndMakeWarmer_BailsToLlm()
    {
        // Arrange — "and" conjoins two separate intents; requires LLM decomposition
        A.CallTo(() => _commandRouter.RouteAsync(
                "turn on the lights and make it warmer", A<CancellationToken>._))
            .Returns(CommandRouteResult.NoMatch(TimeSpan.FromMilliseconds(1)));

        A.CallTo(() => _serviceProvider.GetService(A<Type>._)).Returns(null);

        // Act
        var result = await _processor.ProcessAsync(
            CreateRequest("turn on the lights and make it warmer"));

        // Assert — conjunction detected; bail to LLM for multi-intent handling
        Assert.Equal(ProcessingKind.LlmFallback, result.Kind);
        A.CallTo(() => _skillExecutor.ExecuteAsync(
                A<CommandRouteResult>._, A<ConversationContext>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact(Skip = "Pending: bail signal detection in CommandRouter by Parker/Dallas")]
    public async Task TemporalSignal_AtTime_BailsToLlm()
    {
        // Arrange — "at 10pm" is a scheduled action; requires LLM
        A.CallTo(() => _commandRouter.RouteAsync(
                "turn off the lights at 10pm", A<CancellationToken>._))
            .Returns(CommandRouteResult.NoMatch(TimeSpan.FromMilliseconds(1)));

        A.CallTo(() => _serviceProvider.GetService(A<Type>._)).Returns(null);

        // Act
        var result = await _processor.ProcessAsync(
            CreateRequest("turn off the lights at 10pm"));

        // Assert
        Assert.Equal(ProcessingKind.LlmFallback, result.Kind);
        A.CallTo(() => _skillExecutor.ExecuteAsync(
                A<CommandRouteResult>._, A<ConversationContext>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact(Skip = "Pending: bail signal detection in CommandRouter by Parker/Dallas")]
    public async Task ColorTemperatureSignal_WarmWhite_BailsToLlm()
    {
        // Arrange — "warm white" is a color temperature modifier; requires LLM
        A.CallTo(() => _commandRouter.RouteAsync(
                "make the bedroom lights warm white", A<CancellationToken>._))
            .Returns(CommandRouteResult.NoMatch(TimeSpan.FromMilliseconds(1)));

        A.CallTo(() => _serviceProvider.GetService(A<Type>._)).Returns(null);

        // Act
        var result = await _processor.ProcessAsync(
            CreateRequest("make the bedroom lights warm white"));

        // Assert — color temperature signal detected; bail to LLM
        Assert.Equal(ProcessingKind.LlmFallback, result.Kind);
        A.CallTo(() => _skillExecutor.ExecuteAsync(
                A<CommandRouteResult>._, A<ConversationContext>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    public void Dispose() => _telemetrySource.Dispose();

    private static ConversationRequest CreateRequest(string text) => new()
    {
        Text = text,
        Context = new ConversationContext
        {
            Timestamp = DateTimeOffset.UtcNow,
            ConversationId = "test-bail-signal",
            DeviceArea = "Living Room"
        }
    };
}
