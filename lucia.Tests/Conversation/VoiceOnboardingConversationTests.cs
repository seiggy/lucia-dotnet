using System.Text.Json;
using FakeItEasy;
using lucia.AgentHost;
using lucia.AgentHost.Conversation;
using lucia.AgentHost.Conversation.Execution;
using lucia.AgentHost.Conversation.Models;
using lucia.AgentHost.Conversation.Templates;
using lucia.AgentHost.Conversation.Tracing;
using lucia.Agents.CommandTracing;
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

public sealed class VoiceOnboardingConversationTests : IDisposable
{
    private readonly string _clipPath = Path.Combine(Path.GetTempPath(), "lucia-voice-onboarding", Guid.NewGuid().ToString("N"));
    private readonly InMemorySpeakerProfileStore _profiles = new();
    private readonly InMemoryMemoryStore _memories = new();
    private readonly lucia.AgentHost.Conversation.Tracing.InMemoryCommandTraceRepository _traces = new();
    private readonly FakeTimeProvider _clock = new();
    private readonly VoiceTurnStore _turns;
    private readonly float[] _embedding = Enumerable.Repeat(0.1f, 128).ToArray();
    private readonly ICommandRouter _router = A.Fake<ICommandRouter>();
    private readonly AgentHostTelemetrySource _telemetry = new();
    private readonly VoiceOnboardingService _enrollment;
    private readonly TestDiarizationEngine _diarization;
    private readonly VoiceOnboardingWorkflow _workflow;
    private readonly ConversationCommandProcessor _processor;
    private string _conversationId = "conversation-1";

    public VoiceOnboardingConversationTests()
    {
        _turns = new VoiceTurnStore(_clock);
        A.CallTo(() => _router.RouteAsync(A<string>._, A<CancellationToken>._))
            .Returns(CommandRouteResult.NoMatch(TimeSpan.Zero));
        var options = new VoiceProfileOptions { AudioClipBasePath = _clipPath, OnboardingSampleCount = 3 };
        _diarization = new TestDiarizationEngine(embeddingVector: _embedding);
        _enrollment = new VoiceOnboardingService(
            _diarization, _profiles, new AudioQualityAnalyzer(Options.Create(options)),
            Options.Create(options), NullLogger<VoiceOnboardingService>.Instance);
        _workflow = new VoiceOnboardingWorkflow(
            _enrollment, new TestDiarizationEngine(), _memories,
            _clock, NullLogger<VoiceOnboardingWorkflow>.Instance);
        _processor = new ConversationCommandProcessor(
            _router,
            A.Fake<IDirectSkillExecutor>(),
            new ResponseTemplateRenderer(A.Fake<IResponseTemplateRepository>(), NullLogger<ResponseTemplateRenderer>.Instance),
            new ContextReconstructor(new UserContextProvider(_memories)),
            new ConversationTelemetry(_telemetry),
            _traces,
            new CommandTraceChannel(),
            A.Fake<IServiceProvider>(),
            NullLogger<ConversationCommandProcessor>.Instance,
            new OptionsMonitorStub<CommandRoutingOptions>(new CommandRoutingOptions()),
            new OptionsMonitorStub<PersonalityPromptOptions>(new PersonalityPromptOptions()),
            voiceTurns: _turns,
            speakerProfiles: _profiles,
            onboarding: _workflow);
    }

