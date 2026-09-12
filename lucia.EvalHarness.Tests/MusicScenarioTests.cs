using System.Text.Json;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Providers;
using lucia.EvalHarness.Tests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.EvalHarness.Tests;

public sealed class MusicScenarioTests
{
    [Theory]
    [InlineData("play_artist_office", "Office Speaker", "media_player.zack_s_office_satellite1_media_player", "Zack's Office")]
    [InlineData("play_specific_song", "Office Speaker", "media_player.zack_s_office_satellite1_media_player", "Zack's Office")]
    [InlineData("shuffle_random_tracks", "Office Speaker", "media_player.zack_s_office_satellite1_media_player", "Zack's Office")]
    [InlineData("set_volume_50", "Office Speaker", "media_player.zack_s_office_satellite1_media_player", "Zack's Office")]
    [InlineData("volume_down", "Office Speaker", "media_player.zack_s_office_satellite1_media_player", "Zack's Office")]
    [InlineData("play_album_with_artist", "Bedroom Speaker", "media_player.bedroom_satellite1_media_player", "Bedroom")]
    [InlineData("play_genre_jazz", "Bedroom Speaker", "media_player.bedroom_satellite1_media_player", "Bedroom")]
    [InlineData("stop_music", "Bedroom Speaker", "media_player.bedroom_satellite1_media_player", "Bedroom")]
    [InlineData("volume_up", "Bedroom Speaker", "media_player.bedroom_satellite1_media_player", "Bedroom")]
    [InlineData("find_player_by_name", "Yamaha Speakers", "media_player.yamaha_speakers", "Living Room")]
    [InlineData("stt_fuzzy_play_cure_kitchen", "Kitchen Speaker", "media_player.kitchen_speaker", "Kitchen")]
    public async Task MusicDataset_RealFindPlayerResolvesTheScenarioSpeaker(
        string scenarioId, string playerName, string entityId, string areaName)
    {
        using var loggerFactory = LoggerFactory.Create(_ => { });
        await using var factory = new RealAgentFactory(
            "http://unused:11434",
            Path.Combine(AppContext.BaseDirectory, "TestData", "ha-snapshot.json"),
            loggerFactory)
        {
            ChatClientCreator = (_, _, _) => new CapturingChatClient()
        };
        var instance = await factory.CreateMusicAgentAsync("test-model");
        var scenario = ScenarioLoader.LoadFromFile(instance.DatasetFile)
            .Single(scenario => scenario.Id == scenarioId);
        await ScenarioValidator.SetupInitialStateAsync(
            factory.HomeAssistantClient, scenario, factory.EntityLocationService);
        var tools = Assert.IsType<lucia.MusicAgent.MusicAgent>(instance.Agent).Tools.OfType<AIFunction>().ToList();
        var findPlayer = tools.Single(tool => tool.Name == "FindPlayer");

        var response = await findPlayer.InvokeAsync(new AIFunctionArguments { ["playerName"] = playerName });
        var text = Assert.IsType<JsonElement>(response).GetString();

        Assert.Contains(entityId, text, StringComparison.Ordinal);
        Assert.Contains(playerName, text, StringComparison.Ordinal);
        Assert.Equal(areaName, factory.EntityLocationService.GetAreaForEntity(entityId)?.Name);

        var byId = await findPlayer.InvokeAsync(new AIFunctionArguments { ["playerName"] = entityId });
        Assert.Contains(entityId, Assert.IsType<JsonElement>(byId).GetString(), StringComparison.Ordinal);
    }
}
