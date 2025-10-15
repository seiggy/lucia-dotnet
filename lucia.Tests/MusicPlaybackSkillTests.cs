using System.Linq;
using System.Text.Json;
using FakeItEasy;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using lucia.Agents.Models;
using lucia.Agents.Skills;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using lucia.Tests.TestDoubles;

namespace lucia.Tests;

public class MusicPlaybackSkillTests
{
    private readonly IHomeAssistantClient _homeAssistantClient = A.Fake<IHomeAssistantClient>();
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator = new StubEmbeddingGenerator();
    private readonly MusicPlaybackSkill _skill;

    public MusicPlaybackSkillTests()
    {
        ConfigureDefaultStates();
        _skill = new MusicPlaybackSkill(_homeAssistantClient, _embeddingGenerator, NullLogger<MusicPlaybackSkill>.Instance);
    }

    [Fact]
    public async Task InitializeAsync_PrimesCache_AndFindsPlayer()
    {
        await _skill.InitializeAsync();

        var result = await _skill.FindPlayerAsync("Satellite1 Kitchen");

        Assert.Contains("Satellite player 'Satellite1 Kitchen'", result);
    }

    [Fact]
    public async Task PlayArtistAsync_CallsMusicAssistantPlayMedia()
    {
        ServiceCallRequest? capturedRequest = null;
        A.CallTo(() => _homeAssistantClient.CallServiceAsync("music_assistant", "play_media", A<ServiceCallRequest>._, A<CancellationToken>._))
            .Invokes((string _, string _, ServiceCallRequest request, CancellationToken _) => capturedRequest = request)
            .Returns(Task.FromResult(Array.Empty<object>()));

        await _skill.InitializeAsync();
        var response = await _skill.PlayArtistAsync("Satellite1 Kitchen", "Queen");

        Assert.NotNull(capturedRequest);
        Assert.Equal("media_player.satellite1_kitchen", capturedRequest!.EntityId);
        Assert.Equal("artist", capturedRequest["media_type"]);
        Assert.Equal("Queen", capturedRequest["media_id"]);
        Assert.Equal("replace", capturedRequest["enqueue"]);
        Assert.Contains("Started an artist station", response);
    }

    [Fact]
    public async Task PlayShuffleAsync_SeedsQueueWithRandomTracks()
    {
        var libraryResponse = CreateLibraryResponseJson();
        A.CallTo(() => _homeAssistantClient.CallServiceAsync("music_assistant", "get_library", A<ServiceCallRequest>._, A<CancellationToken>._))
            .Returns(Task.FromResult(new object[] { libraryResponse }));

        ServiceCallRequest? capturedPlayRequest = null;
        A.CallTo(() => _homeAssistantClient.CallServiceAsync("music_assistant", "play_media", A<ServiceCallRequest>._, A<CancellationToken>._))
            .Invokes((string _, string _, ServiceCallRequest request, CancellationToken _) => capturedPlayRequest = request)
            .Returns(Task.FromResult(Array.Empty<object>()));

        await _skill.InitializeAsync();
        var response = await _skill.PlayShuffleAsync("Satellite1 Kitchen", 10);

        Assert.NotNull(capturedPlayRequest);
        var mediaIds = Assert.IsAssignableFrom<IEnumerable<object>>(capturedPlayRequest!["media_id"]);
        Assert.Contains("spotify://track/123", mediaIds.Cast<string>());
        Assert.Equal("track", capturedPlayRequest["media_type"]);
        Assert.Equal("replace", capturedPlayRequest["enqueue"]);
        Assert.Equal(true, capturedPlayRequest["radio_mode"]);
        Assert.Contains("shuffle mix", response);
    }

    private void ConfigureDefaultStates()
    {
        var state = new HomeAssistantState
        {
            EntityId = "media_player.satellite1_kitchen",
            State = "idle",
            Attributes = new Dictionary<string, object>
            {
                ["friendly_name"] = "Satellite1 Kitchen",
                ["config_entry_id"] = "Music Assistant",
                ["music_assistant_player_id"] = "ma_player_1"
            }
        };

        A.CallTo(() => _homeAssistantClient.GetStatesAsync(A<CancellationToken>._))
            .Returns(Task.FromResult<IEnumerable<HomeAssistantState>>(new[] { state }));
    }

    private static JsonElement CreateLibraryResponseJson()
    {
                const string json = """
                {
                    "items": [
                        {"uri": "spotify://track/123"},
                        {"uri": "spotify://track/456"}
                    ]
                }
                """;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
