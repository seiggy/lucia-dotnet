using FakeItEasy;
using lucia.EvalHarness.Evaluation;
using lucia.HomeAssistant.Services;

namespace lucia.EvalHarness.Tests;

public sealed class ScenarioValidatorResponseTests
{
    [Theory]
    [InlineData("Playing \u201cRandom Access Memories\u201d by Daft Punk.")]
    [InlineData("Playing Random\u202FAccess\u202FMemories now.")]
    [InlineData("Playing  Random\u00A0Access   Memories now.")]
    public async Task ValidateAsync_ResponseText_IgnoresTypographyAndSpacingDifferences(string response)
    {
        var scenario = new TestScenario
        {
            Id = "album",
            UserPrompt = "Play Random Access Memories",
            ResponseMustContain = ["Random Access Memories"]
        };

        var result = await ValidateAsync(scenario, response);

        Assert.True(result.Passed, result.Summary);
    }

    [Fact]
    public async Task ValidateAsync_ProhibitedText_IsAlsoMatchedAfterNormalization()
    {
        var scenario = new TestScenario
        {
            Id = "no-bedroom",
            UserPrompt = "Play music",
            ResponseMustNotContain = ["bedroom speaker"]
        };

        var result = await ValidateAsync(scenario, "Using the bedroom\u202Fspeaker.");

        Assert.False(result.Passed);
    }

    [Fact]
    public async Task ValidateAsync_ApostropheVariants_MatchStraightApostrophe()
    {
        var scenario = new TestScenario
        {
            Id = "apostrophe",
            UserPrompt = "Play music",
            ResponseMustContain = ["Zack's Office"]
        };

        var result = await ValidateAsync(scenario, "Playing in Zack\u2019s Office.");

        Assert.True(result.Passed, result.Summary);
    }

    private static Task<ScenarioValidationResult> ValidateAsync(TestScenario scenario, string response) =>
        ScenarioValidator.ValidateAsync(
            scenario,
            [new ConversationTurn { Role = "assistant", Content = response }],
            A.Fake<IHomeAssistantClient>());
}
