using FakeItEasy;
using lucia.AgentHost;
using lucia.AgentHost.Conversation;
using lucia.AgentHost.Conversation.Execution;
using lucia.AgentHost.Conversation.Models;
using lucia.AgentHost.Conversation.Templates;
using lucia.AgentHost.Conversation.Tracing;
using lucia.Agents.Orchestration;
using lucia.Agents.Services;
using lucia.Data.InMemory;
using lucia.Tests.Wyoming;
using lucia.Wyoming.CommandRouting;
using lucia.Wyoming.Diarization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace lucia.Tests.Conversation;

/// <summary>
/// Multi-turn voice follow-ups must stay in the same conversation even when speaker
/// verification fails on a short turn ("The kitchen.", "Okay."). The last recognized
/// speaker is remembered per conversation and applied until it expires.
/// </summary>
public sealed class StickySpeakerTests : IDisposable
{
    private const string DanielProfileId = "p-daniel";
    private const string SarahProfileId = "p-sarah";

    private readonly FakeTimeProvider _clock = new();
    private readonly InMemoryMemoryStore _memories = new();
    private readonly ChatHistoryProvider _history;
    private readonly VoiceTurnStore _turns;
    private readonly StickySpeakerStore _sticky;
    private readonly ISpeakerProfileStore _profiles;
    private readonly ICommandRouter _router = A.Fake<ICommandRouter>();
    private readonly IServiceProvider _services = A.Fake<IServiceProvider>();
    private readonly AgentHostTelemetrySource _telemetry = new();
    private readonly ConversationCommandProcessor _processor;

    public StickySpeakerTests()
    {
        _history = new ChatHistoryProvider(_memories);
        _turns = new VoiceTurnStore(_clock);
        _sticky = new StickySpeakerStore(_clock);
        _profiles = A.Fake<ISpeakerProfileStore>();
        A.CallTo(() => _profiles.GetAsync(DanielProfileId, A<CancellationToken>._))
            .Returns(new SpeakerProfile { Id = DanielProfileId, Name = "Daniel" });
        A.CallTo(() => _profiles.GetAsync(SarahProfileId, A<CancellationToken>._))
            .Returns(new SpeakerProfile { Id = SarahProfileId, Name = "Sarah" });

        A.CallTo(() => _router.RouteAsync(A<string>._, A<CancellationToken>._))
            .Returns(CommandRouteResult.NoMatch(TimeSpan.Zero));
        A.CallTo(() => _services.GetService(A<Type>._)).Returns(null);

        var reconstructor = new ContextReconstructor(new UserContextProvider(_memories), _history);
        _processor = new ConversationCommandProcessor(
            _router,
            A.Fake<IDirectSkillExecutor>(),
            new ResponseTemplateRenderer(A.Fake<IResponseTemplateRepository>(), NullLogger<ResponseTemplateRenderer>.Instance),
            reconstructor,
            new ConversationTelemetry(_telemetry),
            new lucia.AgentHost.Conversation.Tracing.InMemoryCommandTraceRepository(),
            new CommandTraceChannel(),
            _services,
            NullLogger<ConversationCommandProcessor>.Instance,
            new OptionsMonitorStub<CommandRoutingOptions>(new CommandRoutingOptions()),
            new OptionsMonitorStub<PersonalityPromptOptions>(new PersonalityPromptOptions()),
            chatHistoryProvider: _history,
            voiceTurns: _turns,
            speakerProfiles: _profiles,
            stickySpeakers: _sticky);
    }

    public void Dispose() => _telemetry.Dispose();

    [Fact]
    public void StickyStore_RememberThenGet_ReturnsProfileForSameConversation()
    {
        _sticky.Remember("conv-1", DanielProfileId);
        Assert.Equal(DanielProfileId, _sticky.Get("conv-1"));
    }

    [Fact]
    public void StickyStore_Get_DifferentConversation_ReturnsNull()
    {
        _sticky.Remember("conv-1", DanielProfileId);
        Assert.Null(_sticky.Get("conv-2"));
    }

    [Fact]
    public void StickyStore_RememberAgain_UpdatesExistingEntry()
    {
        _sticky.Remember("conv-1", DanielProfileId);
        _sticky.Remember("conv-1", SarahProfileId);
        Assert.Equal(SarahProfileId, _sticky.Get("conv-1"));
    }

