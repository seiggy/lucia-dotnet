using System.Diagnostics.Metrics;

using FakeItEasy;
using lucia.AgentHost;
using lucia.AgentHost.Conversation;
using lucia.AgentHost.Conversation.Execution;
using lucia.AgentHost.Conversation.Models;
using lucia.AgentHost.Conversation.Templates;
using lucia.Wyoming.CommandRouting;
using Microsoft.Extensions.Logging;

namespace lucia.Tests.Conversation;

public sealed class ConversationCommandProcessorTests : IDisposable
{
    private readonly ICommandRouter _commandRouter = A.Fake<ICommandRouter>();
    private readonly IDirectSkillExecutor _skillExecutor = A.Fake<IDirectSkillExecutor>();
    private readonly IResponseTemplateRepository _templateRepo = A.Fake<IResponseTemplateRepository>();
    private readonly IServiceProvider _serviceProvider = A.Fake<IServiceProvider>();
    private readonly AgentHostTelemetrySource _telemetrySource = new();
    private readonly ConversationCommandProcessor _processor;

    public ConversationCommandProcessorTests()
    {
        var templateRenderer = new ResponseTemplateRenderer(
            _templateRepo, A.Fake<ILogger<ResponseTemplateRenderer>>());
        var contextReconstructor = new ContextReconstructor();
        var telemetry = new ConversationTelemetry(_telemetrySource);

        _processor = new ConversationCommandProcessor(
            _commandRouter,
            _skillExecutor,
            templateRenderer,
            contextReconstructor,
            telemetry,
            _serviceProvider,
            A.Fake<ILogger<ConversationCommandProcessor>>());
    }

    [Fact]
    public async Task ProcessAsync_WithMatchedCommand_ReturnsCommandHandled()
    {
        // Arrange
        var pattern = CreatePattern("LightControlSkill", "toggle");
        var routeResult = new CommandRouteResult
        {
            IsMatch = true,
            Confidence = 0.95f,
            MatchedPattern = pattern,
            CapturedValues = new Dictionary<string, string>
            {
                ["action"] = "on",
                ["entity"] = "living room"
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
                    ["action"] = "on",
                    ["entity"] = "living room"
                },
                ResponseText = "Lights turned on"
            });

        A.CallTo(() => _templateRepo.GetBySkillAndActionAsync(
                "LightControlSkill", "toggle", A<CancellationToken>._))
            .Returns(new ResponseTemplate
            {
                SkillId = "LightControlSkill",
                Action = "toggle",
                Templates = ["Turned {action} the {entity}."]
            });

        var request = CreateRequest("turn on the living room lights");

        // Act
        var result = await _processor.ProcessAsync(request);

