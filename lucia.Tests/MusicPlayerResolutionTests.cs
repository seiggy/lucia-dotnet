using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using lucia.MusicAgent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests;

/// <summary>
/// A room name can match several media players, such as a monitor and a speaker.
/// Transport and now-playing tools should act on the player that is making sound.
/// </summary>
public sealed class MusicPlayerResolutionTests
{
    private const string Monitor = "media_player.office_monitor";
    private const string Speaker = "media_player.office_speaker";

    private readonly IHomeAssistantClient _homeAssistant = A.Fake<IHomeAssistantClient>();
    private readonly MusicPlaybackSkill _skill;

    public MusicPlayerResolutionTests()
    {
        var locationService = A.Fake<IEntityLocationService>();
        A.CallTo(() => locationService.SearchHierarchyAsync(
                A<string>._, A<HybridMatchOptions?>._, A<IReadOnlyList<string>?>._, A<CancellationToken>._))
            .Returns(new HierarchicalSearchResult
            {
                FloorMatches = [],
                AreaMatches = [],
                EntityMatches = [],
                ResolvedEntities =
                [
                    new HomeAssistantEntity { EntityId = Monitor, FriendlyName = "49 Odyssey Monitor" },
                    new HomeAssistantEntity { EntityId = Speaker, FriendlyName = "Office Speaker" }
                ],
                ResolutionStrategy = ResolutionStrategy.Area,
                ResolutionReason = "Area match"
            });
        SetState(Monitor, "idle");
        SetState(Speaker, "playing", ("media_title", "Close to Me"), ("media_artist", "The Cure"));

        var config = A.Fake<IOptionsMonitor<MusicAssistantConfig>>();
        A.CallTo(() => config.CurrentValue).Returns(new MusicAssistantConfig());
        var options = A.Fake<IOptionsMonitor<MusicPlaybackSkillOptions>>();
        A.CallTo(() => options.CurrentValue).Returns(new MusicPlaybackSkillOptions());
        _skill = new MusicPlaybackSkill(
            _homeAssistant,
            NullLogger<MusicPlaybackSkill>.Instance,
            locationService,
            options,
            config);
    }

    [Fact]
    public async Task GetNowPlayingAsync_RoomMatchesIdleAndPlayingPlayers_ReportsThePlayingOne()
    {
        var result = await _skill.GetNowPlayingAsync("Zack's Office");

        Assert.Contains("Close to Me", result);
        Assert.Contains("Office Speaker", result);
    }

    [Fact]
    public async Task NextTrackAsync_RoomMatchesIdleAndPlayingPlayers_SkipsOnThePlayingOne()
    {
        await _skill.NextTrackAsync("Zack's Office");

        A.CallTo(() => _homeAssistant.CallServiceAsync(
                "media_player", "next", A<string?>._,
                A<ServiceCallRequest>.That.Matches(request => request.EntityId == Speaker),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task NextTrackAsync_PausedPlayerListedFirst_PrefersThePlayingOne()
    {
        SetState(Monitor, "paused");

        await _skill.NextTrackAsync("Zack's Office");

        A.CallTo(() => _homeAssistant.CallServiceAsync(
                "media_player", "next", A<string?>._,
                A<ServiceCallRequest>.That.Matches(request => request.EntityId == Speaker),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task FindPlayerAsync_RoomMatchesSeveralPlayers_ListsEveryCandidate()
    {
        var result = await _skill.FindPlayerAsync("Zack's Office");

        Assert.Contains("49 Odyssey Monitor", result);
        Assert.Contains("Office Speaker", result);
    }

    [Fact]
    public async Task PlayArtistAsync_RoomMatchesSeveralPlayers_KeepsTheFirstMatch()
    {
        await _skill.PlayArtistAsync("Zack's Office", "The Cure");

        A.CallTo(() => _homeAssistant.CallServiceAsync(
                "music_assistant", "play_media", A<string?>._,
                A<ServiceCallRequest>.That.Matches(request => request.EntityId == Monitor),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    private void SetState(string entityId, string state, params (string Key, string Value)[] attributes)
    {
        var value = new HomeAssistantState { EntityId = entityId, State = state };
        foreach (var (key, attribute) in attributes)
        {
            value.Attributes[key] = attribute;
        }

        A.CallTo(() => _homeAssistant.GetStateAsync(entityId, A<CancellationToken>._))
            .Returns(Task.FromResult<HomeAssistantState?>(value));
    }
}
