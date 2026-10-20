using System.Collections.Immutable;
using System.Net;
using System.Text;
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
using lucia.Tests.TestDoubles;
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
        A.CallTo(() => _homeAssistantClient.CallServiceAsync("music_assistant", "play_media", null, A<ServiceCallRequest>._, A<CancellationToken>._))
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
    public async Task PlayAudiobookAsync_WithoutDirectApi_UsesHomeAssistant()
    {
        ServiceCallRequest? capturedRequest = null;
        A.CallTo(() => _homeAssistantClient.CallServiceAsync("music_assistant", "play_media", null, A<ServiceCallRequest>._, A<CancellationToken>._))
            .Invokes((string _, string _, string? _, ServiceCallRequest request, CancellationToken _) => capturedRequest = request)
            .Returns(Task.FromResult(Array.Empty<object>()));

        var response = await _skill.PlayAudiobookAsync("Satellite1 Kitchen", "Altered Carbon");

        Assert.NotNull(capturedRequest);
        Assert.Equal("media_player.satellite1_kitchen", capturedRequest!.EntityId);
        Assert.Equal("audiobook", capturedRequest["media_type"]);
        Assert.Equal("Altered Carbon", capturedRequest["media_id"]);
        Assert.Equal("replace", capturedRequest["enqueue"]);
        Assert.Contains("Playing audiobook 'Altered Carbon'", response);
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

    private void ThrowFromEveryServiceCall(Exception exception)
        => A.CallTo(() => _homeAssistantClient.CallServiceAsync(
                A<string>._, A<string>._, A<string?>._, A<ServiceCallRequest?>._, A<CancellationToken>._))
            .Throws(exception);

    [Fact]
    public async Task PlayArtistAsync_WhenHomeAssistantFails_ReturnsAGenericMessage()
    {
        ThrowFromEveryServiceCall(new InvalidOperationException("secret connection string"));
        await _skill.InitializeAsync();

        var response = await _skill.PlayArtistAsync("Satellite1 Kitchen", "Queen");

        Assert.Equal("Unable to start playback on 'Satellite1 Kitchen'. Please try again.", response);
    }

    [Fact]
    public async Task PlayArtistAsync_WhenCancelled_Propagates()
    {
        ThrowFromEveryServiceCall(new OperationCanceledException());
        await _skill.InitializeAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _skill.PlayArtistAsync("Satellite1 Kitchen", "Queen"));
    }

    [Fact]
    public async Task PlayShuffleAsync_WhenHomeAssistantFails_ReturnsAGenericMessage()
    {
        ThrowFromEveryServiceCall(new InvalidOperationException("secret connection string"));
        await _skill.InitializeAsync();

        var response = await _skill.PlayShuffleAsync("Satellite1 Kitchen", 10);

        Assert.Equal("Unable to start shuffle playback on 'Satellite1 Kitchen'. Please try again.", response);
    }

    [Fact]
    public async Task PlayShuffleAsync_WhenCancelled_Propagates()
    {
        ThrowFromEveryServiceCall(new OperationCanceledException());
        await _skill.InitializeAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _skill.PlayShuffleAsync("Satellite1 Kitchen", 10));
    }

    public static TheoryData<string, string> ControlFailures => new()
    {
        { "stop", "Unable to stop playback on 'Satellite1 Kitchen'. Please try again." },
        { "volume", "Unable to set the volume on 'Satellite1 Kitchen'. Please try again." },
        { "up", "Unable to turn up the volume on 'Satellite1 Kitchen'. Please try again." },
        { "down", "Unable to turn down the volume on 'Satellite1 Kitchen'. Please try again." },
    };

    private Task<string> InvokeControl(string tool) => tool switch
    {
        "stop" => _skill.StopMusicAsync("Satellite1 Kitchen"),
        "volume" => _skill.SetVolumeAsync("Satellite1 Kitchen", 40),
        "up" => _skill.VolumeUpAsync("Satellite1 Kitchen"),
        _ => _skill.VolumeDownAsync("Satellite1 Kitchen"),
    };

    [Theory]
    [MemberData(nameof(ControlFailures))]
    public async Task ControlTools_WhenHomeAssistantFails_ReturnAGenericMessage(string tool, string expected)
    {
        ThrowFromEveryServiceCall(new InvalidOperationException("secret connection string"));
        await _skill.InitializeAsync();

        Assert.Equal(expected, await InvokeControl(tool));
    }

    [Theory]
    [MemberData(nameof(ControlFailures))]
    public async Task ControlTools_WhenCancelled_Propagate(string tool, string _)
    {
        ThrowFromEveryServiceCall(new OperationCanceledException());
        await _skill.InitializeAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => InvokeControl(tool));
    }

    [Theory]
    [InlineData("stop", "Stopped playback on 'Satellite1 Kitchen'.")]
    [InlineData("volume", "Set volume to 40% on 'Satellite1 Kitchen'.")]
    [InlineData("up", "Turned up the volume on 'Satellite1 Kitchen'.")]
    [InlineData("down", "Turned down the volume on 'Satellite1 Kitchen'.")]
    public async Task ControlTools_ReportSuccess(string tool, string expected)
    {
        await _skill.InitializeAsync();

        Assert.Equal(expected, await InvokeControl(tool));
    }

    [Theory]
    [InlineData("artist")]
    [InlineData("album")]
    [InlineData("song")]
    [InlineData("genre")]
    public async Task PlayTools_WhenThePlayerLookupFails_ReturnAGenericMessage(string tool)
    {
        A.CallTo(() => _locationService.SearchHierarchyAsync(
                A<string>._, A<HybridMatchOptions?>._, A<IReadOnlyList<string>?>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("secret connection string"));

        var response = await InvokePlay(tool);

        Assert.Equal("Unable to start playback on 'Satellite1 Kitchen'. Please try again.", response);
    }

    [Theory]
    [InlineData("artist")]
    [InlineData("album")]
    [InlineData("song")]
    [InlineData("genre")]
    public async Task PlayTools_WhenTheirCallerCancels_Propagate(string tool)
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        A.CallTo(() => _locationService.SearchHierarchyAsync(
                A<string>._, A<HybridMatchOptions?>._, A<IReadOnlyList<string>?>._, A<CancellationToken>._))
            .Throws(new OperationCanceledException(cancelled.Token));

        await Assert.ThrowsAsync<OperationCanceledException>(() => InvokePlay(tool, cancelled.Token));
    }

    [Theory]
    [InlineData("artist", "PlayArtistAsync")]
    [InlineData("album", "PlayAlbumAsync")]
    [InlineData("song", "PlaySongAsync")]
    [InlineData("genre", "PlayGenreAsync")]
    public async Task PlayTools_EachGetTheirOwnSpan(string tool, string expectedName)
    {
        using var recorder = new ActivityRecorder("Lucia.Skills.MusicPlayback");
        var player = $"span-{tool}";

        await InvokePlay(tool, playerName: player);

        Assert.Equal(expectedName, recorder.WithTag("player.name", player)?.OperationName);
    }

    private Task<string> InvokePlay(string tool, CancellationToken cancellationToken = default, string playerName = "Satellite1 Kitchen") => tool switch
    {
        "artist" => _skill.PlayArtistAsync(playerName, "Queen", cancellationToken: cancellationToken),
        "album" => _skill.PlayAlbumAsync(playerName, "Jazz", cancellationToken: cancellationToken),
        "song" => _skill.PlaySongAsync(playerName, "Bohemian Rhapsody", cancellationToken: cancellationToken),
        _ => _skill.PlayGenreAsync(playerName, "Lo-fi", cancellationToken: cancellationToken),
    };

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
                A<string>._, A<string?>._, A<string?>._, A<IReadOnlyList<string>>._, A<string?>._, A<CancellationToken>._))
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
    public async Task FindPlayerAsync_WithDirectApi_PrefersMusicAssistantPlayers()
    {
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"player_id":"office","name":"Satellite1 Dev Office","display_name":"Satellite1 Dev Office","available":true}]""",
                    Encoding.UTF8,
                    "application/json")
            }));
        var musicConfig = A.Fake<IOptionsMonitor<MusicAssistantConfig>>();
        A.CallTo(() => musicConfig.CurrentValue).Returns(new MusicAssistantConfig
        {
            Url = "https://music.example.test",
            Token = "token"
        });
        var directClient = new MusicAssistantClient(
            httpClient,
            musicConfig,
            NullLogger<MusicAssistantClient>.Instance);
        var skillOptions = A.Fake<IOptionsMonitor<MusicPlaybackSkillOptions>>();
        A.CallTo(() => skillOptions.CurrentValue).Returns(new MusicPlaybackSkillOptions());
        var skill = new MusicPlaybackSkill(
            _homeAssistantClient,
            NullLogger<MusicPlaybackSkill>.Instance,
            _locationService,
            skillOptions,
            musicConfig,
            musicAssistantClient: directClient);

        var result = await skill.FindPlayerAsync("Satellite1 Dev Office");

        Assert.Contains("Satellite1 Dev Office", result);
        A.CallTo(() => _locationService.SearchHierarchyAsync(
                A<string>._, A<HybridMatchOptions>._, A<IReadOnlyList<string>>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task FindPlayerAsync_FallsBackToHierarchy_WhenCascadingResolverBails()
    {
        var cascadingResolver = A.Fake<ICascadingEntityResolver>();
        A.CallTo(() => cascadingResolver.Resolve(
                A<string>._, A<string?>._, A<string?>._, A<IReadOnlyList<string>>._, A<string?>._, A<CancellationToken>._))
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

    [Fact]
    public async Task NextTrackAsync_CallsMediaPlayerNext()
    {
        ServiceCallRequest? capturedRequest = null;
        A.CallTo(() => _homeAssistantClient.CallServiceAsync("media_player", "next", null, A<ServiceCallRequest>._, A<CancellationToken>._))
            .Invokes((string _, string _, string? _, ServiceCallRequest request, CancellationToken _) => capturedRequest = request)
            .Returns(Task.FromResult(Array.Empty<object>()));

        var response = await _skill.NextTrackAsync("Satellite1 Kitchen");

        Assert.NotNull(capturedRequest);
        Assert.Equal("media_player.satellite1_kitchen", capturedRequest!.EntityId);
        Assert.Contains("next track", response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreviousTrackAsync_CallsMediaPlayerPrevious()
    {
        ServiceCallRequest? capturedRequest = null;
        A.CallTo(() => _homeAssistantClient.CallServiceAsync("media_player", "previous", null, A<ServiceCallRequest>._, A<CancellationToken>._))
            .Invokes((string _, string _, string? _, ServiceCallRequest request, CancellationToken _) => capturedRequest = request)
            .Returns(Task.FromResult(Array.Empty<object>()));

        var response = await _skill.PreviousTrackAsync("Satellite1 Kitchen");

        Assert.NotNull(capturedRequest);
        Assert.Equal("media_player.satellite1_kitchen", capturedRequest!.EntityId);
        Assert.Contains("previous track", response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NextTrackAsync_ReturnsNotFoundMessage_WhenPlayerMissing()
    {
        var skill = CreateSkillWithEmptyLocationService();

        var response = await skill.NextTrackAsync("Nonexistent Speaker");

        Assert.Contains("couldn't find", response, StringComparison.OrdinalIgnoreCase);
        A.CallTo(() => _homeAssistantClient.CallServiceAsync("media_player", "next", null, A<ServiceCallRequest>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task GetNowPlayingAsync_ReportsTitleAndArtistWhenPlaying()
    {
        var state = new HomeAssistantState
        {
            EntityId = "media_player.satellite1_kitchen",
            State = "playing",
            Attributes = new Dictionary<string, object>
            {
                ["media_title"] = "The Hanging Garden",
                ["media_artist"] = "The Cure"
            }
        };
        A.CallTo(() => _homeAssistantClient.GetStateAsync("media_player.satellite1_kitchen", A<CancellationToken>._))
            .Returns(Task.FromResult<HomeAssistantState?>(state));

        var response = await _skill.GetNowPlayingAsync("Satellite1 Kitchen");

        Assert.Contains("The Hanging Garden", response);
        Assert.Contains("The Cure", response);
        Assert.Contains("playing", response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetNowPlayingAsync_NonMusicContentType_IncludesFriendlyQualifier()
    {
        var state = new HomeAssistantState
        {
            EntityId = "media_player.satellite1_kitchen",
            State = "playing",
            Attributes = new Dictionary<string, object>
            {
                ["media_title"] = "The Wire",
                ["media_content_type"] = "tv"
            }
        };
        A.CallTo(() => _homeAssistantClient.GetStateAsync("media_player.satellite1_kitchen", A<CancellationToken>._))
            .Returns(Task.FromResult<HomeAssistantState?>(state));

        var response = await _skill.GetNowPlayingAsync("Satellite1 Kitchen");

        Assert.Contains("Playing TV", response);
        Assert.Contains("The Wire", response);
    }

    [Fact]
    public async Task GetNowPlayingAsync_ReportsNothingPlayingWhenIdle()
    {
        var state = new HomeAssistantState
        {
            EntityId = "media_player.satellite1_kitchen",
            State = "idle",
            Attributes = new Dictionary<string, object>()
        };
        A.CallTo(() => _homeAssistantClient.GetStateAsync("media_player.satellite1_kitchen", A<CancellationToken>._))
            .Returns(Task.FromResult<HomeAssistantState?>(state));

        var response = await _skill.GetNowPlayingAsync("Satellite1 Kitchen");

        Assert.Contains("Nothing is playing", response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetNowPlayingAsync_ReturnsNotFoundMessage_WhenPlayerMissing()
    {
        var skill = CreateSkillWithEmptyLocationService();

        var response = await skill.GetNowPlayingAsync("Nonexistent Speaker");

        Assert.Contains("couldn't find", response, StringComparison.OrdinalIgnoreCase);
    }

    private MusicPlaybackSkill CreateSkillWithEmptyLocationService()
    {
        var emptyLocationService = A.Fake<IEntityLocationService>();
        A.CallTo(() => emptyLocationService.SearchHierarchyAsync(
                A<string>._,
                A<HybridMatchOptions?>._,
                A<IReadOnlyList<string>?>._,
                A<CancellationToken>._))
            .Returns(Task.FromResult(new HierarchicalSearchResult
            {
                FloorMatches = [],
                AreaMatches = [],
                EntityMatches = [],
                ResolvedEntities = [],
                ResolutionStrategy = ResolutionStrategy.None,
                ResolutionReason = "No matches"
            }));

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
            emptyLocationService,
            skillOptions,
            musicConfig);
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
