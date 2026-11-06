using FakeItEasy;
using lucia.AgentHost;
using lucia.AgentHost.Conversation;
using lucia.AgentHost.Conversation.Execution;
using lucia.AgentHost.Conversation.Models;
using lucia.AgentHost.Conversation.Templates;
using lucia.AgentHost.Conversation.Tracing;
using lucia.Agents.Abstractions;
using lucia.Agents.Family;
using lucia.Agents.Orchestration;
using lucia.Agents.Services;
using lucia.Data.InMemory;
using lucia.Tests.Wyoming;
using lucia.Wyoming.CommandRouting;
using lucia.Wyoming.Diarization;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Conversation;

/// <summary>
/// Requests attributed to a family member who needs mediation skip the deterministic fast
/// path, so every state change goes through the action broker in the agent pipeline.
/// </summary>
public sealed class ConversationMemberMediationTests : IDisposable
{
    private readonly ICommandRouter _router = A.Fake<ICommandRouter>();
    private readonly IDirectSkillExecutor _executor = A.Fake<IDirectSkillExecutor>();
    private readonly IFamilyMemberStore _members = A.Fake<IFamilyMemberStore>();
    private readonly AgentHostTelemetrySource _telemetry = new();
    private readonly ConversationCommandProcessor _processor;

    public ConversationMemberMediationTests()
    {
        var pattern = new CommandPattern
        {
            Id = "light-toggle",
            SkillId = "LightControlSkill",
            Action = "toggle",
            Templates = ["{action:on|off} [the] {entity} [lights]"],
        };
        A.CallTo(() => _router.RouteAsync(A<string>._, A<CancellationToken>._))
            .Returns(new CommandRouteResult
            {
                IsMatch = true,
                Confidence = 0.95f,
                MatchedPattern = pattern,
                CapturedValues = new Dictionary<string, string> { ["action"] = "on", ["entity"] = "kitchen" },
            });
        A.CallTo(() => _executor.ExecuteAsync(A<CommandRouteResult>._, A<ConversationContext>._, A<CancellationToken>._))
            .Returns(SkillExecutionResult.Bail("LightControlSkill", "toggle", "test", "test", TimeSpan.Zero));
        A.CallTo(() => _members.GetAsync(A<string>._, A<CancellationToken>._)).Returns((FamilyMember?)null);
        A.CallTo(() => _members.GetAsync("kid", A<CancellationToken>._))
            .Returns(new FamilyMember { Id = "kid", DisplayName = "Sam", Role = FamilyRole.Child, GuardianIds = ["parent"] });
        A.CallTo(() => _members.GetAsync("parent", A<CancellationToken>._))
            .Returns(new FamilyMember { Id = "parent", DisplayName = "Alex", Role = FamilyRole.Adult });

        var services = A.Fake<IServiceProvider>();
        A.CallTo(() => services.GetService(A<Type>._)).Returns(null);
        _services = services;
        _processor = CreateProcessor(speakerFilter: null);
    }

    private readonly IServiceProvider _services;

    private ConversationCommandProcessor CreateProcessor(SpeakerVerificationFilter? speakerFilter)
    {
        var memories = new InMemoryMemoryStore();
        return new ConversationCommandProcessor(
            _router,
            _executor,
            new ResponseTemplateRenderer(A.Fake<IResponseTemplateRepository>(), NullLogger<ResponseTemplateRenderer>.Instance),
            new ContextReconstructor(new UserContextProvider(memories), new ChatHistoryProvider(memories)),
            new ConversationTelemetry(_telemetry),
            new lucia.AgentHost.Conversation.Tracing.InMemoryCommandTraceRepository(),
            new CommandTraceChannel(),
            _services,
            NullLogger<ConversationCommandProcessor>.Instance,
            new OptionsMonitorStub<CommandRoutingOptions>(new CommandRoutingOptions()),
            new OptionsMonitorStub<PersonalityPromptOptions>(new PersonalityPromptOptions()),
            speakerFilter: speakerFilter,
            familyMembers: _members,
            actionBroker: new ActionBroker(A.Fake<IActionRecordStore>(), _members, TimeProvider.System, NullLogger<ActionBroker>.Instance));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task MemberAppRequests_AreNotTreatedAsUnrecognizedVoice(bool isMemberApp, bool expectIgnored)
    {
        var processor = CreateProcessor(new SpeakerVerificationFilter(
            new OptionsMonitorStub<VoiceProfileOptions>(new VoiceProfileOptions { IgnoreUnknownVoices = true }),
            NullLogger<SpeakerVerificationFilter>.Instance));
        var request = new ConversationRequest
        {
            Text = "turn on the kitchen lights",
            Context = new ConversationContext { ConversationId = "conv-1", DeviceId = "phone-1", MemberId = "parent", IsMemberApp = isMemberApp },
        };

        var result = await processor.ProcessAsync(request);

        Assert.Equal(expectIgnored, result.Response?.Type == "ignored");
    }

    public void Dispose() => _telemetry.Dispose();

    private static ConversationRequest Request(string? memberId) => new()
    {
        Text = "turn on the kitchen lights",
        Context = new ConversationContext { ConversationId = "conv-1", MemberId = memberId },
    };

    [Fact]
    public async Task MediatedMember_SkipsTheFastPathAndGetsAMemberScopedSession()
    {
        var result = await _processor.ProcessAsync(Request("kid"));

        A.CallTo(() => _executor.ExecuteAsync(A<CommandRouteResult>._, A<ConversationContext>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        Assert.Equal(ProcessingKind.LlmFallback, result.Kind);
        Assert.Equal("member:kid:conv-1", result.EngineSessionId);
    }

    [Fact]
    public async Task UnrestrictedMember_KeepsTheFastPath()
    {
        var result = await _processor.ProcessAsync(Request("parent"));

        A.CallTo(() => _executor.ExecuteAsync(A<CommandRouteResult>._, A<ConversationContext>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        Assert.Equal("member:parent:conv-1", result.EngineSessionId);
    }

    [Fact]
    public async Task NoMember_BehavesAsBefore()
    {
        var result = await _processor.ProcessAsync(Request(null));

        A.CallTo(() => _executor.ExecuteAsync(A<CommandRouteResult>._, A<ConversationContext>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        Assert.Equal("conv-1", result.EngineSessionId);
    }
}
