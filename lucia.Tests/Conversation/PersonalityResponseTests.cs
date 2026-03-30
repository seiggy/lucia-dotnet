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
/// Tests for the optional personality-rendered response mode.
/// When personality mode is ON, fast-path results are passed through an LLM
/// personality prompt instead of returning canned template responses.
/// When OFF (default), the existing template rendering behavior applies.
/// </summary>
public sealed class PersonalityResponseTests : IDisposable
{
    private readonly ICommandRouter _commandRouter = A.Fake<ICommandRouter>();
    private readonly IDirectSkillExecutor _skillExecutor = A.Fake<IDirectSkillExecutor>();
    private readonly IResponseTemplateRepository _templateRepo = A.Fake<IResponseTemplateRepository>();
    private readonly IPersonalityResponseRenderer _personalityRenderer = A.Fake<IPersonalityResponseRenderer>();
    private readonly IServiceProvider _serviceProvider = A.Fake<IServiceProvider>();
    private readonly AgentHostTelemetrySource _telemetrySource = new();
    private readonly IOptionsMonitor<CommandRoutingOptions> _routingOptions = A.Fake<IOptionsMonitor<CommandRoutingOptions>>();
    private readonly IOptionsMonitor<PersonalityPromptOptions> _personalityOptions = A.Fake<IOptionsMonitor<PersonalityPromptOptions>>();

    private ConversationCommandProcessor CreateProcessor(
        bool usePersonality = false,
        string? personalityPrompt = null,
        IPersonalityResponseRenderer? renderer = null)
    {
        var routingOpts = new CommandRoutingOptions();
        A.CallTo(() => _routingOptions.CurrentValue).Returns(routingOpts);

        var personalityOpts = new PersonalityPromptOptions
        {
            UsePersonalityResponses = usePersonality,
            Instructions = personalityPrompt
        };
        A.CallTo(() => _personalityOptions.CurrentValue).Returns(personalityOpts);

        var templateRenderer = new ResponseTemplateRenderer(
            _templateRepo, A.Fake<ILogger<ResponseTemplateRenderer>>());

        return new ConversationCommandProcessor(
            _commandRouter,
            _skillExecutor,
            templateRenderer,
            new ContextReconstructor(),
            new ConversationTelemetry(_telemetrySource),
            new InMemoryCommandTraceRepository(),
            new CommandTraceChannel(),
            _serviceProvider,
            A.Fake<ILogger<ConversationCommandProcessor>>(),
            _routingOptions,
            _personalityOptions,
            renderer);
    }

