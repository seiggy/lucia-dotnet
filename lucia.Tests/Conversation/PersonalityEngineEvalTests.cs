using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Orchestration;
using lucia.Agents.Orchestration.Models;
using lucia.AgentHost.Conversation.Templates;
using lucia.Tests.TestDoubles;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Conversation;

/// <summary>
/// Comprehensive eval tests for the personality engine prompt construction.
/// Validates that the correct system/user messages are sent to the LLM for both the
/// fast-path (<see cref="PersonalityResponseRenderer"/>) and the orchestrator path
/// (<see cref="ResultAggregatorExecutor"/>).
/// </summary>
public sealed class PersonalityEngineEvalTests : TestBase
{
    private const string PiratePersonality = "You are a friendly pirate captain. Speak in pirate dialect.";
    private const string DefaultCannedResponse = "Turned on the kitchen lights.";

    // ─────────────────────────────────────────────────────────────────────
    // Section 1: Fast-path prompt construction (PersonalityResponseRenderer)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FastPath_SystemMessage_ContainsPersonalityInstructions()
    {
        // Arrange
        var (renderer, stub) = CreateFastPathRenderer(PiratePersonality);

        // Act
        await renderer.RenderAsync(
            "LightControlSkill", "toggle", DefaultCannedResponse,
            CreateCaptures("on", "kitchen"));

        // Assert — system message is the personality instructions
        var messages = stub.CapturedMessages[0];
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Equal(PiratePersonality, messages[0].Text);
    }

    [Fact]
    public async Task FastPath_UserMessage_ContainsRephraseInstruction()
    {
        // Arrange
        var (renderer, stub) = CreateFastPathRenderer(PiratePersonality);

        // Act
        await renderer.RenderAsync(
            "LightControlSkill", "toggle", DefaultCannedResponse,
            CreateCaptures("on", "kitchen"));

        // Assert — user message starts with "Rephrase"
        var userText = stub.CapturedMessages[0][1].Text!;
        Assert.Contains("Rephrase", userText);
    }

    [Fact]
    public async Task FastPath_UserMessage_ContainsActualSkillResultText()
    {
        // Arrange
        var (renderer, stub) = CreateFastPathRenderer(PiratePersonality);
        var skillResultText = "'Kitchen Light' entity turned on successfully.";

        // Act
        await renderer.RenderAsync(
            "LightControlSkill", "toggle", DefaultCannedResponse,
            CreateCaptures("on", "kitchen"),
            skillResultText: skillResultText);

        // Assert — the actual skill result is in the prompt, not just the canned template
        var userText = stub.CapturedMessages[0][1].Text!;
        Assert.Contains(skillResultText, userText);
    }

    [Fact]
    public async Task FastPath_UserMessage_FallsBackToCannedResponse_WhenSkillResultNull()
    {
        // Arrange
        var (renderer, stub) = CreateFastPathRenderer(PiratePersonality);

        // Act
        await renderer.RenderAsync(
            "LightControlSkill", "toggle", DefaultCannedResponse,
            CreateCaptures("on", "kitchen"),
            skillResultText: null);

        // Assert — falls back to canned response text in the prompt
        var userText = stub.CapturedMessages[0][1].Text!;
        Assert.Contains(DefaultCannedResponse, userText);
    }

    [Theory]
    [InlineData(true, "voice tags")]
    [InlineData(false, "Plain text only")]
    public async Task FastPath_VoiceTagInstruction_ReflectsSupportVoiceTagsFlag(bool supportVoiceTags, string expectedFragment)
    {
        // Arrange
        var (renderer, stub) = CreateFastPathRenderer(PiratePersonality, supportVoiceTags);

        // Act
        await renderer.RenderAsync(
            "LightControlSkill", "toggle", DefaultCannedResponse,
            CreateCaptures("on", "kitchen"));

        // Assert — correct voice tag instruction present
        var userText = stub.CapturedMessages[0][1].Text!;
        Assert.Contains(expectedFragment, userText);
    }

    [Theory]
    [InlineData("LightControlSkill", "toggle", "on", "kitchen", "on kitchen lights")]
    [InlineData("ClimateControlSkill", "set_temperature", "set_temperature", "thermostat", "climate for thermostat")]
    [InlineData("SceneControlSkill", "activate", "activate", "Movie Night", "activate scene Movie Night")]
    public async Task FastPath_ActionDescription_IncludesSkillSpecificFormat(
        string skillId, string action, string actionValue, string entity, string expectedFragment)
    {
        // Arrange
        var (renderer, stub) = CreateFastPathRenderer(PiratePersonality);
        var captures = new Dictionary<string, string>
        {
            ["action"] = actionValue,
            ["entity"] = entity
        };

        // Act
        await renderer.RenderAsync(skillId, action, "Done.", captures);

        // Assert — action description matches skill-specific format
        var userText = stub.CapturedMessages[0][1].Text!;
        Assert.Contains(expectedFragment, userText);
    }

