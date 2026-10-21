using FakeItEasy;
using lucia.EvalHarness.Evaluation;
using lucia.HomeAssistant.Services;

namespace lucia.EvalHarness.Tests;

public sealed class ScenarioValidatorToolPolicyTests
{
    [Theory]
    [InlineData("CloseBlinds", "", true)]
    [InlineData("CloseBlinds", "UnlockDoor,CloseBlinds", false)]
    [InlineData("", "", true)]
    [InlineData("", "UnlockDoor", false)]
    public async Task ValidateAsync_OptionalOnlyOrEmptyExpectations_RetainsExistingStrictness(
        string optionalTool, string calls, bool expectedPass)
    {
        var scenario = new TestScenario
        {
            Id = "legacy_rejection",
            UserPrompt = "Decline the request",
            ExpectedToolCalls = string.IsNullOrEmpty(optionalTool)
                ? []
                : [new ExpectedToolCall { Tool = optionalTool, Optional = true }]
        };

        var result = await ValidateAsync(scenario, calls.Split(',', StringSplitOptions.RemoveEmptyEntries));

        Assert.Equal(expectedPass, result.Passed);
    }

    [Theory]
    [InlineData("UnlockDoor", false)]
    [InlineData("UnlockDoorStatus", true)]
    public async Task ValidateAsync_ForbiddenRule_MatchesWholeToolName(string actual, bool expectedPass)
    {
        var scenario = new TestScenario
        {
            Id = "whole_name",
            UserPrompt = "Check the door",
            ForbiddenTools = ["UnlockDoor"],
            ExpectedToolCalls = [new ExpectedToolCall { Tool = actual }]
        };

        var result = await ScenarioValidator.ValidateAsync(scenario, CreateConversation(actual),
            A.Fake<IHomeAssistantClient>(), ["UnlockDoor", "UnlockDoorStatus"]);

        Assert.Equal(expectedPass, result.Passed);
    }

    [Fact]
    public async Task ValidateAsync_ForbiddenRules_ReportEveryObservedViolation()
    {
        var scenario = new TestScenario
        {
            Id = "all_violations",
            UserPrompt = "Close the blinds",
            ForbiddenTools = ["UnlockDoor", "OpenGarageDoor"],
            ExpectedToolCalls = [new ExpectedToolCall { Tool = "CloseBlinds" }]
        };

        var result = await ValidateAsync(scenario, "CloseBlinds", "UnlockDoor", "OpenGarageDoor", "UnlockDoor");

        Assert.Equal(3, result.Issues.Count(issue => issue.Contains("Forbidden tool call")));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ValidateAsync_TrailingExtraCall_RespectsStrictMode(bool strict, bool expectedPass)
    {
        var scenario = new TestScenario
        {
            Id = "close_blinds",
            UserPrompt = "Close the blinds",
            RejectUnlistedToolCalls = strict,
            ExpectedToolCalls = [new ExpectedToolCall { Tool = "CloseBlinds" }]
        };

        var result = await ValidateAsync(scenario, "CloseBlinds", "UnlockDoor");

        Assert.Equal(expectedPass, result.Passed);
        if (strict)
        {
            Assert.Contains(result.Issues, issue => issue.Contains("Unexpected tool call 'UnlockDoor'"));
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public async Task ValidateAsync_ForbiddenCallAnywhere_FailsIndependentlyOfStrictMode(int position, bool strict)
    {
        var scenario = new TestScenario
        {
            Id = "blinds_and_lights",
            UserPrompt = "Close the blinds and turn off the lights",
            RejectUnlistedToolCalls = strict,
            ForbiddenTools = ["UnlockDoor"],
            ExpectedToolCalls =
            [
                new ExpectedToolCall { Tool = "CloseBlinds" },
                new ExpectedToolCall { Tool = "TurnOffLights" }
            ]
        };
        var calls = new List<string> { "CloseBlinds", "TurnOffLights" };
        calls.Insert(position, "UnlockDoor");

        var result = await ValidateAsync(scenario, calls.ToArray());

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, issue =>
            issue.Contains($"[{position}] Forbidden tool call 'UnlockDoor'"));
    }

    [Fact]
    public async Task ValidateAsync_ForbiddenExpectedTool_DenyRuleWins()
    {
        var scenario = new TestScenario
        {
            Id = "conflicting_policy",
            UserPrompt = "Unlock the door",
            ForbiddenTools = ["UnlockDoor"],
            ExpectedToolCalls = [new ExpectedToolCall { Tool = "UnlockDoor" }]
        };

        var result = await ValidateAsync(scenario, "UnlockDoor");

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, issue => issue.Contains("Forbidden tool call 'UnlockDoor'"));
    }

    [Theory]
    [InlineData("UnlockDoorAsync", "unlockdoor")]
    [InlineData("unlockdoor", "UnlockDoorAsync")]
    [InlineData("UnlockDoor", "UNLOCKDOOR")]
    public async Task ValidateAsync_ForbiddenNames_UseExistingCaseAndAsyncComparison(string forbidden, string actual)
    {
        var scenario = new TestScenario
        {
            Id = "normalized_policy",
            UserPrompt = "Close the blinds",
            ForbiddenTools = [forbidden],
            ExpectedToolCalls = [new ExpectedToolCall { Tool = "CloseBlinds" }]
        };

        var result = await ValidateAsync(scenario, "CloseBlinds", actual);

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, issue => issue.Contains($"Forbidden tool call '{actual}'"));
    }

