using lucia.EvalHarness.Evaluation;

namespace lucia.EvalHarness.Tests;

public sealed class ScenarioLoaderToolPolicyTests
{
    [Fact]
    public void LoadFromFile_SceneFixtures_DeclareReadOnlyPolicies()
    {
        var scenarios = ScenarioLoader.LoadFromFile(
            Path.Combine(AppContext.BaseDirectory, "TestData", "scene-agent.yaml"));

        var listing = Assert.Single(scenarios, scenario => scenario.Id == "list_scenes");
        Assert.True(listing.RejectUnlistedToolCalls);
        Assert.Contains("ActivateScene", listing.ForbiddenTools);
        var rejection = Assert.Single(scenarios, scenario => scenario.Id == "out_of_domain");
        Assert.Contains("ActivateScene", rejection.ForbiddenTools);
        var activation = Assert.Single(scenarios, scenario => scenario.Id == "activate_movie");
        Assert.False(activation.RejectUnlistedToolCalls);
        Assert.Empty(activation.ForbiddenTools);
    }

    [Fact]
    public void LoadFromFile_SnakeCaseToolPolicies_LoadsExplicitValuesAndCompatibleDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lucia-tool-policy-{Guid.NewGuid():N}.yaml");
        try
        {
            File.WriteAllText(path, """
                scenarios:
                  - id: strict
                    user_prompt: Close the blinds
                    reject_unlisted_tool_calls: true
                    forbidden_tools:
                      - UnlockDoor
                      - OpenGarageDoorAsync
                    expected_tool_calls:
                      - tool: CloseBlinds
                  - id: legacy
                    user_prompt: Close the blinds
                    expected_tool_calls:
                      - tool: CloseBlinds
                """);

            var scenarios = ScenarioLoader.LoadFromFile(path);

            Assert.Equal(2, scenarios.Count);
            Assert.True(scenarios[0].RejectUnlistedToolCalls);
            Assert.Equal(new[] { "UnlockDoor", "OpenGarageDoorAsync" }, scenarios[0].ForbiddenTools);
            Assert.False(scenarios[1].RejectUnlistedToolCalls);
            Assert.Empty(scenarios[1].ForbiddenTools);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
