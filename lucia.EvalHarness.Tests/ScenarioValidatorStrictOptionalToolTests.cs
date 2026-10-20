using FakeItEasy;
using lucia.EvalHarness.Evaluation;
using lucia.HomeAssistant.Services;

namespace lucia.EvalHarness.Tests;

public sealed class ScenarioValidatorStrictOptionalToolTests
{
    [Fact]
    public async Task ValidateAsync_OnlyOptionalExpectations_RejectsUnexpectedCalls()
    {
        var scenario = new TestScenario
        {
            Id = "out_of_domain",
            UserPrompt = "Set the thermostat to 72",
            ExpectedToolCalls = [new ExpectedToolCall { Tool = "ListScenes", Optional = true }]
        };

        var result = await ValidateAsync(scenario, "ListScenes", "ActivateScene");

        Assert.False(result.Passed);
    }

    [Fact]
    public async Task ValidateAsync_OnlyOptionalExpectations_AcceptsTheOptionalLookup()
    {
        var scenario = new TestScenario
        {
            Id = "out_of_domain",
            UserPrompt = "Set the thermostat to 72",
            ExpectedToolCalls = [new ExpectedToolCall { Tool = "ListScenes", Optional = true }]
        };

        var result = await ValidateAsync(scenario, "ListScenes");

        Assert.True(result.Passed);
    }

    [Fact]
    public async Task ValidateAsync_RepeatedOptionalCall_ChecksArgumentsOfEveryRepeat()
    {
        var scenario = new TestScenario
        {
            Id = "next_track",
            UserPrompt = "Skip this song",
            ExpectedToolCalls =
            [
                new ExpectedToolCall
                {
                    Tool = "FindPlayer",
                    Optional = true,
                    Arguments = new Dictionary<string, string> { ["playerName"] = "contains:office" }
                },
                new ExpectedToolCall { Tool = "NextTrack" }
            ]
        };

        var result = await ValidateAsync(
            scenario,
            ("FindPlayer", "office"),
            ("FindPlayer", "bedroom"),
            ("NextTrack", "office"));

        Assert.False(result.Passed);
    }

    private static Task<ScenarioValidationResult> ValidateAsync(TestScenario scenario, params string[] tools) =>
        ValidateAsync(scenario, tools.Select(static tool => (tool, (string?)null)).ToArray());

    private static Task<ScenarioValidationResult> ValidateAsync(
        TestScenario scenario,
        params (string Tool, string? PlayerName)[] calls)
    {
        var conversation = calls
            .Select(static call => new ConversationTurn
            {
                Role = "assistant",
                ToolCalls =
                [
                    new ToolCallInfo
                    {
                        Name = call.Tool,
                        Arguments = call.PlayerName is null
                            ? null
                            : new Dictionary<string, string?> { ["playerName"] = call.PlayerName }
                    }
                ]
            })
            .ToList();

        return ScenarioValidator.ValidateAsync(scenario, conversation, A.Fake<IHomeAssistantClient>());
    }
}
