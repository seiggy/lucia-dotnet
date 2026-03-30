using System.Collections.Immutable;
using System.Text.Json;
using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.Agents.Services;
using lucia.Agents.Skills.Models;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using lucia.MusicAgent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests;

public class MusicPlaybackSkillTests
{
    private readonly IHomeAssistantClient _homeAssistantClient = A.Fake<IHomeAssistantClient>();
    private readonly IEntityLocationService _locationService = A.Fake<IEntityLocationService>();
    private readonly MusicPlaybackSkill _skill;

    public MusicPlaybackSkillTests()
    {
        ConfigureDefaultLocationService();

        var musicConfig = A.Fake<IOptionsMonitor<MusicAssistantConfig>>();
        A.CallTo(() => musicConfig.CurrentValue).Returns(new MusicAssistantConfig
        {
            IntegrationId = "DEMO"
        });

        var skillOptions = A.Fake<IOptionsMonitor<MusicPlaybackSkillOptions>>();
        A.CallTo(() => skillOptions.CurrentValue).Returns(new MusicPlaybackSkillOptions());

        _skill = new MusicPlaybackSkill(
            _homeAssistantClient,
            NullLogger<MusicPlaybackSkill>.Instance,
            _locationService,
            skillOptions,
            musicConfig);
    }

    [Fact]
    public async Task InitializeAsync_FindsPlayer_ViaLocationService()
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

    private void ConfigureDefaultLocationService()
    {
        var kitchenPlayer = new HomeAssistantEntity
        {
            EntityId = "media_player.satellite1_kitchen",
            FriendlyName = "Satellite1 Kitchen"
        };

        var searchResult = new HierarchicalSearchResult
        {
            FloorMatches = [],
            AreaMatches = [],
            EntityMatches = [],
            ResolvedEntities = [kitchenPlayer],
            ResolutionStrategy = ResolutionStrategy.Entity,
            ResolutionReason = "Direct entity match"
        };

        A.CallTo(() => _locationService.SearchHierarchyAsync(
                A<string>._,
                A<HybridMatchOptions?>._,
                A<IReadOnlyList<string>?>._,
                A<CancellationToken>._))
            .Returns(Task.FromResult(searchResult));
    }

    [Fact]
    public async Task FindPlayerAsync_UsesCascadingResolver_WhenAvailable()
    {
        var cascadingResolver = A.Fake<ICascadingEntityResolver>();
        A.CallTo(() => cascadingResolver.Resolve(
                A<string>._, A<string?>._, A<string?>._, A<IReadOnlyList<string>>._, A<CancellationToken>._))
            .Returns(new CascadeResult
            {
                IsResolved = true,
                ResolvedEntityIds = ["media_player.satellite1_kitchen"]
            });

        var snapshot = BuildSnapshotWithPlayer("media_player.satellite1_kitchen", "Satellite1 Kitchen");
        A.CallTo(() => _locationService.GetSnapshot()).Returns(snapshot);

        var skill = CreateSkillWithResolver(cascadingResolver);

        var result = await skill.FindPlayerAsync("Kitchen Speaker");

        Assert.Contains("Satellite1 Kitchen", result);
        A.CallTo(() => _locationService.SearchHierarchyAsync(
                A<string>._, A<HybridMatchOptions>._, A<IReadOnlyList<string>>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task FindPlayerAsync_FallsBackToHierarchy_WhenCascadingResolverBails()
    {
        var cascadingResolver = A.Fake<ICascadingEntityResolver>();
        A.CallTo(() => cascadingResolver.Resolve(
                A<string>._, A<string?>._, A<string?>._, A<IReadOnlyList<string>>._, A<CancellationToken>._))
            .Returns(new CascadeResult
            {
                IsResolved = false,
                BailReason = BailReason.Ambiguous,
                Explanation = "Multiple matches",
                ResolvedEntityIds = []
            });

        var skill = CreateSkillWithResolver(cascadingResolver);

        var result = await skill.FindPlayerAsync("Kitchen Speaker");

        A.CallTo(() => _locationService.SearchHierarchyAsync(
                A<string>._, A<HybridMatchOptions>._, A<IReadOnlyList<string>>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    private MusicPlaybackSkill CreateSkillWithResolver(ICascadingEntityResolver resolver)
    {
        var musicConfig = A.Fake<IOptionsMonitor<MusicAssistantConfig>>();
        A.CallTo(() => musicConfig.CurrentValue).Returns(new MusicAssistantConfig
        {
            IntegrationId = "DEMO"
        });

        var skillOptions = A.Fake<IOptionsMonitor<MusicPlaybackSkillOptions>>();
        A.CallTo(() => skillOptions.CurrentValue).Returns(new MusicPlaybackSkillOptions());

        return new MusicPlaybackSkill(
            _homeAssistantClient,
            NullLogger<MusicPlaybackSkill>.Instance,
            _locationService,
            skillOptions,
            musicConfig,
            resolver);
    }

    private static LocationSnapshot BuildSnapshotWithPlayer(string entityId, string friendlyName)
    {
        var entity = new HomeAssistantEntity
        {
            EntityId = entityId,
            FriendlyName = friendlyName
        };

        return new LocationSnapshot(
            ImmutableArray<FloorInfo>.Empty,
            ImmutableArray<AreaInfo>.Empty,
            ImmutableArray.Create(entity),
            ImmutableDictionary<string, FloorInfo>.Empty,
            ImmutableDictionary<string, AreaInfo>.Empty,
            ImmutableDictionary<string, HomeAssistantEntity>.Empty.Add(entityId, entity));
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
