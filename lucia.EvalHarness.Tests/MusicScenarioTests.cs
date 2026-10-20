using System.Text.Json;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Providers;
using lucia.EvalHarness.Tests.TestDoubles;
using lucia.Agents.Abstractions;
using lucia.EvalHarness.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using FakeItEasy;

namespace lucia.EvalHarness.Tests;

public sealed class MusicScenarioTests
{
    [Fact]
    public void MusicDataset_OnlyFindPlayerScenarioRequiresFindPlayer()
    {
        var scenarios = ScenarioLoader.LoadFromFile(
            Path.Combine(AppContext.BaseDirectory, "TestData", "music-agent.yaml"));

        var required = scenarios
            .Where(static scenario => scenario.ExpectedToolCalls.Any(static call =>
                call.Tool == "FindPlayer" && !call.Optional))
            .Select(static scenario => scenario.Id);
        var optional = scenarios
            .Where(static scenario => scenario.ExpectedToolCalls.Any(static call =>
                call.Tool == "FindPlayer" && call.Optional))
            .Select(static scenario => scenario.Id)
            .ToHashSet();

        Assert.Equal(["find_player_by_name"], required);
        Assert.Subset(optional, new HashSet<string> { "next_track", "stop_music", "get_now_playing", "set_volume_50" });
    }

    [Fact]
    public async Task RealAgentFactory_ExposesDirectAndOrchestratedMusicTargets()
    {
        using var loggerFactory = LoggerFactory.Create(_ => { });
        await using var factory = new RealAgentFactory(
            "http://unused:11434",
            Path.Combine(AppContext.BaseDirectory, "TestData", "ha-snapshot.json"),
            loggerFactory)
        {
            EnableTracing = true,
            ChatClientCreator = (_, _, _) => new CapturingChatClient()
        };

        Assert.Contains("MusicAgent", factory.AgentFactories.Keys);
        Assert.Contains("MusicAgentOrchestrated", factory.AgentFactories.Keys);

        var instance = await factory.CreateMusicAgentOrchestratedAsync("test-model");
        Assert.NotNull(instance.ScenarioExecutor);
        Assert.NotNull(instance.RoutedAgentId);
        Assert.NotNull(instance.ResetScenarioState);
        Assert.NotNull(instance.Tracer);
    }

    [Fact]
    public void MusicDataset_CoversEveryMusicTool()
    {
        var scenarios = ScenarioLoader.LoadFromFile(
            Path.Combine(AppContext.BaseDirectory, "TestData", "music-agent.yaml"));
        var coveredTools = scenarios
            .SelectMany(static scenario => scenario.ExpectedToolCalls)
            .Select(static call => call.Tool)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] expectedTools =
        [
            "FindPlayer",
            "PlayArtist",
            "PlayAlbum",
            "PlaySong",
            "PlayGenre",
            "PlayAudiobook",
            "PlayShuffle",
            "StopMusic",
            "NextTrack",
            "PreviousTrack",
            "GetNowPlaying",
            "SetVolume",
            "VolumeUp",
            "VolumeDown"
        ];

        Assert.Subset(coveredTools, expectedTools.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OrchestratedScenario_WhenRouterSelectsWrongAgent_ReportsOnlyRoutingFailure()
    {
        var tracer = new ConversationTracer(new CapturingChatClient());
        var agent = A.Fake<ILuciaAgent>();
        var instance = new RealAgentInstance
        {
            AgentName = "MusicAgentOrchestrated",
            Agent = agent,
            DatasetFile = "TestData/music-agent.yaml",
            Tracer = tracer,
            ScenarioExecutor = (_, _) => Task.FromResult("Wrong agent response"),
            RoutedAgentId = () => "light-agent"
        };
        var scenario = new TestScenario
        {
            Id = "misrouted_audiobook",
            UserPrompt = "Resume Altered Carbon in the office",
            ExpectedAgent = "music",
            ExpectedToolCalls =
            [
                new ExpectedToolCall { Tool = "PlayAudiobook" }
            ]
        };
        var runner = new EvalRunner(new HarnessConfiguration(), judgeChatClient: null);

        var result = await runner.EvaluateScenariosAsync(
            "test-model",
            instance,
            [scenario],
            A.Fake<lucia.HomeAssistant.Services.IHomeAssistantClient>());

        var testCase = Assert.Single(result.TestCaseResults);
        Assert.False(testCase.Passed);
        Assert.Contains("Routing failed", testCase.FailureReason);
        Assert.DoesNotContain("Expected 1 tool", testCase.FailureReason);
    }

    [Fact]
    public async Task OrchestratedScenario_WithoutExpectedAgent_IsSkippedByTheSharedEvaluator()
    {
        var instance = new RealAgentInstance
        {
            AgentName = "MusicAgentOrchestrated",
            Agent = A.Fake<ILuciaAgent>(),
            DatasetFile = "TestData/music-agent.yaml",
            Tracer = new ConversationTracer(new CapturingChatClient()),
            ScenarioExecutor = (_, _) => Task.FromResult("ok"),
            RoutedAgentId = () => "music"
        };
        var scenario = new TestScenario { Id = "unlabelled", UserPrompt = "Turn on the lights" };
        var runner = new EvalRunner(new HarnessConfiguration(), judgeChatClient: null);

        var result = await runner.EvaluateScenariosAsync(
            "test-model",
            instance,
            [scenario],
            A.Fake<lucia.HomeAssistant.Services.IHomeAssistantClient>());

        Assert.Empty(result.TestCaseResults);
    }

    [Theory]
    [InlineData("play_artist_office", "Office Speaker", "media_player.zack_s_office_satellite1_media_player", "Zack's Office")]
    [InlineData("play_specific_song", "Office Speaker", "media_player.zack_s_office_satellite1_media_player", "Zack's Office")]
    [InlineData("play_audiobook", "Office Speaker", "media_player.zack_s_office_satellite1_media_player", "Zack's Office")]
    [InlineData("shuffle_random_tracks", "Office Speaker", "media_player.zack_s_office_satellite1_media_player", "Zack's Office")]
    [InlineData("next_track", "Office Speaker", "media_player.zack_s_office_satellite1_media_player", "Zack's Office")]
    [InlineData("get_now_playing", "Office Speaker", "media_player.zack_s_office_satellite1_media_player", "Zack's Office")]
    [InlineData("set_volume_50", "Office Speaker", "media_player.zack_s_office_satellite1_media_player", "Zack's Office")]
    [InlineData("volume_down", "Office Speaker", "media_player.zack_s_office_satellite1_media_player", "Zack's Office")]
    [InlineData("play_album_with_artist", "Bedroom Speaker", "media_player.bedroom_satellite1_media_player", "Bedroom")]
    [InlineData("play_genre_jazz", "Bedroom Speaker", "media_player.bedroom_satellite1_media_player", "Bedroom")]
    [InlineData("stop_music", "Bedroom Speaker", "media_player.bedroom_satellite1_media_player", "Bedroom")]
    [InlineData("previous_track", "Bedroom Speaker", "media_player.bedroom_satellite1_media_player", "Bedroom")]
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
