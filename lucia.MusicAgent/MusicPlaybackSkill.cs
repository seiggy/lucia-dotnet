using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using lucia.Agents.Abstractions;
using lucia.Agents.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.Agents.Skills;
using lucia.Agents.Skills.Models;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.Options;

namespace lucia.MusicAgent;

/// <summary>
/// Controls Music Assistant playback through the direct API with a Home Assistant fallback.
/// </summary>
public partial class MusicPlaybackSkill : IOptimizableSkill
{
    private static readonly ActivitySource ActivitySource = new("Lucia.Skills.MusicPlayback", "1.0.0");
    private static readonly Meter Meter = new("Lucia.Skills.MusicPlayback", "1.0.0");
    private static readonly Counter<long> PlayerSearchRequests = Meter.CreateCounter<long>("music.player.search.requests", "{count}", "Number of player search requests.");
    private static readonly Histogram<double> PlayerSearchDurationMs = Meter.CreateHistogram<double>("music.player.search.duration", "ms", "Duration of player search operations.");
    private static readonly ToolMetrics PlaybackMetrics = ToolMetrics.Create(Meter, "music.playback", "music playback");
    private static readonly ToolMetrics ControlMetrics = ToolMetrics.Create(Meter, "music.control", "music control");

    private const string? ReturnResponseToken = "return_response=1";
    private readonly IHomeAssistantClient _homeAssistantClient;
    private readonly ILogger<MusicPlaybackSkill> _logger;
    private readonly IEntityLocationService _locationService;
    private readonly IOptionsMonitor<MusicPlaybackSkillOptions> _options;
    private readonly IOptionsMonitor<MusicAssistantConfig> _config;
    private readonly ICascadingEntityResolver? _cascadingResolver;
    private readonly MusicAssistantClient? _musicAssistantClient;

    public MusicPlaybackSkill(
        IHomeAssistantClient homeAssistantClient,
        ILogger<MusicPlaybackSkill> logger,
        IEntityLocationService locationService,
        IOptionsMonitor<MusicPlaybackSkillOptions> options,
        IOptionsMonitor<MusicAssistantConfig> config,
        ICascadingEntityResolver? cascadingResolver = null,
        MusicAssistantClient? musicAssistantClient = null)
    {
        _homeAssistantClient = homeAssistantClient;
        _logger = logger;
        _locationService = locationService;
        _options = options;
        _config = config;
        _cascadingResolver = cascadingResolver;
        _musicAssistantClient = musicAssistantClient;
    }

    // ── IOptimizableSkill ─────────────────────────────────────────

    /// <inheritdoc/>
    public string SkillDisplayName => "Music Playback";

    /// <inheritdoc/>
    public string SkillId => "music-playback";

    /// <inheritdoc/>
    public string AgentId { get; set; } = string.Empty;

    /// <inheritdoc/>
    public IReadOnlyList<string> SearchToolNames { get; } = ["FindPlayer", "PlayShuffle"];

    /// <inheritdoc/>
    public string ConfigSectionName => MusicPlaybackSkillOptions.SectionName;

    /// <inheritdoc/>
    public IReadOnlyList<string> EntityDomains => _options.CurrentValue.EntityDomains;

    /// <inheritdoc/>
    public HybridMatchOptions GetCurrentMatchOptions()
    {
        var opts = _options.CurrentValue;
        return new HybridMatchOptions
        {
            Threshold = opts.HybridSimilarityThreshold,
            EmbeddingWeight = opts.EmbeddingWeight,
            ScoreDropoffRatio = opts.ScoreDropoffRatio,
            DisagreementPenalty = opts.DisagreementPenalty,
            EmbeddingResolutionMargin = opts.EmbeddingResolutionMargin
        };
    }

