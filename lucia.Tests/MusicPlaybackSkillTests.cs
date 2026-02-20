using System.Linq;
using System.Text.Json;
using FakeItEasy;
using lucia.Agents.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using lucia.Agents.Models;
using lucia.Agents.Skills;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using lucia.Agents.Services;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.Options;
using lucia.Agents.Skills.Models;
using lucia.MusicAgent;

namespace lucia.Tests;

public class MusicPlaybackSkillTests
{
    private readonly IHomeAssistantClient _homeAssistantClient = A.Fake<IHomeAssistantClient>();
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator = new StubEmbeddingGenerator();
    private readonly IDeviceCacheService _deviceCache = A.Fake<IDeviceCacheService>();
    private readonly MusicPlaybackSkill _skill;

    public MusicPlaybackSkillTests()
    {
        ConfigureDefaultStates();
        var options = Options.Create<MusicAssistantConfig>(new MusicAssistantConfig()
        {
            IntegrationId = "DEMO"
        });
        
        _skill = new MusicPlaybackSkill(
            _homeAssistantClient,
            options,
            _embeddingGenerator,
            _deviceCache,
            NullLogger<MusicPlaybackSkill>.Instance);
    }

    [Fact]
    public async Task InitializeAsync_PrimesCache_AndFindsPlayer()
    {
        await _skill.InitializeAsync();

        var result = await _skill.FindPlayerAsync("Satellite1 Kitchen");

        Assert.Contains("MusicAssistant player 'Satellite1 Kitchen'", result);
    }

    [Fact]
    public async Task PlayArtistAsync_CallsMusicAssistantPlayMedia()
    {
        ServiceCallRequest? capturedRequest = null;
        A.CallTo(() => _homeAssistantClient.CallServiceAsync("music_assistant", "play_media", "return_response=1", A<ServiceCallRequest>._, A<CancellationToken>._))
            .Invokes((string _, string _, string? _, ServiceCallRequest request, CancellationToken _) => capturedRequest = request)
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
        var libraryResponse = new MusicLibraryResponse
        {
            ServiceResponse = new MusicLibraryServiceResponse
            {
                Items = [
                    new LibraryItems { MediaType = "track", Uri = "spotify://track/123", Name = "Track 1" },
                    new LibraryItems { MediaType = "track", Uri = "spotify://track/456", Name = "Track 2" }
                ]
            }
        };
        A.CallTo(() => _homeAssistantClient.CallServiceAsync<MusicLibraryResponse>(A<string>._, A<string>._, A<string?>._, A<ServiceCallRequest?>._, A<CancellationToken>._))
            .Returns(Task.FromResult(libraryResponse));

        ServiceCallRequest? capturedPlayRequest = null;
        A.CallTo(() => _homeAssistantClient.CallServiceAsync("music_assistant", "play_media", null, A<ServiceCallRequest>._, A<CancellationToken>._))
            .Invokes((string _, string _, string? _, ServiceCallRequest request, CancellationToken _) => capturedPlayRequest = request)
            .Returns(Task.FromResult(Array.Empty<object>()));

        await _skill.InitializeAsync();
        var response = await _skill.PlayShuffleAsync("Satellite1 Kitchen", 10);

        Assert.NotNull(capturedPlayRequest);
        var mediaIds = Assert.IsAssignableFrom<IEnumerable<object>>(capturedPlayRequest!["media_id"]);
        Assert.Contains("spotify://track/123", mediaIds.Cast<string>());
        Assert.Equal("track", capturedPlayRequest["media_type"]);
        Assert.Equal("replace", capturedPlayRequest["enqueue"]);
        Assert.Contains("Shuffling", response);
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

        A.CallTo(() => _homeAssistantClient.GetAllEntityStatesAsync(A<CancellationToken>._))
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
