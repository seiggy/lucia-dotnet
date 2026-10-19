using FakeItEasy;
using lucia.EvalHarness.Evaluation;
using lucia.HomeAssistant.Services;

namespace lucia.EvalHarness.Tests;

public sealed class ScenarioValidatorRepeatedOptionalToolTests
{
    [Theory]
    [InlineData("FindPlayer,FindPlayer,NextTrack", true)]
    [InlineData("FindPlayer,FindPlayer,FindPlayer,NextTrack", true)]
    [InlineData("FindPlayer,FindPlayer", false)]
    [InlineData("FindPlayer,FindPlayer,PlayArtist", false)]
    public async Task ValidateAsync_OptionalCall_AcceptsRepeatedLookups(string actualTools, bool expectedPass)
    {
        var scenario = new TestScenario
        {
            Id = "next_track",
            UserPrompt = "Skip this song",
            ExpectedToolCalls =
            [
                new ExpectedToolCall { Tool = "FindPlayer", Optional = true },
                new ExpectedToolCall { Tool = "NextTrack" }
            ]
        };
        var conversation = actualTools
            .Split(',')
            .Select(static name => new ConversationTurn
            {
                Role = "assistant",
                ToolCalls = [new ToolCallInfo { Name = name }]
            })
            .ToList();

        var result = await ScenarioValidator.ValidateAsync(
            scenario,
            conversation,
            A.Fake<IHomeAssistantClient>());

        Assert.Equal(expectedPass, result.Passed);
    }
}
