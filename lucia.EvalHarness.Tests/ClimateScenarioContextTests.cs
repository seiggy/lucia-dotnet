using System.Text.Json;
using lucia.Agents.Agents;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Providers;
using lucia.EvalHarness.Tests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.EvalHarness.Tests;

public sealed class ClimateScenarioContextTests
{
    [Theory]
    [InlineData("set_temperature_72", "living room", "climate.living_room_thermostat", 72d)]
    [InlineData("set_bedroom_temp_70", "bedroom", "climate.bedroom_thermostat", 70d)]
    public async Task SetTemperatureScenario_RealAreaToolFindsAndControlsTheRequestedThermostat(
        string scenarioId, string areaName, string entityId, double temperature)
    {
        var scenario = ScenarioLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "climate-agent.yaml"))
            .Single(scenario => scenario.Id == scenarioId);
        using var loggerFactory = LoggerFactory.Create(_ => { });
        await using var factory = new RealAgentFactory(
            "http://unused:11434",
            Path.Combine(AppContext.BaseDirectory, "TestData", "ha-snapshot.json"),
            loggerFactory)
        {
            ChatClientCreator = (_, _, _) => new CapturingChatClient()
        };
        var instance = await factory.CreateClimateAgentAsync("test-model");
        await ScenarioValidator.SetupInitialStateAsync(
            factory.HomeAssistantClient, scenario, factory.EntityLocationService);
        var tool = Assert.IsType<ClimateAgent>(instance.Agent).Tools.OfType<AIFunction>()
            .Single(tool => tool.Name == "FindClimateDevicesByArea");

        var result = await tool.InvokeAsync(new AIFunctionArguments { ["areaName"] = areaName });

        var text = Assert.IsType<JsonElement>(result).GetString();
        Assert.NotNull(text);
        Assert.Contains(entityId, text, StringComparison.Ordinal);

        var setTemperature = Assert.IsType<ClimateAgent>(instance.Agent).Tools.OfType<AIFunction>()
            .Single(tool => tool.Name == "SetClimateTemperature");
        await setTemperature.InvokeAsync(new AIFunctionArguments
        {
            ["entityId"] = entityId,
            ["temperature"] = temperature
        });

        var target = await factory.HomeAssistantClient.GetEntityStateAsync(entityId);
        Assert.NotNull(target);
        Assert.Equal(temperature, Convert.ToDouble(target.Attributes["temperature"], System.Globalization.CultureInfo.InvariantCulture));

        foreach (var (otherId, expected) in scenario.ExpectedFinalState.Where(pair => pair.Key != entityId))
        {
            Assert.DoesNotContain(otherId, text, StringComparison.Ordinal);
            var other = await factory.HomeAssistantClient.GetEntityStateAsync(otherId);
            Assert.NotNull(other);
            Assert.Equal(expected.State, other.State);
            foreach (var (attribute, value) in expected.Attributes ?? [])
            {
                Assert.Equal(value, Convert.ToString(other.Attributes[attribute], System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }

    [Theory]
    [InlineData("FindClimateDevicesByArea", "areaName", "Living Room", "climate.living_room_thermostat", true)]
    [InlineData("FindClimateDevice", "searchTerm", "living room thermostat", "climate.living_room_thermostat", true)]
    [InlineData("FindClimateDevice", "searchTerm", "thermostat", "climate.living_room_thermostat", false)]
    [InlineData("FindClimateDevicesByArea", "areaName", "Living Room", "climate.bedroom_thermostat", false)]
    public async Task ContextAwareScenario_AcceptsEquivalentSearchesButRejectsWrongTargets(
        string tool, string argument, string search, string target, bool expectedPass)
    {
        var scenario = ScenarioLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "climate-agent.yaml"))
            .Single(scenario => scenario.Id == "set_temperature_72");
        using var loggerFactory = LoggerFactory.Create(_ => { });
        await using var factory = new RealAgentFactory(
            "http://unused:11434",
            Path.Combine(AppContext.BaseDirectory, "TestData", "ha-snapshot.json"),
            loggerFactory);
        await ScenarioValidator.SetupInitialStateAsync(factory.HomeAssistantClient, scenario, factory.EntityLocationService);
        await factory.HomeAssistantClient.SetEntityStateAsync(
            target, target.Contains("bedroom", StringComparison.Ordinal) ? "cool" : "heat",
            new Dictionary<string, object> { ["temperature"] = 72 });

        var result = await ScenarioValidator.ValidateAsync(scenario,
        [
            new ConversationTurn
            {
                Role = "assistant",
                ToolCalls =
                [
                    new ToolCallInfo { Name = tool, Arguments = new() { [argument] = search } },
                    new ToolCallInfo { Name = "SetClimateTemperature", Arguments = new() { ["entityId"] = target, ["temperature"] = "72" } }
                ]
            },
            new ConversationTurn { Role = "assistant", Content = "Set to 72 degrees." }
        ], factory.HomeAssistantClient);

        Assert.Equal(expectedPass, result.Passed);
    }

    [Fact]
    public async Task ScenarioAreaAssignment_MakesUnqualifiedEntitiesDiscoverableAndUpdatesBetweenScenarios()
    {
        using var loggerFactory = LoggerFactory.Create(_ => { });
        await using var factory = new RealAgentFactory(
            "http://unused:11434",
            Path.Combine(AppContext.BaseDirectory, "TestData", "ha-snapshot.json"),
            loggerFactory);
        foreach (var area in new[] { "Living Room", "Bedroom" })
        {
            await ScenarioValidator.SetupInitialStateAsync(factory.HomeAssistantClient, new TestScenario
            {
                Id = "room-assignment",
                UserPrompt = "Set the thermostat.",
                InitialState = new()
                {
                    ["climate.zone_a"] = new EntitySetup
                    {
                        State = "heat",
                        Area = area,
                        Attributes = new() { ["friendly_name"] = "Main thermostat" }
                    }
                }
            }, factory.EntityLocationService);
            var entities = await factory.EntityLocationService.FindEntitiesByLocationAsync(area, ["climate"]);
            Assert.Contains(entities, entity => entity.EntityId == "climate.zone_a");
        }

        var originalRoom = await factory.EntityLocationService.FindEntitiesByLocationAsync("Living Room", ["climate"]);
        Assert.DoesNotContain(originalRoom, entity => entity.EntityId == "climate.zone_a");
    }

    [Fact]
    public async Task ImplicitThermostatScenario_SendsRequestAreaToRealClimateAgent()
    {
        var scenario = ScenarioLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "climate-agent.yaml"))
            .Single(scenario => scenario.Id == "set_temperature_72");
        var client = new CapturingChatClient();
        using var loggerFactory = LoggerFactory.Create(_ => { });
        await using var factory = new RealAgentFactory(
            "http://unused:11434",
            Path.Combine(AppContext.BaseDirectory, "TestData", "ha-snapshot.json"),
            loggerFactory)
        {
            EnableTracing = true,
            ChatClientCreator = (_, _, _) => client
        };
        var agent = await factory.CreateClimateAgentAsync("test-model");

        await new EvalRunner(new HarnessConfiguration(), null).EvaluateScenariosAsync(
            "test-model", agent, [scenario], factory.HomeAssistantClient, factory.EntityLocationService);

        var prompt = string.Join("\n", client.CapturedMessages
            .Where(message => message.Role == ChatRole.User).Select(message => message.Text));
        Assert.Contains("Device Area: Living Room", prompt, StringComparison.Ordinal);
        Assert.Contains("Set the thermostat to 72 degrees", prompt, StringComparison.Ordinal);
        Assert.Equal("Living Room", factory.EntityLocationService.GetAreaForEntity("climate.living_room_thermostat")?.Name);
    }
}