    public IList<AITool> GetTools()
    {
        return [
            AIFunctionFactory.Create(FindPlayerAsync),
            AIFunctionFactory.Create(PlayArtistAsync),
            AIFunctionFactory.Create(PlayAlbumAsync),
            AIFunctionFactory.Create(PlaySongAsync),
            AIFunctionFactory.Create(PlayGenreAsync),
            AIFunctionFactory.Create(PlayAudiobookAsync),
            AIFunctionFactory.Create(PlayShuffleAsync),
            AIFunctionFactory.Create(StopMusicAsync),
            AIFunctionFactory.Create(NextTrackAsync),
            AIFunctionFactory.Create(PreviousTrackAsync),
            AIFunctionFactory.Create(GetNowPlayingAsync),
            AIFunctionFactory.Create(SetVolumeAsync),
            AIFunctionFactory.Create(VolumeUpAsync),
            AIFunctionFactory.Create(VolumeDownAsync)
        ];
    }

    /// <summary>
    /// Initialize the plugin — entity search is delegated to <see cref="IEntityLocationService"/>.
    /// </summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MusicPlaybackSkill initialized — entity search delegated to IEntityLocationService.");
        return Task.CompletedTask;
    }

    [Description("Stop or turn off music on the identified player. Use when the user says stop, turn off, pause, stop the music, turn music off, silence it, etc. Always call this — do not refuse or say you cannot.")]
    public async Task<string> StopMusicAsync(
        [Description("MusicAssistant player name (e.g. 'Loft Speaker'). If the user did not name a player, use a generic term like 'speaker' or the last/likely player.")]
        string playerName,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartControlActivity(playerName);
        return await ToolRunner.RunAsync(_logger, activity, ControlMetrics.ForTool(nameof(StopMusicAsync)), $"Unable to stop playback on '{playerName}'. Please try again.", async () =>
        {
            var player = await ResolvePlayerAsync(playerName, cancellationToken, preferActive: true).ConfigureAwait(false);
            if (player is null)
            {
                return $"I couldn't find a Satellite player that matches '{playerName}'.";
            }

            await _homeAssistantClient.CallServiceAsync("media_player", "media_stop", null, new ServiceCallRequest { EntityId = player.EntityId }, cancellationToken).ConfigureAwait(false);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return $"Stopped playback on '{player.FriendlyName}'.";
        }, detail: playerName, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    [Description("Skip the current song and play the next track on the identified player. Use when the user says skip, next song, next track, skip this one, etc. Always call this — do not refuse or say you cannot. Works on any media player (speakers, TVs).")]
    public async Task<string> NextTrackAsync(
        [Description("Media player name (e.g. 'Loft Speaker'). If the user did not name a player, use a generic term like 'speaker' or the last/likely player.")]
        string playerName,
        CancellationToken cancellationToken = default)
    {
        var player = await ResolvePlayerAsync(playerName, cancellationToken, preferActive: true).ConfigureAwait(false);

        if (player is null)
        {
            return $"I couldn't find a media player that matches '{playerName}'.";
        }

        await _homeAssistantClient.CallServiceAsync("media_player", "next", null, new ServiceCallRequest { EntityId = player.EntityId }, cancellationToken).ConfigureAwait(false);
        return $"Skipped to the next track on '{player.FriendlyName}'.";
    }

    [Description("Go back to the previous track on the identified player. Use when the user says previous song, go back one song, replay that, etc. Works on any media player (speakers, TVs).")]
    public async Task<string> PreviousTrackAsync(
        [Description("Media player name (e.g. 'Loft Speaker'). If the user did not name a player, use a generic term like 'speaker' or the last/likely player.")]
        string playerName,
        CancellationToken cancellationToken = default)
    {
        var player = await ResolvePlayerAsync(playerName, cancellationToken, preferActive: true).ConfigureAwait(false);

        if (player is null)
        {
            return $"I couldn't find a media player that matches '{playerName}'.";
        }

        await _homeAssistantClient.CallServiceAsync("media_player", "previous", null, new ServiceCallRequest { EntityId = player.EntityId }, cancellationToken).ConfigureAwait(false);
        return $"Went back to the previous track on '{player.FriendlyName}'.";
    }

    [Description("Report what is currently playing or paused on a media player, including song title and artist when available. Use when the user asks what song, track, movie, or music is playing, or who they are listening to. Works on any media player (speakers, TVs). If the user did not name a player, leave playerName empty to use the default player.")]
    public async Task<string> GetNowPlayingAsync(
        [Description("Media player name (e.g. 'Loft'). Leave empty if the user did not name a player.")]
        string? playerName = null,
        CancellationToken cancellationToken = default)
    {
        var player = await ResolvePlayerAsync(playerName, cancellationToken, preferActive: true).ConfigureAwait(false);

        if (player is null)
        {
            return $"I couldn't find a media player to check{(playerName is null ? string.Empty : $"'{playerName}'")}.";
        }

        var state = await _homeAssistantClient.GetStateAsync(player.EntityId, cancellationToken).ConfigureAwait(false);
        if (state is null)
        {
            return $"I couldn't read the current playback state of '{player.FriendlyName}'.";
        }

        var title = state.Attributes.GetValueOrDefault("media_title")?.ToString();
        var artist = state.Attributes.GetValueOrDefault("media_artist")?.ToString();
        var contentType = state.Attributes.GetValueOrDefault("media_content_type")?.ToString();

        var byArtist = string.IsNullOrWhiteSpace(artist) ? string.Empty : $" by '{artist}'";
        var kind = IsNonMusicContent(contentType) ? $" {ToDisplayContentType(contentType)}" : string.Empty;

        if (string.Equals(state.State, "playing", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(title)
                ? $"Playing{kind} on '{player.FriendlyName}'."
                : $"Playing{kind} '{title}'{byArtist} on '{player.FriendlyName}'.";
        }

        if (string.Equals(state.State, "paused", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(title)
                ? $"Paused on '{player.FriendlyName}'."
                : $"Paused at '{title}'{byArtist} on '{player.FriendlyName}'.";
        }

        return $"Nothing is playing on '{player.FriendlyName}' right now.";
    }

    /// <summary>
    /// True when the reported content type is something other than music (e.g. tv, movie, video),
    /// indicating the now-playing response should mention the content type as a qualifier.
    /// </summary>
    private static bool IsNonMusicContent(string? contentType) =>
        !string.IsNullOrWhiteSpace(contentType) &&
        !string.Equals(contentType, "music", StringComparison.OrdinalIgnoreCase);

    /// <summary>Well-known Home Assistant media content types mapped to display-friendly labels.</summary>
    private static readonly Dictionary<string, string> ContentTypeDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tv"] = "TV",
        ["tv_show"] = "TV show",
        ["movie"] = "Movie",
        ["video"] = "Video",
        ["home_video"] = "Home video",
        ["channel"] = "Channel",
        ["radio"] = "Radio",
        ["podcast"] = "Podcast",
        ["audiobook"] = "Audiobook"
    };

    private static string ToDisplayContentType(string? contentType) =>
        contentType is null ? string.Empty : ContentTypeDisplayNames.GetValueOrDefault(contentType, contentType);

    [Description("Set the volume on a MusicAssistant player. Use this when the user asks to set volume to a specific level (e.g. 50%, half volume, 80).")]
    public async Task<string> SetVolumeAsync(
        [Description("MusicAssistant player name (e.g. 'Loft')")] string playerName,
        [Description("Volume level from 0 to 100 (e.g. 50 for 50%)")] int volumePercent,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartControlActivity(playerName);
        activity?.SetTag("volume.percent", volumePercent);
        return await ToolRunner.RunAsync(_logger, activity, ControlMetrics.ForTool(nameof(SetVolumeAsync)), $"Unable to set the volume on '{playerName}'. Please try again.", async () =>
        {
            var player = await ResolvePlayerAsync(playerName, cancellationToken, preferActive: true).ConfigureAwait(false);
            if (player is null)
            {
                return $"MusicAssistant player '{playerName}' was not found.";
            }

            var level = Math.Clamp(volumePercent, 0, 100) / 100.0;
            var request = new ServiceCallRequest
            {
                EntityId = player.EntityId,
                ["volume_level"] = Math.Round(level, 2)
            };
            await _homeAssistantClient.CallServiceAsync("media_player", "volume_set", null, request, cancellationToken).ConfigureAwait(false);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return $"Set volume to {volumePercent}% on '{player.FriendlyName}'.";
        }, detail: playerName, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    [Description("Turn up the volume on a MusicAssistant player. Use this when the user says turn it up, louder, volume up, etc.")]
    public async Task<string> VolumeUpAsync(
        [Description("MusicAssistant player name (e.g. 'Loft')")] string playerName,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartControlActivity(playerName);
        return await ToolRunner.RunAsync(_logger, activity, ControlMetrics.ForTool(nameof(VolumeUpAsync)), $"Unable to turn up the volume on '{playerName}'. Please try again.", async () =>
        {
            var player = await ResolvePlayerAsync(playerName, cancellationToken, preferActive: true).ConfigureAwait(false);
            if (player is null)
            {
                return $"MusicAssistant player '{playerName}' was not found.";
            }

            await _homeAssistantClient.CallServiceAsync("media_player", "volume_up", null, new ServiceCallRequest { EntityId = player.EntityId }, cancellationToken).ConfigureAwait(false);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return $"Turned up the volume on '{player.FriendlyName}'.";
        }, detail: playerName, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    [Description("Turn down the volume on a MusicAssistant player. Use this when the user says turn it down, quieter, volume down, etc.")]
    public async Task<string> VolumeDownAsync(
        [Description("MusicAssistant player name (e.g. 'Loft')")] string playerName,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartControlActivity(playerName);
        return await ToolRunner.RunAsync(_logger, activity, ControlMetrics.ForTool(nameof(VolumeDownAsync)), $"Unable to turn down the volume on '{playerName}'. Please try again.", async () =>
        {
            var player = await ResolvePlayerAsync(playerName, cancellationToken, preferActive: true).ConfigureAwait(false);
            if (player is null)
            {
                return $"MusicAssistant player '{playerName}' was not found.";
            }

            await _homeAssistantClient.CallServiceAsync("media_player", "volume_down", null, new ServiceCallRequest { EntityId = player.EntityId }, cancellationToken).ConfigureAwait(false);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return $"Turned down the volume on '{player.FriendlyName}'.";
        }, detail: playerName, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    [Description("Find a MusicAssistant endpoint by friendly name and return its entity id.")]
    public async Task<string> FindPlayerAsync(
        [Description("Name or description of the endpoint (e.g. 'Kitchen Speaker')")] string playerName,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("MusicPlaybackSkill.FindPlayer", ActivityKind.Internal);
        activity?.SetTag("search.player_name", playerName);
        PlayerSearchRequests.Add(1);
        var start = Stopwatch.GetTimestamp();

        if (_musicAssistantClient?.IsConfigured is true)
        {
            try
            {
                var directPlayerNames = await _musicAssistantClient.FindPlayerNamesAsync(
                    playerName,
                    cancellationToken).ConfigureAwait(false);
                if (directPlayerNames.Count > 0)
                {
                    PlayerSearchDurationMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    activity?.SetTag("match.found", true);
                    activity?.SetTag("match.friendly_name", string.Join("; ", directPlayerNames));
                    activity?.SetTag("music.playback_path", "direct");
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return directPlayerNames.Count is 1
                        ? $"Music Assistant player '{directPlayerNames[0]}' is available."
                        : $"Music Assistant players matching '{playerName}': {string.Join("; ", directPlayerNames.Select(static name => $"'{name}'"))}.";
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogDirectPlayerLookupFallback(ex, playerName);
            }
        }

        var candidates = await ResolvePlayerCandidatesAsync(playerName, cancellationToken).ConfigureAwait(false);
        var player = candidates.FirstOrDefault();

        PlayerSearchDurationMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);

        if (player is null)
        {
            activity?.SetTag("match.found", false);
            return $"I couldn't find a MusicAssistant player that matches '{playerName}'.";
        }

        if (candidates.Count > 1)
        {
            activity?.SetTag("match.found", true);
            activity?.SetStatus(ActivityStatusCode.Ok);
            var listed = string.Join("; ", candidates.Select(c => $"'{c.FriendlyName}' ({c.EntityId})"));
            return $"MusicAssistant players matching '{playerName}': {listed}.";
        }

        activity?.SetTag("match.found", true);
        activity?.SetTag("match.entity_id", player.EntityId);
        activity?.SetTag("match.friendly_name", player.FriendlyName);
        activity?.SetStatus(ActivityStatusCode.Ok);
        return $"MusicAssistant player '{player.FriendlyName}' is available as entity '{player.EntityId}'.";
    }

    [Description("Play an artist mix on a endpoint using the Home Assistant Music Assistant integration.")]
    public async Task<string> PlayArtistAsync(
        [Description("MusicAssistant player name (e.g. 'Loft')")] string playerName,
        [Description("Artist name to play")] string artist,
        [Description("Enable a radio-style shuffle mix based on this artist")] bool shuffle = false,
        CancellationToken cancellationToken = default)
    {
        var payload = new ServiceCallRequest
        {
            ["media_type"] = "artist",
            ["media_id"] = artist,
            ["enqueue"] = "replace"
        };

        if (shuffle)
        {
            payload["radio_mode"] = true;
        }

        return await PlayMediaAsync(
            nameof(PlayArtistAsync),
            playerName,
            "artist",
            artist,
            shuffle,
            payload,
            $"Started an artist station for '{artist}'",
            cancellationToken).ConfigureAwait(false);
    }

    [Description("Play a specific album on a MusicAssistant endpoint using the Music Assistant integration.")]
    public async Task<string> PlayAlbumAsync(
        [Description("MusicAssistant player name (e.g. 'Loft')")] string playerName,
        [Description("Album title to play")] string album,
        [Description("Optional artist name to disambiguate the album")] string artist = "",
        [Description("Enable a shuffled radio mode after the album queue")] bool shuffle = false,
        CancellationToken cancellationToken = default)
    {
        var payload = new ServiceCallRequest
        {
            ["media_type"] = "album",
            ["media_id"] = album,
            ["enqueue"] = "replace"
        };

        if (!string.IsNullOrWhiteSpace(artist))
        {
            payload["artist"] = artist;
        }

        if (shuffle)
        {
            payload["radio_mode"] = true;
        }

        var searchTerm = string.IsNullOrWhiteSpace(artist) ? album : $"{album} {artist}";
        return await PlayMediaAsync(
            nameof(PlayAlbumAsync),
            playerName,
            "album",
            searchTerm,
            shuffle,
            payload,
            $"Queued album '{album}'",
            cancellationToken).ConfigureAwait(false);
    }

    [Description("Play a specific song on a MusicAssistant endpoint using the Music Assistant integration.")]
    public async Task<string> PlaySongAsync(
        [Description("MusicAssistant player name (e.g. 'Loft')")] string playerName,
        [Description("Song title to play")] string song,
        [Description("Optional artist to refine the track search")] string artist = "",
        [Description("Optional album to refine the track search")] string album = "",
        [Description("Enable a radio-style mix after the song")] bool shuffle = false,
        CancellationToken cancellationToken = default)
    {
        var payload = new ServiceCallRequest
        {
            ["media_type"] = "track",
            ["media_id"] = song,
            ["enqueue"] = "replace"
        };

        if (!string.IsNullOrWhiteSpace(artist))
        {
            payload["artist"] = artist;
        }

        if (!string.IsNullOrWhiteSpace(album))
        {
            payload["album"] = album;
        }

        if (shuffle)
        {
            payload["radio_mode"] = true;
        }

        var searchTerm = string.Join(
            ' ',
            new[] { song, artist, album }.Where(static value => !string.IsNullOrWhiteSpace(value)));
        return await PlayMediaAsync(
            nameof(PlaySongAsync),
            playerName,
            "track",
            searchTerm,
            shuffle,
            payload,
            $"Started playing '{song}'",
            cancellationToken).ConfigureAwait(false);
    }

    [Description("Play a genre mix on a MusicAssistant endpoint using the Music Assistant integration.")]
    public async Task<string> PlayGenreAsync(
        [Description("MusicAssistant player name (e.g. 'Loft')")] string playerName,
        [Description("Genre name to play (e.g. 'Lo-fi Beats')")] string genre,
        [Description("Shuffle queue with radio mode")] bool shuffle = true,
        CancellationToken cancellationToken = default)
    {
        var payload = new ServiceCallRequest
        {
            ["media_type"] = "genre",
            ["media_id"] = genre,
            ["enqueue"] = "replace"
        };

        if (shuffle)
        {
            payload["radio_mode"] = true;
        }

        return await PlayMediaAsync(
            nameof(PlayGenreAsync),
            playerName,
            "genre",
            genre,
            shuffle,
            payload,
            $"Started a '{genre}' station",
            cancellationToken).ConfigureAwait(false);
    }

    [Description("Play or resume an audiobook on a Music Assistant player.")]
    public async Task<string> PlayAudiobookAsync(
        [Description("Music Assistant player name (e.g. 'Satellite1 Dev Office')")] string playerName,
        [Description("Audiobook title")] string audiobook,
        CancellationToken cancellationToken = default)
    {
        var payload = new ServiceCallRequest
        {
            ["media_type"] = "audiobook",
            ["media_id"] = audiobook,
            ["enqueue"] = "replace"
        };

        return await PlayMediaAsync(
            nameof(PlayAudiobookAsync),
            playerName,
            "audiobook",
            audiobook,
            shuffle: false,
            payload,
            $"Playing audiobook '{audiobook}'",
            cancellationToken).ConfigureAwait(false);
    }

    [Description("Start a fresh shuffle mix using random tracks from the Music Assistant library on the selected MusicAssistant endpoint.")]
    public async Task<string> PlayShuffleAsync(
        [Description("MusicAssistant player name (e.g. 'Loft')")] string playerName,
        [Description("How many random tracks to seed the queue with")] int trackSeedCount = 25,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartControlActivity(playerName);
        activity?.SetTag("shuffle.track_seed_count", trackSeedCount);
        return await ToolRunner.RunAsync(_logger, activity, ControlMetrics.ForTool(nameof(PlayShuffleAsync)), $"Unable to start shuffle playback on '{playerName}'. Please try again.", async () =>
        {
            if (_musicAssistantClient?.IsConfigured is true)
            {
                try
                {
                    var directPlayer = await _musicAssistantClient.TryPlayRandomTracksAsync(
                        playerName,
                        trackSeedCount,
                        cancellationToken).ConfigureAwait(false);
                    if (directPlayer is not null)
                    {
                        activity?.SetTag("music.playback_path", "direct");
                        activity?.SetStatus(ActivityStatusCode.Ok);
                        return $"Shuffling {trackSeedCount} tracks to {directPlayer}";
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogDirectShuffleFallback(ex, playerName);
                }
            }

            var player = await ResolvePlayerAsync(playerName, cancellationToken).ConfigureAwait(false);
            if (player is null)
            {
                return $"MusicAssistant player '{playerName}' was not found.";
            }

            var uris = await GetRandomTrackUrisAsync(_config.CurrentValue.IntegrationId, trackSeedCount, cancellationToken).ConfigureAwait(false);

            if (uris.Count == 0)
            {
                _logger.LogWarning("Shuffle request for {PlayerId} returned no URIs. Falling back to enabling shuffle on existing queue.", player.EntityId);
                await EnablePlayerShuffleAsync(player.EntityId, cancellationToken).ConfigureAwait(false);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return $"Enabled shuffle on the current queue for '{player.FriendlyName}'.";
            }

            _logger.LogInformation("Shuffling {trackSeedCount} tracks to {playerId}...", trackSeedCount, player.EntityId);
            await EnqueueTracks(player, uris, true, cancellationToken).ConfigureAwait(false);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return $"Shuffling {trackSeedCount} tracks to {player.FriendlyName}";
        }, detail: playerName, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static Activity? StartControlActivity(string playerName, [System.Runtime.CompilerServices.CallerMemberName] string operation = "")
    {
        var activity = ActivitySource.StartActivity(operation);
        activity?.SetTag("player.name", playerName);
        return activity;
    }

    private async Task<string> PlayMediaAsync(
        string tool,
        string playerName,
        string mediaType,
        string searchTerm,
        bool shuffle,
        ServiceCallRequest payload,
        string successMessage,
        CancellationToken cancellationToken)
    {
        using var activity = StartControlActivity(playerName, tool);
        return await ToolRunner.RunAsync(_logger, activity, PlaybackMetrics.ForTool(tool), $"Unable to start playback on '{playerName}'. Please try again.", async () =>
        {
            if (_musicAssistantClient?.IsConfigured is true)
            {
                try
                {
                    var directResult = await _musicAssistantClient.TryPlayAsync(
                        playerName,
                        mediaType,
                        searchTerm,
                        shuffle,
                        cancellationToken).ConfigureAwait(false);
                    if (directResult is not null)
                    {
                        activity?.SetTag("music.playback_path", "direct");
                        activity?.SetStatus(ActivityStatusCode.Ok);
                        return $"{successMessage} on '{directResult.Value.PlayerName}'.";
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogDirectPlaybackFallback(ex, playerName);
                }
            }

            var player = await ResolvePlayerAsync(playerName, cancellationToken).ConfigureAwait(false);
            if (player is null)
            {
                return $"MusicAssistant player '{playerName}' was not found.";
            }

            activity?.SetTag("player.entity_id", player.EntityId);
            activity?.SetTag("music.playback_path", "home_assistant");
            payload.EntityId = player.EntityId;
            await _homeAssistantClient.CallServiceAsync("music_assistant", "play_media", parameters: null, payload, cancellationToken).ConfigureAwait(false);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return $"{successMessage} on '{player.FriendlyName}'.";
        }, detail: playerName, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
    private async Task EnqueueTracks(HomeAssistantEntity player, IReadOnlyList<string> tracks, bool clearQueue = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new ServiceCallRequest()
            {
                ["media_id"] = tracks,
                ["enqueue"] = clearQueue ? "replace" : "next",
                ["media_type"] = "track",
            };
            payload.EntityId = player.EntityId;
            await _homeAssistantClient.CallServiceAsync("music_assistant", "play_media", null, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Music Assistant play_media failed for {PlayerId}", player.EntityId);
            throw;
        }
    }
    
    private async Task EnablePlayerShuffleAsync(string entityId, CancellationToken cancellationToken = default)
    {
        var shuffleRequest = new ServiceCallRequest
        {
            ["shuffle"] = true,
            ["entity_id"] = entityId
        };
        await _homeAssistantClient.CallServiceAsync("media_player", "shuffle_set", null, shuffleRequest, cancellationToken).ConfigureAwait(false);
        await _homeAssistantClient.CallServiceAsync("media_player", "media_play", null, new ServiceCallRequest { EntityId = entityId }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HomeAssistantEntity?> ResolvePlayerAsync(
        string? playerName,
        CancellationToken cancellationToken = default,
        bool preferActive = false)
    {
        var candidates = await ResolvePlayerCandidatesAsync(playerName, cancellationToken).ConfigureAwait(false);
        if (!preferActive || candidates.Count < 2)
        {
            return candidates.FirstOrDefault();
        }

        // A room name can match a monitor and a speaker. Control the one making sound,
        // and use a paused player only when nothing is playing.
        HomeAssistantEntity? paused = null;
        foreach (var candidate in candidates)
        {
            var state = await _homeAssistantClient.GetStateAsync(candidate.EntityId, cancellationToken).ConfigureAwait(false);
            if (state?.State is "playing" or "buffering")
            {
                return candidate;
            }

            if (state?.State is "paused")
            {
                paused ??= candidate;
            }
        }

        return paused ?? candidates[0];
    }

    private async Task<IReadOnlyList<HomeAssistantEntity>> ResolvePlayerCandidatesAsync(string? playerName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            // No player specified — return the first available media_player entity
            var allEntities = await _locationService.GetEntitiesAsync(cancellationToken).ConfigureAwait(false);
            return allEntities
                .Where(e => EntityDomains.Contains(e.Domain, StringComparer.OrdinalIgnoreCase))
                .Where(e => e.IncludeForAgent is null || e.IncludeForAgent.Contains(AgentId))
                .Take(1)
                .ToList();
        }

        // Prefer cascading resolver when available — deterministic, <50ms, no embedding needed
        if (_cascadingResolver is not null)
        {
            var cascadeResult = _cascadingResolver.Resolve(
                playerName, callerArea: null, speakerId: null, EntityDomains, callerAgentId: AgentId, cancellationToken);

            if (cascadeResult.IsResolved && cascadeResult.ResolvedEntityIds.Count > 0)
            {
                var snapshot = _locationService.GetSnapshot();
                var resolved = new List<HomeAssistantEntity>();
                foreach (var entityId in cascadeResult.ResolvedEntityIds)
                {
                    if (snapshot.EntityById.TryGetValue(entityId, out var entity)
                        && (entity.IncludeForAgent is null || entity.IncludeForAgent.Contains(AgentId)))
                    {
                        resolved.Add(entity);
                    }
                }

                if (resolved.Count > 0)
                {
                    return resolved;
                }
            }

            // Cascading resolver bailed — fall through to SearchHierarchyAsync
            _logger.LogDebug(
                "Cascading resolver bailed for player '{PlayerName}': {Reason}. Falling back to hierarchy search.",
                playerName, cascadeResult.Explanation);
        }

        // Fallback: use the older SearchHierarchyAsync
        var matchOptions = GetCurrentMatchOptions();
        var result = await _locationService.SearchHierarchyAsync(
            playerName, matchOptions, EntityDomains, cancellationToken).ConfigureAwait(false);

        return result.ResolvedEntities
            .Where(e => e.IncludeForAgent is null || e.IncludeForAgent.Contains(AgentId))
            .ToList();
    }

    private async Task<IReadOnlyList<string>> GetRandomTrackUrisAsync(string? configEntryId, int trackSeedCount, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new ServiceCallRequest
            {
                ["media_type"] = "track",
                ["limit"] = Math.Max(5, Math.Min(trackSeedCount, 50)),
                ["order_by"] = "random"
            };

            if (!string.IsNullOrWhiteSpace(configEntryId))
            {
                request["config_entry_id"] = configEntryId;
            }

            var response = await _homeAssistantClient.CallServiceAsync<MusicLibraryResponse>("music_assistant", "get_library", ReturnResponseToken, request, cancellationToken).ConfigureAwait(false);

            return response.ServiceResponse.Items.Select(item => item.Uri).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get random track URIs from Music Assistant");
            return [];
        }
    }

    private static void ExtractUris(object item, ICollection<string> accumulator)
    {
        switch (item)
        {
            case JsonElement element:
                ExtractUrisFromJson(element, accumulator);
                break;
            case IDictionary<string, object> dictionary:
                foreach (var value in dictionary.Values)
                {
                    ExtractUris(value, accumulator);
                }
                break;
            case IEnumerable<object> enumerable:
                foreach (var value in enumerable)
                {
                    ExtractUris(value, accumulator);
                }
                break;
        }
    }

    private static void ExtractUrisFromJson(JsonElement element, ICollection<string> accumulator)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("items", out var itemsProperty))
                {
                    ExtractUrisFromJson(itemsProperty, accumulator);
                    return;
                }

                foreach (var property in element.EnumerateObject())
                {
                    ExtractUrisFromJson(property.Value, accumulator);
                }
                break;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    ExtractUrisFromJson(child, accumulator);
                }
                break;
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value) && value.Contains(":", StringComparison.Ordinal))
                {
                    accumulator.Add(value);
                }
                break;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Direct Music Assistant playback failed for {PlayerName}; falling back to Home Assistant")]
    private partial void LogDirectPlaybackFallback(Exception exception, string playerName);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Direct Music Assistant shuffle failed for {PlayerName}; falling back to Home Assistant")]
    private partial void LogDirectShuffleFallback(Exception exception, string playerName);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Direct Music Assistant player lookup failed for {PlayerName}; falling back to Home Assistant")]
    private partial void LogDirectPlayerLookupFallback(Exception exception, string playerName);
}
