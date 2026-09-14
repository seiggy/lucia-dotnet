using System.Text.Json;
using lucia.Agents.Agents;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Providers;
using lucia.EvalHarness.Tests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.EvalHarness.Tests;

public sealed class SceneScenarioTests
{
    [Theory]
    [InlineData("activate_movie", "ListScenes", null, "scene.movie_mode")]
    [InlineData("activate_by_area", "FindScenesByArea", "bedroom", "scene.romantic")]
    [InlineData("activate_goodnight", "ListScenes", null, "scene.goodnight")]
    [InlineData("list_scenes", "ListScenes", null, "scene.movie_mode")]
    public async Task SceneDataset_RealToolsDiscoverTheRequestedScene(
        string scenarioId, string toolName, string? areaName, string entityId)
    {
        using var loggerFactory = LoggerFactory.Create(_ => { });
        await using var factory = new RealAgentFactory(
            "http://unused:11434",
            Path.Combine(AppContext.BaseDirectory, "TestData", "ha-snapshot.json"),
            loggerFactory)
        {
            ChatClientCreator = (_, _, _) => new CapturingChatClient()
        };
        var instance = await factory.CreateSceneAgentAsync("test-model");
        var scenario = ScenarioLoader.LoadFromFile(instance.DatasetFile)
            .Single(scenario => scenario.Id == scenarioId);
        await ScenarioValidator.SetupInitialStateAsync(
            factory.HomeAssistantClient, scenario, factory.EntityLocationService);

        var tools = Assert.IsType<SceneAgent>(instance.Agent).Tools.OfType<AIFunction>().ToList();
        var lookup = tools.Single(tool => tool.Name == toolName);
        var arguments = new AIFunctionArguments();
        if (areaName is not null)
        {
            arguments["areaName"] = areaName;
        }

        var result = await lookup.InvokeAsync(arguments);
        var text = Assert.IsType<JsonElement>(result).GetString();
        Assert.NotNull(text);
        Assert.Contains(entityId, text, StringComparison.Ordinal);

        if (areaName is not null)
        {
            Assert.DoesNotContain("scene.movie_mode", text, StringComparison.Ordinal);
            Assert.Equal("Bedroom", factory.EntityLocationService.GetAreaForEntity(entityId)?.Name);
        }

        if (scenarioId == "list_scenes")
        {
            Assert.Contains("scene.romantic", text, StringComparison.Ordinal);
            Assert.Contains("scene.goodnight", text, StringComparison.Ordinal);
        }
        else
        {
            var expectedActivation = scenario.ExpectedToolCalls.Single(call => call.Tool == "ActivateScene");
            Assert.Equal(entityId, expectedActivation.Arguments["entityId"]);
            var activation = tools.Single(tool => tool.Name == "ActivateScene");
            var response = await activation.InvokeAsync(new AIFunctionArguments { ["entityId"] = entityId });
            var activationText = Assert.IsType<JsonElement>(response).GetString();
            Assert.Contains(entityId, activationText, StringComparison.Ordinal);
            Assert.Contains("activated successfully", activationText, StringComparison.Ordinal);
        }
    }
}