    [Fact]
    public void StickyStore_Get_AfterLifetime_ReturnsNull()
    {
        _sticky.Remember("conv-1", DanielProfileId);
        _clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Null(_sticky.Get("conv-1"));
    }

    [Fact]
    public async Task ProcessAsync_UnrecognizedVoiceFollowUp_UsesStickySpeakerContext()
    {
        // Arrange — prior recognized turns left history + a sticky speaker for this conversation
        await _history.AppendTurnAsync(DanielProfileId, "Set a timer for fifteen seconds.", "Okay, 15 seconds. Starting now.");
        _sticky.Remember("conv-a", DanielProfileId);

        var request = CreateVoiceRequest("conv-a", "The kitchen.", speaker: null);

        // Act
        var result = await _processor.ProcessAsync(request);

        // Assert — same session identity and chat history as the recognized turns, not anonymous
        Assert.Equal(ProcessingKind.LlmFallback, result.Kind);
        Assert.Equal($"voice:conv-a:{DanielProfileId}", result.EngineSessionId);
        Assert.Contains("RECENT CHAT HISTORY", result.LlmPrompt!);
        Assert.Contains("Okay, 15 seconds. Starting now.", result.LlmPrompt);
        Assert.Contains("[Speaker: Daniel]", result.LlmPrompt);
    }

    [Fact]
    public async Task ProcessAsync_RecognizedVoiceTurn_RemembersStickySpeaker()
    {
        // Arrange
        var request = CreateVoiceRequest("conv-b", "turn on the kitchen light",
            speaker: new SpeakerIdentification
            {
                ProfileId = SarahProfileId,
                Name = "Sarah",
                Similarity = 0.93f,
                IsAuthorized = true
            });

        // Act
        await _processor.ProcessAsync(request);

        // Assert
        Assert.Equal(SarahProfileId, _sticky.Get("conv-b"));
    }

    [Fact]
    public async Task ProcessAsync_RecognizedSpeaker_OverridesStaleStickySpeaker()
    {
        // Arrange — Daniel was last seen in this conversation, but Sarah is speaking now
        _sticky.Remember("conv-c", DanielProfileId);
        var request = CreateVoiceRequest("conv-c", "play some jazz",
            speaker: new SpeakerIdentification
            {
                ProfileId = SarahProfileId,
                Name = "Sarah",
                Similarity = 0.91f,
                IsAuthorized = true
            });

        // Act
        var result = await _processor.ProcessAsync(request);

        // Assert — fresh recognition wins and refreshes the sticky entry
        Assert.Equal($"voice:conv-c:{SarahProfileId}", result.EngineSessionId);
        Assert.Equal(SarahProfileId, _sticky.Get("conv-c"));
    }

    [Fact]
    public async Task ProcessAsync_UnrecognizedVoiceWithoutStickySpeaker_StaysAnonymous()
    {
        // Arrange — history exists for Daniel but this conversation never resolved a speaker
        await _history.AppendTurnAsync(DanielProfileId, "Set a timer for fifteen seconds.", "Okay, 15 seconds. Starting now.");

        var request = CreateVoiceRequest("conv-d", "The kitchen.", speaker: null);

        // Act
        var result = await _processor.ProcessAsync(request);

        // Assert — baseline behavior preserved: anonymous session, no cross-speaker history leak
        Assert.Equal($"voice:conv-d:anonymous", result.EngineSessionId);
        Assert.DoesNotContain("RECENT CHAT HISTORY", result.LlmPrompt!);
    }

    private ConversationRequest CreateVoiceRequest(
        string conversationId, string utterance, SpeakerIdentification? speaker)
    {
        var audio = new float[1600];
        var taggedText = _turns.Capture(utterance, audio, 1600, speaker);
        return new ConversationRequest
        {
            Text = taggedText,
            Context = new ConversationContext
            {
                Timestamp = DateTimeOffset.UtcNow,
                ConversationId = conversationId,
                DeviceId = "satellite-1",
                DeviceArea = "Kitchen",
                DeviceType = "Satellite1"
            }
        };
    }
}
