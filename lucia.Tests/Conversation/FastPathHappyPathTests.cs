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
/// Happy-path tests that validate the fast-path still works end-to-end
/// when exact-match entity resolution succeeds and no bail signals are present.
/// </summary>
public sealed class FastPathHappyPathTests : IDisposable
{
    private readonly ICommandRouter _commandRouter = A.Fake<ICommandRouter>();
    private readonly IDirectSkillExecutor _skillExecutor = A.Fake<IDirectSkillExecutor>();
    private readonly IResponseTemplateRepository _templateRepo = A.Fake<IResponseTemplateRepository>();
    private readonly IServiceProvider _serviceProvider = A.Fake<IServiceProvider>();
    private readonly AgentHostTelemetrySource _telemetrySource = new();
    private readonly ConversationCommandProcessor _processor;

    public FastPathHappyPathTests()
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
    public async Task TurnOnKitchenLights_ExactMatch_ReturnsCannedResponse()
    {
        // Arrange — router matches "kitchen lights" with exact entity resolution
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
                Templates = ["Turned {action} the {entity}."]
            });

        // Act
        var result = await _processor.ProcessAsync(
            CreateRequest("turn on the kitchen lights"));

        // Assert — fast-path: exact match → execute → canned response
        Assert.Equal(ProcessingKind.CommandHandled, result.Kind);
        Assert.NotNull(result.Response);
        Assert.Equal("command", result.Response!.Type);
        Assert.Equal("Turned on the kitchen.", result.Response.Text);
        Assert.Equal("LightControlSkill", result.Response.Command!.SkillId);
        Assert.Equal("toggle", result.Response.Command.Action);
    }

    [Fact]
    public async Task TurnOffBedroomLamp_ExactMatch_Executes()
    {
        // Arrange
        var pattern = CreateLightPattern();
        var routeResult = new CommandRouteResult
        {
            IsMatch = true,
            Confidence = 0.90f,
            MatchedPattern = pattern,
            CapturedValues = new Dictionary<string, string>
            {
                ["action"] = "off",
                ["entity"] = "bedroom lamp"
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
                    ["action"] = "off",
                    ["entity"] = "bedroom lamp"
                }
            });

        A.CallTo(() => _templateRepo.GetBySkillAndActionAsync(
                A<string>._, A<string>._, A<CancellationToken>._))
            .Returns(new ResponseTemplate
            {
                SkillId = "LightControlSkill",
                Action = "toggle",
                Templates = ["Turned {action} the {entity}."]
            });

        // Act
        var result = await _processor.ProcessAsync(
            CreateRequest("turn off the bedroom lamp"));

        // Assert
        Assert.Equal(ProcessingKind.CommandHandled, result.Kind);
        A.CallTo(() => _skillExecutor.ExecuteAsync(
                A<CommandRouteResult>._, A<ConversationContext>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task SetThermostatTo72_ExactMatch_Executes()
    {
        // Arrange — thermostat entity resolved via exact-match cache
        var pattern = new CommandPattern
        {
            Id = "climate-set-temperature",
            SkillId = "ClimateControlSkill",
            Action = "set_temperature",
            Templates = ["set {entity} to {value}"]
        };

        var routeResult = new CommandRouteResult
        {
            IsMatch = true,
            Confidence = 0.95f,
            MatchedPattern = pattern,
            CapturedValues = new Dictionary<string, string>
            {
                ["entity"] = "thermostat",
                ["value"] = "72"
            },
            ResolvedEntityId = "climate.thermostat"
        };

        A.CallTo(() => _commandRouter.RouteAsync(A<string>._, A<CancellationToken>._))
            .Returns(routeResult);

        A.CallTo(() => _skillExecutor.ExecuteAsync(
                A<CommandRouteResult>._, A<ConversationContext>._, A<CancellationToken>._))
            .Returns(new SkillExecutionResult
            {
                Success = true,
                SkillId = "ClimateControlSkill",
                Action = "set_temperature",
                Captures = new Dictionary<string, string>
                {
                    ["entity"] = "thermostat",
                    ["value"] = "72"
                },
                ResponseText = "Thermostat set to 72°F"
            });

        A.CallTo(() => _templateRepo.GetBySkillAndActionAsync(
                "ClimateControlSkill", "set_temperature", A<CancellationToken>._))
            .Returns(new ResponseTemplate
            {
                SkillId = "ClimateControlSkill",
                Action = "set_temperature",
                Templates = ["Set {entity} to {value} degrees."]
            });

        // Act
        var result = await _processor.ProcessAsync(
            CreateRequest("set thermostat to 72"));

        // Assert
        Assert.Equal(ProcessingKind.CommandHandled, result.Kind);
        Assert.NotNull(result.Response);
        Assert.Equal("command", result.Response!.Type);
        Assert.Equal("ClimateControlSkill", result.Response.Command!.SkillId);
    }

    public void Dispose() => _telemetrySource.Dispose();

    private static ConversationRequest CreateRequest(string text) => new()
    {
        Text = text,
        Context = new ConversationContext
        {
            Timestamp = DateTimeOffset.UtcNow,
            ConversationId = "test-happy-path",
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