        // Assert
        Assert.Equal(ProcessingKind.CommandHandled, result.Kind);
        Assert.NotNull(result.Response);
        Assert.Equal("command", result.Response!.Type);
        Assert.Equal("Turned on the living room.", result.Response.Text);
        A.CallTo(() => _skillExecutor.ExecuteAsync(
                A<CommandRouteResult>._, A<ConversationContext>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessAsync_WithNoMatch_ReturnsLlmResult()
    {
        // Arrange
        A.CallTo(() => _commandRouter.RouteAsync(A<string>._, A<CancellationToken>._))
            .Returns(CommandRouteResult.NoMatch(TimeSpan.FromMilliseconds(5)));

        // LuciaEngine not available → LlmFallback
        A.CallTo(() => _serviceProvider.GetService(A<Type>._)).Returns(null);

        var request = CreateRequest("tell me a joke");

        // Act
        var result = await _processor.ProcessAsync(request);

        // Assert
        Assert.Equal(ProcessingKind.LlmFallback, result.Kind);
        Assert.NotNull(result.LlmPrompt);
        Assert.Contains("tell me a joke", result.LlmPrompt!);
    }

    [Fact]
    public async Task ProcessAsync_WithMatchButExecutionFailure_FallsBackToLlm()
    {
        // Arrange
        var pattern = CreatePattern("LightControlSkill", "toggle");

        A.CallTo(() => _commandRouter.RouteAsync(A<string>._, A<CancellationToken>._))
            .Returns(new CommandRouteResult
            {
                IsMatch = true,
                Confidence = 0.9f,
                MatchedPattern = pattern,
                CapturedValues = new Dictionary<string, string> { ["action"] = "on" }
            });

        A.CallTo(() => _skillExecutor.ExecuteAsync(
                A<CommandRouteResult>._, A<ConversationContext>._, A<CancellationToken>._))
            .Returns(SkillExecutionResult.Failed(
                "LightControlSkill", "toggle", "Service unavailable", TimeSpan.FromMilliseconds(10)));

        // LuciaEngine not available → LlmFallback
        A.CallTo(() => _serviceProvider.GetService(A<Type>._)).Returns(null);

        var request = CreateRequest("turn on the lights");

        // Act
        var result = await _processor.ProcessAsync(request);

        // Assert — falls back to LLM after skill failure
        Assert.Equal(ProcessingKind.LlmFallback, result.Kind);
        Assert.NotNull(result.LlmPrompt);
        Assert.Contains("turn on the lights", result.LlmPrompt!);
    }

    [Fact]
    public async Task ProcessAsync_RecordsTelemetry_ForCommandPath()
    {
        // Arrange — listen for the command_parsed counter
        var commandParsedCount = 0L;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "conversation.command_parsed")
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "conversation.command_parsed")
                Interlocked.Add(ref commandParsedCount, measurement);
        });
        meterListener.Start();

        var pattern = CreatePattern("LightControlSkill", "toggle");

        A.CallTo(() => _commandRouter.RouteAsync(A<string>._, A<CancellationToken>._))
            .Returns(new CommandRouteResult
            {
                IsMatch = true,
                Confidence = 0.95f,
                MatchedPattern = pattern,
                CapturedValues = new Dictionary<string, string>
                {
                    ["action"] = "on",
                    ["entity"] = "lights"
                }
            });

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
                    ["entity"] = "lights"
                }
            });

        A.CallTo(() => _templateRepo.GetBySkillAndActionAsync(
                A<string>._, A<string>._, A<CancellationToken>._))
            .Returns((ResponseTemplate?)null);

        // Act
        await _processor.ProcessAsync(CreateRequest("turn on the lights"));

        // Assert — command_parsed counter incremented
        meterListener.RecordObservableInstruments();
        Assert.Equal(1, commandParsedCount);
    }

    [Fact]
    public async Task ProcessAsync_RecordsTelemetry_ForLlmPath()
    {
        // Arrange — listen for both command and LLM counters
        var commandParsedCount = 0L;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name is "conversation.command_parsed" or "conversation.llm_fallback")
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "conversation.command_parsed")
                Interlocked.Add(ref commandParsedCount, measurement);
        });
        meterListener.Start();

        // No command match → LLM fallback
        A.CallTo(() => _commandRouter.RouteAsync(A<string>._, A<CancellationToken>._))
            .Returns(CommandRouteResult.NoMatch(TimeSpan.FromMilliseconds(1)));

        A.CallTo(() => _serviceProvider.GetService(A<Type>._)).Returns(null);

        // Act
        var result = await _processor.ProcessAsync(CreateRequest("tell me a story"));

        // Assert — LLM path taken; command counter NOT incremented
        Assert.Equal(ProcessingKind.LlmFallback, result.Kind);
        meterListener.RecordObservableInstruments();
        Assert.Equal(0, commandParsedCount);
    }

    public void Dispose()
    {
        _telemetrySource.Dispose();
    }

    private static ConversationRequest CreateRequest(string text) => new()
    {
        Text = text,
        Context = new ConversationContext
        {
            Timestamp = DateTimeOffset.UtcNow,
            ConversationId = "test-conv-id",
            DeviceArea = "Living Room"
        }
    };

    private static CommandPattern CreatePattern(string skillId, string action) => new()
    {
        Id = $"{skillId.ToLowerInvariant()}-{action}",
        SkillId = skillId,
        Action = action,
        Templates = [$"{{action}} the {{entity}}"]
    };
}
