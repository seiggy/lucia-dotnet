using System.Diagnostics;
using System.Diagnostics.Metrics;

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
/// Tests that bail reasons are recorded as telemetry tags on the conversation activity.
/// Each bail path should emit a <c>fast_path_bail_reason</c> tag with a specific value:
/// <c>cache_miss</c>, <c>no_exact_match</c>, or <c>bail_signal_detected</c>.
/// </summary>
public sealed class ConversationTelemetryBailTests : IDisposable
{
    private readonly ICommandRouter _commandRouter = A.Fake<ICommandRouter>();
    private readonly IDirectSkillExecutor _skillExecutor = A.Fake<IDirectSkillExecutor>();
    private readonly IResponseTemplateRepository _templateRepo = A.Fake<IResponseTemplateRepository>();
    private readonly IServiceProvider _serviceProvider = A.Fake<IServiceProvider>();
    private readonly AgentHostTelemetrySource _telemetrySource = new();
    private readonly ConversationCommandProcessor _processor;

    public ConversationTelemetryBailTests()
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

    [Fact(Skip = "Pending: bail reason telemetry tags in ConversationCommandProcessor by Parker/Dallas")]
    public async Task CacheMiss_RecordsTelemetryTag()
    {
        // Arrange — entity cache not loaded; router bails
        using var activityListener = CreateActivityListener();

        A.CallTo(() => _commandRouter.RouteAsync(A<string>._, A<CancellationToken>._))
            .Returns(CommandRouteResult.NoMatch(TimeSpan.FromMilliseconds(1)));

        A.CallTo(() => _serviceProvider.GetService(A<Type>._)).Returns(null);

        // Act
        Activity? capturedActivity = null;
        activityListener.ActivityStopped = activity => capturedActivity = activity;

        await _processor.ProcessAsync(CreateRequest("turn on kitchen lights"));

        // Assert — activity should have the bail reason tag
        // NOTE: When implemented, the activity will have:
        //   SetTag("fast_path_bail_reason", "cache_miss")
        Assert.NotNull(capturedActivity);
        var bailTag = capturedActivity!.GetTagItem("fast_path_bail_reason");
        Assert.Equal("cache_miss", bailTag);
    }

    [Fact(Skip = "Pending: bail reason telemetry tags in ConversationCommandProcessor by Parker/Dallas")]
    public async Task NoExactMatch_RecordsTelemetryTag()
    {
        // Arrange — cache loaded but entity not found via exact match
        using var activityListener = CreateActivityListener();

        A.CallTo(() => _commandRouter.RouteAsync(A<string>._, A<CancellationToken>._))
            .Returns(CommandRouteResult.NoMatch(TimeSpan.FromMilliseconds(2)));

        A.CallTo(() => _serviceProvider.GetService(A<Type>._)).Returns(null);

        // Act
        Activity? capturedActivity = null;
        activityListener.ActivityStopped = activity => capturedActivity = activity;

        await _processor.ProcessAsync(
            CreateRequest("turn on the front room lights"));

        // Assert
        Assert.NotNull(capturedActivity);
        var bailTag = capturedActivity!.GetTagItem("fast_path_bail_reason");
        Assert.Equal("no_exact_match", bailTag);
    }

    [Fact(Skip = "Pending: bail reason telemetry tags in ConversationCommandProcessor by Parker/Dallas")]
    public async Task BailSignalDetected_RecordsTelemetryTag()
    {
        // Arrange — temporal/complex signal detected; router bails
        using var activityListener = CreateActivityListener();

        A.CallTo(() => _commandRouter.RouteAsync(A<string>._, A<CancellationToken>._))
            .Returns(CommandRouteResult.NoMatch(TimeSpan.FromMilliseconds(1)));

        A.CallTo(() => _serviceProvider.GetService(A<Type>._)).Returns(null);

        // Act
        Activity? capturedActivity = null;
        activityListener.ActivityStopped = activity => capturedActivity = activity;

        await _processor.ProcessAsync(
            CreateRequest("turn off the lights in 5 minutes"));

        // Assert
        Assert.NotNull(capturedActivity);
        var bailTag = capturedActivity!.GetTagItem("fast_path_bail_reason");
        Assert.Equal("bail_signal_detected", bailTag);
    }

    [Fact]
    public async Task SuccessfulCommand_IncrementsParsedCounter()
    {
        // Arrange — listen for the command_parsed counter scoped to THIS instance's meter.
        // Use Meter equality check to avoid cross-test interference from parallel execution.
        var meter = _telemetrySource.Meter;
        var commandParsedCount = 0L;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "conversation.command_parsed" && instrument.Meter == meter)
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "conversation.command_parsed" && instrument.Meter == meter)
                Interlocked.Add(ref commandParsedCount, measurement);
        });
        meterListener.Start();

        var pattern = CreateLightPattern();
        A.CallTo(() => _commandRouter.RouteAsync(A<string>._, A<CancellationToken>._))
            .Returns(new CommandRouteResult
            {
                IsMatch = true,
                Confidence = 0.95f,
                MatchedPattern = pattern,
                CapturedValues = new Dictionary<string, string>
                {
                    ["action"] = "on",
                    ["entity"] = "kitchen"
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
                    ["entity"] = "kitchen"
                }
            });

        A.CallTo(() => _templateRepo.GetBySkillAndActionAsync(
                A<string>._, A<string>._, A<CancellationToken>._))
            .Returns((ResponseTemplate?)null);

        // Act
        await _processor.ProcessAsync(CreateRequest("turn on the kitchen lights"));

        // Assert — command_parsed counter should be 1 (scoped to this test's meter)
        meterListener.RecordObservableInstruments();
        Assert.Equal(1, commandParsedCount);
    }

    [Fact]
    public async Task LlmFallback_DoesNotIncrementParsedCounter()
    {
        // Arrange — scope listener to this test's meter to avoid parallel test interference
        var meter = _telemetrySource.Meter;
        var commandParsedCount = 0L;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "conversation.command_parsed" && instrument.Meter == meter)
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "conversation.command_parsed" && instrument.Meter == meter)
                Interlocked.Add(ref commandParsedCount, measurement);
        });
        meterListener.Start();

        A.CallTo(() => _commandRouter.RouteAsync(A<string>._, A<CancellationToken>._))
            .Returns(CommandRouteResult.NoMatch(TimeSpan.FromMilliseconds(1)));
        A.CallTo(() => _serviceProvider.GetService(A<Type>._)).Returns(null);

        // Act
        await _processor.ProcessAsync(CreateRequest("set the lights to blue"));

        // Assert — no command parsed on LLM fallback
        meterListener.RecordObservableInstruments();
        Assert.Equal(0, commandParsedCount);
    }

    public void Dispose() => _telemetrySource.Dispose();

    private ActivityListener CreateActivityListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "lucia.AgentHost",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static ConversationRequest CreateRequest(string text) => new()
    {
        Text = text,
        Context = new ConversationContext
        {
            Timestamp = DateTimeOffset.UtcNow,
            ConversationId = "test-telemetry-bail",
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