    [Fact]
    public async Task FastPath_LightControlSkill_BrightnessAction_IncludesValueInPrompt()
    {
        // Arrange
        var (renderer, stub) = CreateFastPathRenderer(PiratePersonality);
        var captures = new Dictionary<string, string>
        {
            ["action"] = "brightness",
            ["entity"] = "living room",
            ["value"] = "75"
        };

        // Act
        await renderer.RenderAsync("LightControlSkill", "brightness", "Set brightness to 75%.", captures);

        // Assert — brightness value included in action description
        var userText = stub.CapturedMessages[0][1].Text!;
        Assert.Contains("75%", userText);
        Assert.Contains("brightness", userText);
    }

    [Fact]
    public async Task FastPath_ClimateControlSkill_WithValue_IncludesValueInPrompt()
    {
        // Arrange
        var (renderer, stub) = CreateFastPathRenderer(PiratePersonality);
        var captures = new Dictionary<string, string>
        {
            ["action"] = "set_temperature",
            ["entity"] = "thermostat",
            ["area"] = "bedroom",
            ["value"] = "22"
        };

        // Act
        await renderer.RenderAsync("ClimateControlSkill", "set_temperature", "Temperature set.", captures);

        // Assert — temperature value and area included in action description
        var userText = stub.CapturedMessages[0][1].Text!;
        Assert.Contains("22", userText);
        Assert.Contains("bedroom", userText);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Section 2: Orchestrator prompt construction (ResultAggregatorExecutor)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Orchestrator_UserMessage_ContainsRephraseInstructionWrapper()
    {
        // Arrange
        var (aggregator, stub) = CreateOrchestratorWithPersonality(PiratePersonality);

        // Act
        await InvokeAggregatorAsync(aggregator,
            new AgentResponseBuilder().WithAgentId("light-agent").WithContent("Lights are on.").Build());

        // Assert
        var userText = stub.CapturedMessages[0][1].Text!;
        Assert.Contains("Rephrase the following smart home assistant response in your voice", userText);
    }

    [Fact]
    public async Task Orchestrator_UserMessage_ContainsKeepSameMeaningGuardrails()
    {
        // Arrange
        var (aggregator, stub) = CreateOrchestratorWithPersonality(PiratePersonality);

        // Act
        await InvokeAggregatorAsync(aggregator,
            new AgentResponseBuilder().WithAgentId("light-agent").WithContent("Lights are on.").Build());

        // Assert
        var userText = stub.CapturedMessages[0][1].Text!;
        Assert.Contains("Keep the SAME meaning", userText);
    }

    [Fact]
    public async Task Orchestrator_UserMessage_ContainsNeverRefuseGuardrail()
    {
        // Arrange
        var (aggregator, stub) = CreateOrchestratorWithPersonality(PiratePersonality);

        // Act
        await InvokeAggregatorAsync(aggregator,
            new AgentResponseBuilder().WithAgentId("light-agent").WithContent("Lights are on.").Build());

        // Assert — critical guardrail against LLM refusal behavior
        var userText = stub.CapturedMessages[0][1].Text!;
        Assert.Contains("Never refuse", userText);
        Assert.Contains("never say you can't do things", userText);
    }

    [Fact]
    public async Task Orchestrator_UserMessage_PreservesOriginalContent_SuccessResponse()
    {
        // Arrange
        var (aggregator, stub) = CreateOrchestratorWithPersonality(PiratePersonality);
        const string originalContent = "The living room lights have been turned on and set to 80% brightness.";

        // Act
        await InvokeAggregatorAsync(aggregator,
            new AgentResponseBuilder().WithAgentId("light-agent").WithContent(originalContent).Build());

        // Assert — original agent response is preserved in prompt
        var userText = stub.CapturedMessages[0][1].Text!;
        Assert.Contains(originalContent, userText);
    }

    [Fact]
    public async Task Orchestrator_UserMessage_PreservesOriginalContent_ErrorResponse()
    {
        // Arrange
        var (aggregator, stub) = CreateOrchestratorWithPersonality(PiratePersonality);
        const string errorMessage = "light-agent service timed out";

        // Act
        await InvokeAggregatorAsync(aggregator,
            new AgentResponseBuilder().WithAgentId("light-agent").WithError(errorMessage).Build());

        // Assert — error information is preserved in composed message sent to LLM
        var userText = stub.CapturedMessages[0][1].Text!;
        Assert.Contains(errorMessage, userText);
    }

    [Fact]
    public async Task Orchestrator_UserMessage_PreservesOriginalContent_ClarificationQuestion()
    {
        // Arrange
        var (aggregator, stub) = CreateOrchestratorWithPersonality(PiratePersonality);
        const string question = "Which light did you mean — the ceiling light or the lamp?";

        // Act
        await InvokeAggregatorAsync(aggregator,
            new AgentResponseBuilder().WithAgentId("light-agent").WithContent(question).WithNeedsInput().Build());

        // Assert — clarification question is preserved in prompt
        var userText = stub.CapturedMessages[0][1].Text!;
        Assert.Contains(question, userText);
    }

    [Fact]
    public async Task Orchestrator_SystemMessage_ContainsPersonalityInstructions()
    {
        // Arrange
        var (aggregator, stub) = CreateOrchestratorWithPersonality(PiratePersonality);

        // Act
        await InvokeAggregatorAsync(aggregator,
            new AgentResponseBuilder().WithAgentId("light-agent").WithContent("Lights on.").Build());

        // Assert
        var systemMessage = stub.CapturedMessages[0][0];
        Assert.Equal(ChatRole.System, systemMessage.Role);
        Assert.Equal(PiratePersonality, systemMessage.Text);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Section 3: Personality bypass scenarios
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FastPath_NullInstructions_ReturnsRawResponse_NoLlmCall()
    {
        // Arrange — null instructions
        var (renderer, stub) = CreateFastPathRenderer(instructions: null);

        // Act
        var result = await renderer.RenderAsync(
            "LightControlSkill", "toggle", DefaultCannedResponse,
            CreateCaptures("on", "kitchen"));

        // Assert — raw response returned, no LLM call
        Assert.Equal(DefaultCannedResponse, result);
        Assert.Equal(0, stub.InvocationCount);
    }

    [Fact]
    public async Task FastPath_EmptyInstructions_ReturnsRawResponse_NoLlmCall()
    {
        // Arrange — empty string instructions
        var (renderer, stub) = CreateFastPathRenderer(instructions: "   ");

        // Act
        var result = await renderer.RenderAsync(
            "LightControlSkill", "toggle", DefaultCannedResponse,
            CreateCaptures("on", "kitchen"));

        // Assert — raw response returned, no LLM call
        Assert.Equal(DefaultCannedResponse, result);
        Assert.Equal(0, stub.InvocationCount);
    }

    [Fact]
    public async Task Orchestrator_NullPersonality_ReturnsRawComposedMessage()
    {
        // Arrange — no personality configured
        var aggregator = new ResultAggregatorExecutor(
            CreateLogger<ResultAggregatorExecutor>(),
            CreateOptions(new ResultAggregatorOptions()),
            personalityChatClient: null,
            personalityInstructions: null);

        // Act
        var result = await InvokeAggregatorAsync(aggregator,
            new AgentResponseBuilder().WithAgentId("light-agent").WithContent("Lights on.").Build());

        // Assert — raw composed message, no LLM call
        Assert.Equal("Lights on.", result.Text);
    }

    [Fact]
    public async Task Orchestrator_EmptyInstructions_ReturnsRawComposedMessage()
    {
        // Arrange — empty instructions with non-null client
        var stub = CreateStubChatClient("should not be called");
        var aggregator = new ResultAggregatorExecutor(
            CreateLogger<ResultAggregatorExecutor>(),
            CreateOptions(new ResultAggregatorOptions()),
            personalityChatClient: stub,
            personalityInstructions: "   ");

        // Act
        var result = await InvokeAggregatorAsync(aggregator,
            new AgentResponseBuilder().WithAgentId("light-agent").WithContent("Lights on.").Build());

        // Assert — raw composed message, no LLM call
        Assert.Equal("Lights on.", result.Text);
        Assert.Equal(0, stub.InvocationCount);
    }

    [Fact]
    public async Task FastPath_LlmThrows_FallsBackToRawResponse()
    {
        // Arrange — LLM throws a non-cancellation exception
        var stub = new StubChatClient([_ => throw new InvalidOperationException("LLM down")]);
        var resolver = A.Fake<IChatClientResolver>();
        A.CallTo(() => resolver.ResolveAsync(A<string?>._, A<CancellationToken>._)).Returns(stub);

        var renderer = new PersonalityResponseRenderer(
            resolver,
            CreateOptionsMonitor(new PersonalityPromptOptions
            {
                Instructions = PiratePersonality,
                UsePersonalityResponses = true
            }),
            CreateLogger<PersonalityResponseRenderer>());

        // Act — should not throw
        var result = await renderer.RenderAsync(
            "LightControlSkill", "toggle", DefaultCannedResponse,
            CreateCaptures("on", "kitchen"));

        // Assert — falls back to canned response
        Assert.Equal(DefaultCannedResponse, result);
    }

    [Fact]
    public async Task FastPath_LlmReturnsEmpty_FallsBackToRawResponse()
    {
        // Arrange — LLM returns whitespace
        var stub = new StubChatClient(
        [
            _ => new ChatResponse([new ChatMessage(ChatRole.Assistant, "   ")])
        ]);
        var resolver = A.Fake<IChatClientResolver>();
        A.CallTo(() => resolver.ResolveAsync(A<string?>._, A<CancellationToken>._)).Returns(stub);

        var renderer = new PersonalityResponseRenderer(
            resolver,
            CreateOptionsMonitor(new PersonalityPromptOptions
            {
                Instructions = PiratePersonality,
                UsePersonalityResponses = true
            }),
            CreateLogger<PersonalityResponseRenderer>());

        // Act
        var result = await renderer.RenderAsync(
            "LightControlSkill", "toggle", DefaultCannedResponse,
            CreateCaptures("on", "kitchen"));

        // Assert — falls back to canned response
        Assert.Equal(DefaultCannedResponse, result);
    }

    [Fact]
    public async Task FastPath_OperationCanceled_Propagates()
    {
        // Arrange — LLM throws OperationCanceledException
        var stub = new StubChatClient([_ => throw new OperationCanceledException()]);
        var resolver = A.Fake<IChatClientResolver>();
        A.CallTo(() => resolver.ResolveAsync(A<string?>._, A<CancellationToken>._)).Returns(stub);

        var renderer = new PersonalityResponseRenderer(
            resolver,
            CreateOptionsMonitor(new PersonalityPromptOptions
            {
                Instructions = PiratePersonality,
                UsePersonalityResponses = true
            }),
            CreateLogger<PersonalityResponseRenderer>());

        // Act & Assert — cancellation must propagate, not be swallowed
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            renderer.RenderAsync("LightControlSkill", "toggle", DefaultCannedResponse,
                CreateCaptures("on", "kitchen")));
    }

    [Fact]
    public async Task Orchestrator_LlmReturnsEmpty_FallsBackToRawMessage()
    {
        // Arrange
        var stub = new StubChatClient(
        [
            _ => new ChatResponse([new ChatMessage(ChatRole.Assistant, "  ")])
        ]);

        var aggregator = new ResultAggregatorExecutor(
            CreateLogger<ResultAggregatorExecutor>(),
            CreateOptions(new ResultAggregatorOptions()),
            personalityChatClient: stub,
            personalityInstructions: PiratePersonality);

        // Act
        var result = await InvokeAggregatorAsync(aggregator,
            new AgentResponseBuilder().WithAgentId("light-agent").WithContent("Lights on.").Build());

        // Assert — falls back to raw composed message
        Assert.Equal("Lights on.", result.Text);
    }

    [Fact]
    public async Task Orchestrator_OperationCanceled_Propagates()
    {
        // Arrange
        var stub = new StubChatClient([_ => throw new OperationCanceledException()]);

        var aggregator = new ResultAggregatorExecutor(
            CreateLogger<ResultAggregatorExecutor>(),
            CreateOptions(new ResultAggregatorOptions()),
            personalityChatClient: stub,
            personalityInstructions: PiratePersonality);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            InvokeAggregatorAsync(aggregator,
                new AgentResponseBuilder().WithAgentId("light-agent").WithContent("Lights on.").Build()).AsTask());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Section 4: Multi-agent aggregation with personality
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Orchestrator_TwoSuccessResponses_ComposedMessageSentToLlm()
    {
        // Arrange
        var (aggregator, stub) = CreateOrchestratorWithPersonality(PiratePersonality);

        // Act
        await InvokeAggregatorAsync(aggregator,
            new AgentResponseBuilder().WithAgentId("light-agent").WithContent("Turned on the lights.").Build(),
            new AgentResponseBuilder().WithAgentId("music-agent").WithContent("Playing jazz.").Build());

        // Assert — both agent responses present in the composed message sent to LLM
        var userText = stub.CapturedMessages[0][1].Text!;
        Assert.Contains("Turned on the lights.", userText);
        Assert.Contains("Playing jazz.", userText);
    }

    [Fact]
    public async Task Orchestrator_OneSuccessOneFail_ComposedMessageIncludesFailureInfo()
    {
        // Arrange
        var (aggregator, stub) = CreateOrchestratorWithPersonality(PiratePersonality);

        // Act
        await InvokeAggregatorAsync(aggregator,
            new AgentResponseBuilder().WithAgentId("light-agent").WithContent("Lights are on.").Build(),
            new AgentResponseBuilder().WithAgentId("music-agent").WithError("Spotify connection lost").Build());

        // Assert — composed message includes both success and failure info
        var userText = stub.CapturedMessages[0][1].Text!;
        Assert.Contains("Lights are on.", userText);
        Assert.Contains("Spotify connection lost", userText);
    }

    [Fact]
    public async Task Orchestrator_SingleAgentResponse_ForwardedDirectlyToLlm()
    {
        // Arrange
        var (aggregator, stub) = CreateOrchestratorWithPersonality(PiratePersonality);
        const string singleResponse = "The bedroom thermostat is now set to 22°C.";

        // Act
        await InvokeAggregatorAsync(aggregator,
            new AgentResponseBuilder().WithAgentId("climate-agent").WithContent(singleResponse).Build());

        // Assert — single response forwarded to personality LLM
        Assert.Equal(1, stub.InvocationCount);
        var userText = stub.CapturedMessages[0][1].Text!;
        Assert.Contains(singleResponse, userText);
    }

    [Fact]
    public async Task Orchestrator_ErrorOnlyResponses_ComposedFailureMessageSentToLlm()
    {
        // Arrange
        var (aggregator, stub) = CreateOrchestratorWithPersonality(PiratePersonality);

        // Act
        await InvokeAggregatorAsync(aggregator,
            new AgentResponseBuilder().WithAgentId("light-agent").WithError("Device unreachable").Build());

        // Assert — error message is in the composed text sent to LLM
        var userText = stub.CapturedMessages[0][1].Text!;
        Assert.Contains("Device unreachable", userText);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helper methods
    // ─────────────────────────────────────────────────────────────────────

    private (PersonalityResponseRenderer Renderer, StubChatClient Stub) CreateFastPathRenderer(
        string? instructions, bool supportVoiceTags = false)
    {
        var stub = CreateStubChatClient("Arrr, the lights be on!");
        var resolver = A.Fake<IChatClientResolver>();
        A.CallTo(() => resolver.ResolveAsync(A<string?>._, A<CancellationToken>._)).Returns(stub);

        var renderer = new PersonalityResponseRenderer(
            resolver,
            CreateOptionsMonitor(new PersonalityPromptOptions
            {
                Instructions = instructions,
                UsePersonalityResponses = true,
                SupportVoiceTags = supportVoiceTags
            }),
            CreateLogger<PersonalityResponseRenderer>());

        return (renderer, stub);
    }

    private (ResultAggregatorExecutor Aggregator, StubChatClient Stub) CreateOrchestratorWithPersonality(
        string instructions)
    {
        var stub = CreateStubChatClient("Ahoy! All done, captain!");
        var aggregator = new ResultAggregatorExecutor(
            CreateLogger<ResultAggregatorExecutor>(),
            CreateOptions(new ResultAggregatorOptions()),
            personalityChatClient: stub,
            personalityInstructions: instructions);

        return (aggregator, stub);
    }

    private static StubChatClient CreateStubChatClient(string response)
    {
        return new StubChatClient(
        [
            _ => new ChatResponse([new ChatMessage(ChatRole.Assistant, response)])
        ]);
    }

    private static async ValueTask<OrchestratorResult> InvokeAggregatorAsync(
        ResultAggregatorExecutor aggregator, params OrchestratorAgentResponse[] responses)
    {
        var context = A.Fake<IWorkflowContext>();
        return await aggregator.HandleAsync(responses.ToList(), context, CancellationToken.None);
    }

    private static Dictionary<string, string> CreateCaptures(string action, string entity, string? area = null)
    {
        var captures = new Dictionary<string, string>
        {
            ["action"] = action,
            ["entity"] = entity
        };

        if (area is not null)
        {
            captures["area"] = area;
        }

        return captures;
    }

    private static IOptionsMonitor<T> CreateOptionsMonitor<T>(T value)
    {
        var monitor = A.Fake<IOptionsMonitor<T>>();
        A.CallTo(() => monitor.CurrentValue).Returns(value);
        return monitor;
    }
}
