using FakeItEasy;
using lucia.EvalHarness.Evaluation;
using lucia.HomeAssistant.Services;

namespace lucia.EvalHarness.Tests;

public sealed class ScenarioValidatorOptionalToolTests
{
    [Theory]
    [InlineData("FindPlayer,NextTrack", true)]
    [InlineData("NextTrack", true)]
    [InlineData("FindPlayer,PlayArtist", false)]
    [InlineData("FindPlayer", false)]
    [InlineData("", false)]
    public async Task ValidateAsync_OptionalFindPlayer_AcceptsTheCallOrItsAbsence(string actualTools, bool expectedPass)
    {
        var scenario = new TestScenario
        {
            Id = "next_track",
            UserPrompt = "Skip this song on the office speaker",
            ExpectedToolCalls =
            [
                new ExpectedToolCall { Tool = "FindPlayer", Optional = true },
                new ExpectedToolCall { Tool = "NextTrack" }
            ]
        };
        var conversation = actualTools
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
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
