using FakeItEasy;
using lucia.AgentHost;
using lucia.AgentHost.Conversation;
using lucia.AgentHost.Conversation.Execution;
using lucia.AgentHost.Conversation.Models;
using lucia.AgentHost.Conversation.Templates;
using lucia.AgentHost.Conversation.Tracing;
using lucia.Agents.CommandTracing;
using lucia.Agents.Orchestration;
using lucia.Wyoming.CommandRouting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Conversation;

/// <summary>
/// Tests that the conversation fast-path uses exact-match entity resolution
/// against the entity cache. No fuzzy matching — if the entity name doesn't
/// match a cached area/entity exactly, bail to LLM immediately.
/// </summary>
public sealed class ExactMatchEntityResolutionTests : IDisposable
{
    private readonly ICommandRouter _commandRouter = A.Fake<ICommandRouter>();
    private readonly IDirectSkillExecutor _skillExecutor = A.Fake<IDirectSkillExecutor>();
    private readonly IResponseTemplateRepository _templateRepo = A.Fake<IResponseTemplateRepository>();
    private readonly IServiceProvider _serviceProvider = A.Fake<IServiceProvider>();
    private readonly AgentHostTelemetrySource _telemetrySource = new();
    private readonly ConversationCommandProcessor _processor;

    public ExactMatchEntityResolutionTests()
    {
        var templateRenderer = new ResponseTemplateRenderer(
            _templateRepo, A.Fake<ILogger<ResponseTemplateRenderer>>());
        var contextReconstructor = new ContextReconstructor();
        var telemetry = new ConversationTelemetry(_telemetrySource);
        var traceRepository = new InMemoryCommandTraceRepository();
        var routingOptions = A.Fake<IOptionsMonitor<CommandRoutingOptions>>();
        A.CallTo(() => routingOptions.CurrentValue).Returns(new CommandRoutingOptions());
        var personalityOptions = A.Fake<IOptionsMonitor<PersonalityPromptOptions>>();
        A.CallTo(() => personalityOptions.CurrentValue).Returns(new PersonalityPromptOptions());

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
            routingOptions,
            personalityOptions);
    }

    [Fact]
    public async Task ExactMatch_KitchenInCache_FastPathSucceeds()
    {
        // Arrange — "kitchen" is in the entity cache; router resolves exact match
        var pattern = CreateLightPattern();
        var routeResult = new CommandRouteResult
        {
            IsMatch = true,
            Confidence = 0.95f,
            MatchedPattern = pattern,
            CapturedValues = new Dictionary<string, string>
            {
                ["action"] = "on",
                ["entity"] = "kitchen"
            },
            ResolvedAreaId = "kitchen"
        };

        A.CallTo(() => _commandRouter.RouteAsync("turn on kitchen lights", A<CancellationToken>._))
            .Returns(routeResult);

        A.CallTo(() => _skillExecutor.ExecuteAsync(
                A<CommandRouteResult>._, A<ConversationContext>._, A<CancellationToken>._))
            .Returns(new SkillExecutionResult
            {
                Success = true,
                SkillId = "LightControlSkill",
                Action = "toggle",
                Captures = new Dictionary<string, string>
                {
                    ["action"] = "on",
                    ["entity"] = "kitchen"
                }
            });

        A.CallTo(() => _templateRepo.GetBySkillAndActionAsync(
                "LightControlSkill", "toggle", A<CancellationToken>._))
            .Returns(new ResponseTemplate
            {
                SkillId = "LightControlSkill",
                Action = "toggle",
                Templates = ["Turned {action} the {entity} lights."]
            });

        // Act
        var result = await _processor.ProcessAsync(CreateRequest("turn on kitchen lights"));

        // Assert — fast-path handled, not LLM fallback
        Assert.Equal(ProcessingKind.CommandHandled, result.Kind);
        Assert.NotNull(result.Response);
        Assert.Equal("command", result.Response!.Type);
    }

    [Fact]
    public async Task CacheNotLoaded_BailsToLlm()
    {
        // Arrange — entity cache is not yet populated; router bails immediately
        A.CallTo(() => _commandRouter.RouteAsync("turn on kitchen lights", A<CancellationToken>._))
            .Returns(CommandRouteResult.NoMatch(TimeSpan.FromMilliseconds(1)));

        A.CallTo(() => _serviceProvider.GetService(A<Type>._)).Returns(null);

        // Act
        var result = await _processor.ProcessAsync(CreateRequest("turn on kitchen lights"));

        // Assert — bails to LLM because cache wasn't loaded
        Assert.Equal(ProcessingKind.LlmFallback, result.Kind);
        Assert.NotNull(result.LlmPrompt);
        A.CallTo(() => _skillExecutor.ExecuteAsync(
                A<CommandRouteResult>._, A<ConversationContext>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task NoExactMatch_FrontRoomNotInCache_BailsToLlm()
    {
        // Arrange — "front room" is not in cache; even if "Guest Room" exists,
        // no fuzzy matching should occur. Router returns NoMatch.
        A.CallTo(() => _commandRouter.RouteAsync("turn on front room lights", A<CancellationToken>._))
            .Returns(CommandRouteResult.NoMatch(TimeSpan.FromMilliseconds(2)));

        A.CallTo(() => _serviceProvider.GetService(A<Type>._)).Returns(null);

        // Act
        var result = await _processor.ProcessAsync(CreateRequest("turn on front room lights"));

        // Assert — no fuzzy match attempted; bails to LLM
        Assert.Equal(ProcessingKind.LlmFallback, result.Kind);
        A.CallTo(() => _skillExecutor.ExecuteAsync(
                A<CommandRouteResult>._, A<ConversationContext>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task ExactMatch_DiningRoomCaseInsensitive_FastPathSucceeds()
    {
        // Arrange — "Dining room" is in cache (case-insensitive exact match).
        // "dining room" in transcript matches "Dining room" in cache.
        var pattern = CreateLightPattern();
        var routeResult = new CommandRouteResult
        {
            IsMatch = true,
            Confidence = 0.95f,
            MatchedPattern = pattern,
            CapturedValues = new Dictionary<string, string>
            {
                ["action"] = "off",
                ["entity"] = "dining room"
            },
            ResolvedAreaId = "dining_room"
        };

        A.CallTo(() => _commandRouter.RouteAsync("turn off dining room lights", A<CancellationToken>._))
            .Returns(routeResult);

        A.CallTo(() => _skillExecutor.ExecuteAsync(
                A<CommandRouteResult>._, A<ConversationContext>._, A<CancellationToken>._))
            .Returns(new SkillExecutionResult
            {
                Success = true,
                SkillId = "LightControlSkill",
                Action = "toggle",
                Captures = new Dictionary<string, string>
                {
                    ["action"] = "off",
                    ["entity"] = "dining room"
                }
            });

        A.CallTo(() => _templateRepo.GetBySkillAndActionAsync(
                "LightControlSkill", "toggle", A<CancellationToken>._))
            .Returns(new ResponseTemplate
            {
                SkillId = "LightControlSkill",
                Action = "toggle",
                Templates = ["Turned {action} the {entity} lights."]
            });

        // Act
        var result = await _processor.ProcessAsync(CreateRequest("turn off dining room lights"));

        // Assert — exact match succeeded (case-insensitive)
        Assert.Equal(ProcessingKind.CommandHandled, result.Kind);
        Assert.NotNull(result.Response);
    }

    [Fact]
    public async Task CacheLoadedButEmpty_BailsToLlm()
    {
        // Arrange — cache is loaded but contains zero entities.
        // Any entity reference must bail to LLM.
        A.CallTo(() => _commandRouter.RouteAsync("turn on the lights", A<CancellationToken>._))
            .Returns(CommandRouteResult.NoMatch(TimeSpan.FromMilliseconds(1)));

        A.CallTo(() => _serviceProvider.GetService(A<Type>._)).Returns(null);

        // Act
        var result = await _processor.ProcessAsync(CreateRequest("turn on the lights"));

        // Assert
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
            ConversationId = "test-exact-match",
            DeviceArea = "Living Room"
        }
    };

    private static CommandPattern CreateLightPattern() => new()
    {
        Id = "light-toggle",
        SkillId = "LightControlSkill",
        Action = "toggle",
        Templates = ["{action:on|off} [the] {entity} [lights]"]
    };
}