    [Theory]
    [InlineData("enroll my voice")]
    [InlineData("I want to enroll my voice.")]
    [InlineData("Enroll my voice profile")]
    [InlineData("Learn my voice")]
    [InlineData("Hey Lucia, enroll my voice.")]
    [InlineData("Lucia, learn my voice")]
    public async Task EnrollmentRequests_StartTheWorkflowWithoutLlmRouting(string text)
    {
        var response = await SayAsync(text);

        Assert.Equal("onboarding", response.Type);
        Assert.Contains("permission", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.NeedsInput);
        A.CallTo(() => _router.RouteAsync(A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Theory]
    [InlineData("Skip this question")]
    [InlineData("No preference")]
    [InlineData("Leave it blank")]
    [InlineData("Skip. Skip. Skip.")]
    [InlineData("No")]
    [InlineData("No thanks")]
    [InlineData("I'd rather not say")]
    public async Task Birthday_CanBeSkippedWithoutAddingAnotherQuestion(string answer)
    {
        await SayAsync("learn my voice");
        await SayAsync("yes");
        var birthday = await SayAsync("Alice");
        var confirmation = await SayAsync(answer);

        Assert.Contains("birthday", birthday.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("year", birthday.Text);
        Assert.Contains("I'll call you Alice", confirmation.Text);
        Assert.Contains("Is that correct?", confirmation.Text);
        Assert.DoesNotContain("birthday is", confirmation.Text);
        Assert.DoesNotContain("preferred room", confirmation.Text);
        Assert.DoesNotContain("preferences are", confirmation.Text);

        var response = await SayAsync("yes");
        for (var index = 0; index < 3; index++)
        {
            response = await SayAsync(GetPhrase(response));
        }
        Assert.False(response.NeedsInput);
        Assert.DoesNotContain("birthday", response.Text, StringComparison.OrdinalIgnoreCase);
        var profile = Assert.Single(await _profiles.GetEnrolledProfilesAsync(CancellationToken.None));
        Assert.Equal("preferred_name", Assert.Single(await _memories.GetAllAsync(profile.Id)).Key);
    }

    [Theory]
    [InlineData("conversation-1")]
    [InlineData("different-conversation")]
    [InlineData("voice-onboarding:client-generated-marker")]
    public async Task FollowupFromSameSatellite_ResumesWhenClientConversationIdChanges(string clientConversationId)
    {
        var started = await SayAsync("onboard me");
        _conversationId = clientConversationId;

        var followup = await SayAsync("yes");

        Assert.Equal(started.ConversationId, followup.ConversationId);
        Assert.Contains("name", followup.Text, StringComparison.OrdinalIgnoreCase);
        Assert.True(followup.NeedsInput);
        A.CallTo(() => _router.RouteAsync(A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task FollowupFromAnotherSatellite_DoesNotAdvanceTheActiveEnrollment()
    {
        await SayAsync("onboard me");
        _conversationId = "another-conversation";

        var other = await ProcessAsync("yes", deviceId: "another-satellite");
        Assert.NotNull(other.LlmPrompt);

        var followup = await SayAsync("yes");
        Assert.Contains("name", followup.Text, StringComparison.OrdinalIgnoreCase);
        A.CallTo(() => _router.RouteAsync("yes", A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task RestartRequestFromSameSatellite_RepeatsInsteadOfCreatingAnotherEnrollment()
    {
        var started = await SayAsync("onboard me");
        await SayAsync("yes");
        _conversationId = "new-client-conversation";

        var repeated = await SayAsync("onboard me");

        Assert.Equal(started.ConversationId, repeated.ConversationId);
        Assert.Contains("name", repeated.Text, StringComparison.OrdinalIgnoreCase);
        A.CallTo(() => _router.RouteAsync(A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task OnboardingTurns_AreTracedWithoutVoiceTokensOrPersonalAnswers()
    {
        await SayAsync("onboard me");
        _conversationId = "followup-client-id";
        await SayAsync("yes");
        await SayAsync("My name is Private Person");
        await SayAsync("March 14, 1990");

        var traces = (await _traces.ListAsync(new CommandTraceFilter())).Items;

        Assert.Equal(4, traces.Count);
        Assert.All(traces, trace =>
        {
            Assert.Equal(CommandTraceOutcome.CommandHandled, trace.Outcome);
            Assert.NotNull(trace.Workflow);
            Assert.Equal("voice-onboarding", trace.Workflow.Name);
            Assert.Null(trace.LlmFallback);
            Assert.Null(trace.Execution);
            Assert.DoesNotContain("lucia-voice", trace.RawText);
            Assert.DoesNotContain("Private Person", trace.RawText);
            Assert.DoesNotContain("Private Person", trace.CleanText);
            Assert.DoesNotContain("Private Person", trace.ResponseText ?? string.Empty);
            Assert.DoesNotContain("March 14, 1990", trace.RawText);
            Assert.DoesNotContain("March 14, 1990", trace.CleanText);
            Assert.DoesNotContain("March 14, 1990", trace.ResponseText ?? string.Empty);
            var restored = Assert.IsType<CommandTrace>(
                JsonSerializer.Deserialize<CommandTrace>(JsonSerializer.Serialize(trace)));
            Assert.Equal(trace.Workflow, restored.Workflow);
        });
        var consent = Assert.Single(traces, trace => trace.Workflow!.Stage == "Consent");
        var name = Assert.Single(traces, trace => trace.Workflow!.Stage == "Name");
        Assert.Equal("followup-client-id", name.RequestContext.ConversationId);
        Assert.Equal(consent.Workflow!.ConversationId, name.Workflow!.ConversationId);
        Assert.True(name.Workflow.NeedsInput);
        Assert.Single(traces, trace => trace.Workflow!.Stage == "Birthday");
    }

    [Fact]
    public async Task ChangedConversationId_DoesNotAllowTextOnlyConsent()
    {
        await SayAsync("onboard me");
        var request = CreateRequest("yes") with
        {
            Context = new ConversationContext { ConversationId = "new-client-id", DeviceId = "satellite-1" },
        };

        var result = await _processor.ProcessAsync(request);

        Assert.Contains("live voice samples", result.Response!.Text);
        var followup = await SayAsync("yes");
        Assert.Contains("name", followup.Text, StringComparison.OrdinalIgnoreCase);
        A.CallTo(() => _router.RouteAsync(A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ChangedConversationId_ExpiredEnrollmentReturnsAnExpiryNoticeBeforeNormalRouting()
    {
        await SayAsync("onboard me");
        _conversationId = "new-client-id";
        _clock.Advance(TimeSpan.FromMinutes(11));

        var expired = await SayAsync("yes");

        Assert.Contains("expired", expired.Text, StringComparison.OrdinalIgnoreCase);
        Assert.False(expired.NeedsInput);
        A.CallTo(() => _router.RouteAsync(A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
        Assert.NotNull((await ProcessAsync("what time is it")).LlmPrompt);
    }

    [Fact]
    public async Task ChangedConversationId_CanCancelWithoutRoutingTheAnswer()
    {
        await SayAsync("onboard me");
        _conversationId = "new-client-id";

        var cancelled = await SayAsync("cancel");

        Assert.Contains("cancelled", cancelled.Text, StringComparison.OrdinalIgnoreCase);
        Assert.False(cancelled.NeedsInput);
        Assert.Empty(await _profiles.GetAllAsync(CancellationToken.None));
        A.CallTo(() => _router.RouteAsync(A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
        Assert.NotNull((await ProcessAsync("what time is it")).LlmPrompt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SatelliteTurns_PromoteOnlyTheMatchingProvisionalProfile(bool matches)
    {
        const string ProvisionalId = "unknown-existing";
        await _profiles.CreateAsync(new SpeakerProfile
        {
            Id = ProvisionalId,
            Name = "Unknown Speaker 1",
            IsProvisional = true,
            IsAuthorized = false,
            AverageEmbedding = matches ? [.. _embedding] : _embedding.Select(value => -value).ToArray()
        }, CancellationToken.None);
        _diarization.Identification = matches
            ? new SpeakerIdentification { ProfileId = ProvisionalId, Name = "Unknown Speaker 1", Similarity = 1, IsAuthorized = false }
            : null;

        await SayAsync("onboard me");
        await SayAsync("yes");
        await SayAsync("Alice");
        await SayAsync("skip");
        var response = await SayAsync("yes");
        for (var index = 0; index < 3; index++)
        {
            response = await SayAsync(GetPhrase(response));
        }

        Assert.False(response.NeedsInput);
        var profile = Assert.Single(await _profiles.GetEnrolledProfilesAsync(CancellationToken.None));
        Assert.Equal("Alice", profile.Name);
        Assert.Equal("Alice", await _memories.RetrieveAsync(profile.Id, "preferred_name"));
        if (matches)
        {
            Assert.Equal(ProvisionalId, profile.Id);
            Assert.Empty(await _profiles.GetProvisionalProfilesAsync(CancellationToken.None));
        }
        else
        {
            Assert.NotEqual(ProvisionalId, profile.Id);
            Assert.Equal(ProvisionalId, Assert.Single(await _profiles.GetProvisionalProfilesAsync(CancellationToken.None)).Id);
        }
        A.CallTo(() => _router.RouteAsync(A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task SatelliteTurns_EnrollVoiceAndSaveConfirmedFacts_WithoutExecutingSampleCommands()
    {
        var response = await SayAsync("Onboard me");
        Assert.True(response.NeedsInput);
        Assert.Contains("permission", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _profiles.GetEnrolledProfilesAsync(CancellationToken.None));

        response = await SayAsync("yes");
        Assert.Contains("name", response.Text, StringComparison.OrdinalIgnoreCase);
        response = await SayAsync("My name is Alice");
        Assert.Contains("birthday", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("year", response.Text);
        Assert.DoesNotContain("room", response.Text);
        response = await SayAsync("March 14, 1990");
        Assert.Contains("Alice", response.Text);
        Assert.Contains("March 14, 1990", response.Text);
        Assert.Contains("Is that correct?", response.Text);
        Assert.Empty(await _profiles.GetEnrolledProfilesAsync(CancellationToken.None));

        response = await SayAsync("yes");
        for (var index = 0; index < 3; index++)
        {
            Assert.True(response.NeedsInput);
            response = await SayAsync(GetPhrase(response));
        }

        Assert.False(response.NeedsInput);
        Assert.Equal("conversation-1", response.ConversationId);
        var profile = Assert.Single(await _profiles.GetEnrolledProfilesAsync(CancellationToken.None));
        Assert.Equal("Alice", profile.Name);
        Assert.Equal(3, profile.Embeddings.Length);
        Assert.Equal("Alice", await _memories.RetrieveAsync(profile.Id, "preferred_name"));
        Assert.Equal("March 14, 1990", await _memories.RetrieveAsync(profile.Id, "birthday"));
        Assert.Equal(2, (await _memories.GetAllAsync(profile.Id)).Count);
        Assert.Null(await _memories.RetrieveAsync(profile.Id, "preferred_room"));
        Assert.Null(await _memories.RetrieveAsync(profile.Id, "preferences"));
        Assert.Contains("name and birthday", response.Text);
        Assert.Contains("birthday: March 14, 1990",
            await new UserContextProvider(_memories).GetUserContextAsync(profile.Id, CancellationToken.None));
        Assert.Empty(await _memories.GetAllAsync("ha-service-account"));
        A.CallTo(() => _router.RouteAsync(A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task RejectedConfirmation_DiscardsTheBirthdayBeforeRestartingQuestions()
    {
        await SayAsync("learn my voice");
        await SayAsync("yes");
        await SayAsync("Alice");
        await SayAsync("March 14, 1990");

        var restart = await SayAsync("no");

        Assert.Contains("name", restart.Text, StringComparison.OrdinalIgnoreCase);
        await SayAsync("Alex");
        var confirmation = await SayAsync("skip this question");
        Assert.DoesNotContain("March 14", confirmation.Text);
        var response = await SayAsync("yes");
        for (var index = 0; index < 3; index++)
        {
            response = await SayAsync(GetPhrase(response));
        }
        var profile = Assert.Single(await _profiles.GetEnrolledProfilesAsync(CancellationToken.None));
        var memory = Assert.Single(await _memories.GetAllAsync(profile.Id));
        Assert.Equal("preferred_name", memory.Key);
        Assert.Equal("Alex", memory.Value);
    }

    [Fact]
    public async Task Onboarding_RejectsWrongPhraseQuietAudioAndMixedSpeakers_AndCancelsStagedSamples()
    {
        await SayAsync("Onboard me");
        await SayAsync("yes");
        await SayAsync("Alice");
        await SayAsync("skip");
        var prompt = await SayAsync("yes");

        var wrongDevice = await ProcessAsync("turn off all the lights", deviceId: "another-satellite");
        Assert.Contains("where you started", wrongDevice.Response!.Text);
        var wrongPhrase = await SayAsync("unlock the front door");
        Assert.Contains("didn't match", wrongPhrase.Text);
        var quiet = await ProcessAsync(GetPhrase(prompt), audio: new float[32_000]);
        Assert.Contains("quiet", quiet.Response!.Text);

        var next = await SayAsync(GetPhrase(prompt));
        Array.Fill(_embedding, -0.1f);
        var mixed = await SayAsync(GetPhrase(next));
        Assert.Contains("same person", mixed.Text, StringComparison.OrdinalIgnoreCase);

        var cancelled = await SayAsync("cancel");
        Assert.False(cancelled.NeedsInput);
        Assert.Empty(await _profiles.GetEnrolledProfilesAsync(CancellationToken.None));
        Assert.Empty(Directory.GetFiles(_clipPath, "*.wav", SearchOption.AllDirectories));
        A.CallTo(() => _router.RouteAsync(A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Theory]
    [InlineData("no")]
    [InlineData("cancel")]
    public async Task Consent_CanBeDeclinedWithoutEnrollment(string answer)
    {
        await SayAsync("onboard me");
        var ambiguous = await SayAsync("maybe");
        Assert.Contains("permission", ambiguous.Text);
        var result = await SayAsync(answer);
        Assert.False(result.NeedsInput);
        Assert.Empty(await _profiles.GetAllAsync(CancellationToken.None));
        Assert.False(Directory.Exists(_clipPath));
    }

    [Fact]
    public async Task IdleConversation_ExpiresInsteadOfExecutingTheNextSample()
    {
        await SayAsync("onboard me");
        await SayAsync("yes");
        await SayAsync("Alice");
        await SayAsync("skip");
        var prompt = await SayAsync("yes");
        _clock.Advance(TimeSpan.FromMinutes(11));

        var result = await SayAsync(GetPhrase(prompt));

        Assert.Contains("expired", result.Text);
        Assert.False(result.NeedsInput);
        Assert.Empty(await _profiles.GetAllAsync(CancellationToken.None));
        A.CallTo(() => _router.RouteAsync(A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task RestartedServer_DoesNotExecuteAnOutstandingEnrollmentPrompt()
    {
        await SayAsync("onboard me");
        await SayAsync("yes");
        await SayAsync("Alice");
        await SayAsync("skip");
        var prompt = await SayAsync("yes");

        using var restarted = new VoiceOnboardingConversationTests();
        restarted._conversationId = _conversationId;
        var result = await restarted.SayAsync(GetPhrase(prompt));

        Assert.Contains("expired", result.Text);
        Assert.False(result.NeedsInput);
        A.CallTo(() => restarted._router.RouteAsync(A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Theory]
    [InlineData("verified")]
    [InlineData("unknown")]
    [InlineData("legacy")]
    [InlineData("provisional")]
    [InlineData("revoked")]
    [InlineData("non-finite")]
    public async Task Memories_UseVerifiedProfileId_NotSpeakerNameOrHaServiceAccount(string mode)
    {
        await _profiles.CreateAsync(new SpeakerProfile
        {
            Id = "profile-a",
            Name = "Alice",
            IsProvisional = mode == "provisional",
            IsAuthorized = mode != "revoked",
        }, CancellationToken.None);
        await _memories.StoreAsync("profile-a", "drink", "coffee");
        await _memories.StoreAsync("profile-b", "drink", "tea");
        await _memories.StoreAsync("Alice", "drink", "lemonade");
        await _memories.StoreAsync("ha-service-account", "drink", "water");
        var speaker = mode == "unknown" ? null : new SpeakerIdentification
        {
            ProfileId = "profile-a", Name = "Alice", IsAuthorized = true,
            Similarity = mode == "non-finite" ? float.NaN : 0.95f,
        };

        var result = mode == "legacy"
            ? await _processor.ProcessAsync(CreateRequest("<Alice />What do you know about me?"))
            : await ProcessAsync("What do you know about me?", speaker: speaker);

        Assert.NotNull(result.LlmPrompt);
        Assert.Equal(mode == "verified", result.LlmPrompt.Contains("drink: coffee", StringComparison.Ordinal));
        Assert.DoesNotContain("drink: tea", result.LlmPrompt);
        Assert.DoesNotContain("drink: lemonade", result.LlmPrompt);
        Assert.DoesNotContain("drink: water", result.LlmPrompt);
        Assert.DoesNotContain("lucia-voice", result.LlmPrompt);
        Assert.Equal(mode == "verified" ? "voice:conversation-1:profile-a" : "voice:conversation-1:anonymous", result.EngineSessionId);
        Assert.Equal(mode == "verified" ? "profile-a" : null, result.SpeakerContext?.EnrolledProfileId);
        Assert.Equal("conversation-1", result.ConversationId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VoiceTokens_CannotBeReplayedOrUsedAfterExpiry(bool expire)
    {
        var tagged = _turns.Capture("onboard me", new float[32_000], 16_000, null);
        if (expire)
        {
            _clock.Advance(TimeSpan.FromMinutes(3));
        }
        else
        {
            await _processor.ProcessAsync(CreateRequest(tagged));
        }

        var result = await _processor.ProcessAsync(CreateRequest(tagged));

        Assert.Equal("error", result.Response!.Type);
        Assert.Contains("expired or was already used", result.Response.Text);
        A.CallTo(() => _router.RouteAsync(A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    private async Task<ConversationResponse> SayAsync(string text)
    {
        var response = Assert.IsType<ConversationResponse>((await ProcessAsync(text)).Response);
        _conversationId = response.ConversationId!;
        return response;
    }

    private Task<ProcessingResult> ProcessAsync(
        string text, string deviceId = "satellite-1", float[]? audio = null, SpeakerIdentification? speaker = null)
    {
        audio ??= Enumerable.Repeat(0.1f, 32_000).ToArray();
        var taggedText = _turns.Capture(text, audio, 16_000, speaker);
        var request = CreateRequest(taggedText, deviceId);
        return _processor.ProcessAsync(request with { Context = request.Context with { ConversationId = _conversationId } });
    }

    private static ConversationRequest CreateRequest(string text, string deviceId = "satellite-1") => new()
    {
        Text = text,
        Context = new ConversationContext
        {
            ConversationId = "conversation-1",
            DeviceId = deviceId,
            UserId = "ha-service-account",
        },
    };

    private static string GetPhrase(ConversationResponse response) =>
        response.Text[(response.Text.LastIndexOf("Please say: ", StringComparison.Ordinal) + "Please say: ".Length)..];

    public void Dispose()
    {
        _workflow.Dispose();
        _enrollment.Dispose();
        _turns.Dispose();
        _telemetry.Dispose();
        if (Directory.Exists(_clipPath))
        {
            Directory.Delete(_clipPath, recursive: true);
        }
    }
}