    [Fact]
    public async Task PersonalityModeOff_ReturnsCannedTemplateResponse()
    {
        // Arrange — personality mode is disabled (default behavior).
        var processor = CreateProcessor(usePersonality: false);
        var pattern = CreateLightPattern();
        ArrangeSuccessfulFastPath(pattern, "on", "kitchen");

        A.CallTo(() => _templateRepo.GetBySkillAndActionAsync(
                "LightControlSkill", "toggle", A<CancellationToken>._))
            .Returns(new ResponseTemplate
            {
                SkillId = "LightControlSkill",
                Action = "toggle",
                Templates = ["Turned {action} the {entity}."]
            });

        // Act
        var result = await processor.ProcessAsync(
            CreateRequest("turn on the kitchen lights"));

        // Assert — canned template response, personality renderer never called
        Assert.Equal(ProcessingKind.CommandHandled, result.Kind);
        Assert.Equal("Turned on the kitchen.", result.Response!.Text);
        A.CallTo(() => _personalityRenderer.RenderAsync(
                A<string>._, A<string>._, A<string>._, A<IReadOnlyDictionary<string, string>>._, A<ConversationContext?>._, A<string?>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task PersonalityModeOn_ReturnsLlmStyledResponse()
    {
        // Arrange — personality mode enabled with configured prompt and renderer.
        var processor = CreateProcessor(
            usePersonality: true,
            personalityPrompt: "You are a cheerful assistant named Lucia.",
            renderer: _personalityRenderer);

        var pattern = CreateLightPattern();
        ArrangeSuccessfulFastPath(pattern, "on", "kitchen");

        A.CallTo(() => _templateRepo.GetBySkillAndActionAsync(
                "LightControlSkill", "toggle", A<CancellationToken>._))
            .Returns(new ResponseTemplate
            {
                SkillId = "LightControlSkill",
                Action = "toggle",
                Templates = ["Turned {action} the {entity}."]
            });

        // Personality renderer rewrites the canned response
        A.CallTo(() => _personalityRenderer.RenderAsync(
                "LightControlSkill", "toggle",
                "Turned on the kitchen.",
                A<IReadOnlyDictionary<string, string>>._,
                A<ConversationContext?>._,
                A<string?>._,
                A<CancellationToken>._))
            .Returns("Sure thing! I've turned on the kitchen lights for you!");

        // Act
        var result = await processor.ProcessAsync(
            CreateRequest("turn on the kitchen lights"));

        // Assert — personality-styled response returned
        Assert.Equal(ProcessingKind.CommandHandled, result.Kind);
        Assert.Equal("Sure thing! I've turned on the kitchen lights for you!", result.Response!.Text);
    }

    [Fact]
    public async Task PersonalityModeOn_LlmFails_FallsBackToCannedResponse()
    {
        // Arrange — personality mode enabled but renderer returns original on failure
        // (IPersonalityResponseRenderer contract: fall back to cannedResponse on error)
        var processor = CreateProcessor(
            usePersonality: true,
            personalityPrompt: "You are Lucia.",
            renderer: _personalityRenderer);

        var pattern = CreateLightPattern();
        ArrangeSuccessfulFastPath(pattern, "off", "bedroom");

        A.CallTo(() => _templateRepo.GetBySkillAndActionAsync(
                "LightControlSkill", "toggle", A<CancellationToken>._))
            .Returns(new ResponseTemplate
            {
                SkillId = "LightControlSkill",
                Action = "toggle",
                Templates = ["Turned {action} the {entity}."]
            });

        // Renderer falls back to original canned text on LLM failure
        A.CallTo(() => _personalityRenderer.RenderAsync(
                A<string>._, A<string>._, A<string>._, A<IReadOnlyDictionary<string, string>>._, A<ConversationContext?>._, A<string?>._, A<CancellationToken>._))
            .Returns("Turned off the bedroom.");

        // Act
        var result = await processor.ProcessAsync(
            CreateRequest("turn off the bedroom lights"));

        // Assert — canned response returned (renderer fell back)
        Assert.Equal(ProcessingKind.CommandHandled, result.Kind);
        Assert.Equal("Turned off the bedroom.", result.Response!.Text);
    }

    [Fact]
    public async Task PersonalityPromptNotConfigured_NoRendererInjected_UsesCannedResponse()
    {
        // Arrange — personality mode ON but no renderer injected (null).
        // Processor checks: opts.UsePersonalityResponses && _personalityRenderer is not null
        var processor = CreateProcessor(
            usePersonality: true,
            personalityPrompt: null,
            renderer: null);

        var pattern = CreateLightPattern();
        ArrangeSuccessfulFastPath(pattern, "on", "living room");

        A.CallTo(() => _templateRepo.GetBySkillAndActionAsync(
                "LightControlSkill", "toggle", A<CancellationToken>._))
            .Returns(new ResponseTemplate
            {
                SkillId = "LightControlSkill",
                Action = "toggle",
                Templates = ["Turned {action} the {entity}."]
            });

        // Act
        var result = await processor.ProcessAsync(
            CreateRequest("turn on the living room lights"));

        // Assert — no renderer → canned template used
        Assert.Equal(ProcessingKind.CommandHandled, result.Kind);
        Assert.Equal("Turned on the living room.", result.Response!.Text);
    }

    public void Dispose() => _telemetrySource.Dispose();

    private void ArrangeSuccessfulFastPath(CommandPattern pattern, string action, string entity)
    {
        var routeResult = new CommandRouteResult
        {
            IsMatch = true,
            Confidence = 0.95f,
            MatchedPattern = pattern,
            CapturedValues = new Dictionary<string, string>
            {
                ["action"] = action,
                ["entity"] = entity
            }
        };

        A.CallTo(() => _commandRouter.RouteAsync(A<string>._, A<CancellationToken>._))
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
                    ["action"] = action,
                    ["entity"] = entity
                }
            });
    }

    private static ConversationRequest CreateRequest(string text) => new()
    {
        Text = text,
        Context = new ConversationContext
        {
            Timestamp = DateTimeOffset.UtcNow,
            ConversationId = "test-personality",
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
