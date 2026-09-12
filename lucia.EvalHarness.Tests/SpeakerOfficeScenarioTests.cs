using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Providers;
using Microsoft.Extensions.Logging;

namespace lucia.EvalHarness.Tests;

public sealed class SpeakerOfficeScenarioTests
{
    [Theory]
    [InlineData("Dianna's Office", true, false, true)]
    [InlineData("dianna's office lights", true, false, true)]
    [InlineData("office", true, false, false)]
    [InlineData("Zack's Office", true, false, false)]
    [InlineData("Dianna's Office", true, true, false)]
    [InlineData("Zack's Office", false, true, false)]
    [InlineData("Dianna's Office", false, false, false)]
    public async Task MyOffice_RequiresSpeakerOwnedSearchAndLeavesDeviceAreaLightsUnchanged(
        string searchTerms, bool diannaLightsOn, bool zackLightsOn, bool expectedPass)
    {
        var scenario = ScenarioLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "light-agent.yaml"))
            .Single(scenario => scenario.Id == "speaker_disambiguated_office_lights");
        Assert.Equal("Dianna", scenario.SpeakerId);
        Assert.Equal("Zack's Office", scenario.DeviceArea);

        using var loggerFactory = LoggerFactory.Create(_ => { });
        await using var factory = new RealAgentFactory(
            "http://unused:11434",
            Path.Combine(AppContext.BaseDirectory, "TestData", "ha-snapshot.json"),
            loggerFactory);
        await ScenarioValidator.SetupInitialStateAsync(
            factory.HomeAssistantClient, scenario, factory.EntityLocationService);

        foreach (var color in new[] { "white", "yellow" })
        {
            await factory.HomeAssistantClient.SetEntityStateAsync(
                $"light.guest_room_fan_{color}_light", diannaLightsOn ? "on" : "off");
            await factory.HomeAssistantClient.SetEntityStateAsync(
                $"light.office_fan_{color}_light", zackLightsOn ? "on" : "off");
        }

        var result = await ScenarioValidator.ValidateAsync(scenario,
        [
            new ConversationTurn
            {
                Role = "assistant",
                ToolCalls =
                [
                    new ToolCallInfo
                    {
                        Name = "ControlLights",
                        Arguments = new() { ["searchTerms"] = searchTerms, ["state"] = "on" }
                    }
                ]
            },
            new ConversationTurn { Role = "assistant", Content = "Your office lights are on." }
        ], factory.HomeAssistantClient);

        Assert.Equal(expectedPass, result.Passed);
    }
}