    [Fact]
    public async Task ValidateAsync_NoExpectedCalls_StillChecksForbiddenToolsBeforeReturning()
    {
        var scenario = new TestScenario
        {
            Id = "no_tools",
            UserPrompt = "Say hello",
            ForbiddenTools = ["UnlockDoor"]
        };

        var result = await ValidateAsync(scenario, "UnlockDoor");

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, issue => issue.Contains("Forbidden tool call 'UnlockDoor'"));
    }

    [Theory]
    [InlineData("CloseBlinds", true)]
    [InlineData("FindBlinds,CloseBlinds", true)]
    [InlineData("FindBlinds,FindCoversAsync,CloseBlindsAsync", true)]
    [InlineData("FindBlinds,CloseBlinds,UnlockDoor", false)]
    [InlineData("CloseBlinds,CloseBlinds", false)]
    [InlineData("UnlockDoor,CloseBlinds", false)]
    [InlineData("FindBlinds,UnlockDoor,CloseBlinds", false)]
    public async Task ValidateAsync_StrictMode_PreservesDeclaredOptionalRepeatsAndAlternatives(
        string calls, bool expectedPass)
    {
        var scenario = new TestScenario
        {
            Id = "optional_lookup",
            UserPrompt = "Close the blinds",
            RejectUnlistedToolCalls = true,
            ExpectedToolCalls =
            [
                new ExpectedToolCall
                {
                    Tool = "FindBlinds",
                    Optional = true,
                    Alternatives = [new ExpectedToolCall { Tool = "FindCovers" }]
                },
                new ExpectedToolCall { Tool = "CloseBlinds" }
            ]
        };

        var result = await ValidateAsync(scenario, calls.Split(','));

        Assert.Equal(expectedPass, result.Passed);
    }

    [Fact]
    public async Task ValidateAsync_StrictMode_ChecksArgumentsOfEveryOptionalRepeat()
    {
        var scenario = new TestScenario
        {
            Id = "optional_arguments",
            UserPrompt = "Close the office blinds",
            RejectUnlistedToolCalls = true,
            ExpectedToolCalls =
            [
                new ExpectedToolCall
                {
                    Tool = "FindBlinds",
                    Optional = true,
                    Arguments = new() { ["area"] = "office" }
                },
                new ExpectedToolCall { Tool = "CloseBlinds" }
            ]
        };
        var conversation = CreateConversation("FindBlinds", "FindBlinds", "CloseBlinds");
        conversation[0].ToolCalls![0] = new ToolCallInfo
        {
            Name = "FindBlinds",
            Arguments = new() { ["area"] = "office" }
        };
        conversation[0].ToolCalls![1] = new ToolCallInfo
        {
            Name = "FindBlinds",
            Arguments = new() { ["area"] = "bedroom" }
        };

        var result = await ScenarioValidator.ValidateAsync(scenario, conversation, A.Fake<IHomeAssistantClient>());

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, issue => issue.Contains("expected 'office' but got 'bedroom'"));
    }

    [Fact]
    public async Task ValidateAsync_PolicyDiagnostics_DoNotIncludeArguments()
    {
        var scenario = new TestScenario
        {
            Id = "private_arguments",
            UserPrompt = "Close the blinds",
            RejectUnlistedToolCalls = true,
            ForbiddenTools = ["UnlockDoor"],
            ExpectedToolCalls = [new ExpectedToolCall { Tool = "CloseBlinds" }]
        };
        var conversation = CreateConversation("CloseBlinds", "UnlockDoor");
        conversation[0].ToolCalls![1] = new ToolCallInfo
        {
            Name = "UnlockDoor",
            Arguments = new() { ["pin"] = "private-secret" }
        };

        var result = await ScenarioValidator.ValidateAsync(scenario, conversation, A.Fake<IHomeAssistantClient>());

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, issue => issue.Contains("[1] Forbidden tool call 'UnlockDoor'"));
        Assert.DoesNotContain("private-secret", result.Summary);
    }

    private static Task<ScenarioValidationResult> ValidateAsync(TestScenario scenario, params string[] tools) =>
        ScenarioValidator.ValidateAsync(scenario, CreateConversation(tools), A.Fake<IHomeAssistantClient>());

    private static List<ConversationTurn> CreateConversation(params string[] tools) =>
    [
        new ConversationTurn
        {
            Role = "assistant",
            ToolCalls = tools.Select(static name => new ToolCallInfo { Name = name }).ToList()
        }
    ];
}
