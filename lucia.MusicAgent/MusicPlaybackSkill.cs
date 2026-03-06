using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using lucia.Agents.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.Agents.Skills.Models;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.Options;

namespace lucia.MusicAgent;

/// <summary>
/// Semantic Kernel plugin that controls Music Assistant playback on Satellite endpoints via Home Assistant.
/// </summary>
public class MusicPlaybackSkill : IOptimizableSkill
{
    private static readonly ActivitySource ActivitySource = new("Lucia.Skills.MusicPlayback", "1.0.0");
    private static readonly Meter Meter = new("Lucia.Skills.MusicPlayback", "1.0.0");
    private static readonly Counter<long> PlayerSearchRequests = Meter.CreateCounter<long>("music.player.search.requests", "{count}", "Number of player search requests.");
    private static readonly Counter<long> PlaybackRequests = Meter.CreateCounter<long>("music.playback.requests", "{count}", "Number of playback requests.");
    private static readonly Histogram<double> PlayerSearchDurationMs = Meter.CreateHistogram<double>("music.player.search.duration", "ms", "Duration of player search operations.");
    private static readonly Histogram<double> PlaybackDurationMs = Meter.CreateHistogram<double>("music.playback.duration", "ms", "Duration of playback operations.");

    private const string? ReturnResponseToken = "return_response=1";
    private readonly IHomeAssistantClient _homeAssistantClient;
    private readonly ILogger<MusicPlaybackSkill> _logger;
    private readonly IEntityLocationService _locationService;
    private readonly IOptionsMonitor<MusicPlaybackSkillOptions> _options;
    private readonly IOptionsMonitor<MusicAssistantConfig> _config;

    public MusicPlaybackSkill(
        IHomeAssistantClient homeAssistantClient,
        ILogger<MusicPlaybackSkill> logger,
        IEntityLocationService locationService,
        IOptionsMonitor<MusicPlaybackSkillOptions> options,
        IOptionsMonitor<MusicAssistantConfig> config)
    {
        _homeAssistantClient = homeAssistantClient;
        _logger = logger;
        _locationService = locationService;
        _options = options;
        _config = config;
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
            AIFunctionFactory.Create(PlayShuffleAsync),
            AIFunctionFactory.Create(StopMusicAsync),
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
        var player = await ResolvePlayerAsync(playerName, cancellationToken).ConfigureAwait(false);

        if (player is null)
        {
            return $"I couldn't find a Satellite player that matches '{playerName}'.";
        }

        await _homeAssistantClient.CallServiceAsync("media_player", "media_stop", null, new ServiceCallRequest { EntityId = player.EntityId }, cancellationToken).ConfigureAwait(false);
        return $"Stopped playback on '{player.FriendlyName}'.";
    }

    [Description("Set the volume on a MusicAssistant player. Use this when the user asks to set volume to a specific level (e.g. 50%, half volume, 80).")]
    public async Task<string> SetVolumeAsync(
        [Description("MusicAssistant player name (e.g. 'Loft')")] string playerName,
        [Description("Volume level from 0 to 100 (e.g. 50 for 50%)")] int volumePercent,
        CancellationToken cancellationToken = default)
    {
        var player = await ResolvePlayerAsync(playerName, cancellationToken).ConfigureAwait(false);
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
        return $"Set volume to {volumePercent}% on '{player.FriendlyName}'.";
    }

    [Description("Turn up the volume on a MusicAssistant player. Use this when the user says turn it up, louder, volume up, etc.")]
    public async Task<string> VolumeUpAsync(
        [Description("MusicAssistant player name (e.g. 'Loft')")] string playerName,
        CancellationToken cancellationToken = default)
    {
        var player = await ResolvePlayerAsync(playerName, cancellationToken).ConfigureAwait(false);
        if (player is null)
        {
            return $"MusicAssistant player '{playerName}' was not found.";
        }

        await _homeAssistantClient.CallServiceAsync("media_player", "volume_up", null, new ServiceCallRequest { EntityId = player.EntityId }, cancellationToken).ConfigureAwait(false);
        return $"Turned up the volume on '{player.FriendlyName}'.";
    }

    [Description("Turn down the volume on a MusicAssistant player. Use this when the user says turn it down, quieter, volume down, etc.")]
    public async Task<string> VolumeDownAsync(
        [Description("MusicAssistant player name (e.g. 'Loft')")] string playerName,
        CancellationToken cancellationToken = default)
    {
        var player = await ResolvePlayerAsync(playerName, cancellationToken).ConfigureAwait(false);
        if (player is null)
        {
            return $"MusicAssistant player '{playerName}' was not found.";
        }

        await _homeAssistantClient.CallServiceAsync("media_player", "volume_down", null, new ServiceCallRequest { EntityId = player.EntityId }, cancellationToken).ConfigureAwait(false);
        return $"Turned down the volume on '{player.FriendlyName}'.";
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

        var player = await ResolvePlayerAsync(playerName, cancellationToken).ConfigureAwait(false);

        PlayerSearchDurationMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);

        if (player is null)
        {
            activity?.SetTag("match.found", false);
            return $"I couldn't find a MusicAssistant player that matches '{playerName}'.";
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
        var player = await ResolvePlayerAsync(playerName, cancellationToken).ConfigureAwait(false);
        if (player is null)
        {
            return $"MusicAssistant player '{playerName}' was not found.";
        }

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

        return await PlayMediaAsync(player, payload, $"Started an artist station for '{artist}'", cancellationToken).ConfigureAwait(false);
    }

    [Description("Play a specific album on a MusicAssistant endpoint using the Music Assistant integration.")]
    public async Task<string> PlayAlbumAsync(
        [Description("MusicAssistant player name (e.g. 'Loft')")] string playerName,
        [Description("Album title to play")] string album,
        [Description("Optional artist name to disambiguate the album")] string artist = "",
        [Description("Enable a shuffled radio mode after the album queue")] bool shuffle = false,
        CancellationToken cancellationToken = default)
    {
        var player = await ResolvePlayerAsync(playerName, cancellationToken).ConfigureAwait(false);
        if (player is null)
        {
            return $"MusicAssistant player '{playerName}' was not found.";
        }

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

        return await PlayMediaAsync(player, payload, $"Queued album '{album}'", cancellationToken).ConfigureAwait(false);
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
        var player = await ResolvePlayerAsync(playerName, cancellationToken).ConfigureAwait(false);
        if (player is null)
        {
            return $"MusicAssistant player '{playerName}' was not found.";
        }

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

        return await PlayMediaAsync(player, payload, $"Started playing '{song}'", cancellationToken).ConfigureAwait(false);
    }

    [Description("Play a genre mix on a MusicAssistant endpoint using the Music Assistant integration.")]
    public async Task<string> PlayGenreAsync(
        [Description("MusicAssistant player name (e.g. 'Loft')")] string playerName,
        [Description("Genre name to play (e.g. 'Lo-fi Beats')")] string genre,
        [Description("Shuffle queue with radio mode")] bool shuffle = true,
        CancellationToken cancellationToken = default)
    {
        var player = await ResolvePlayerAsync(playerName, cancellationToken).ConfigureAwait(false);
        if (player is null)
        {
            return $"MusicAssistant player '{playerName}' was not found.";
        }

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

        return await PlayMediaAsync(player, payload, $"Started a '{genre}' station", cancellationToken).ConfigureAwait(false);
    }

    [Description("Start a fresh shuffle mix using random tracks from the Music Assistant library on the selected MusicAssistant endpoint.")]
    public async Task<string> PlayShuffleAsync(
        [Description("MusicAssistant player name (e.g. 'Loft')")] string playerName,
        [Description("How many random tracks to seed the queue with")] int trackSeedCount = 25,
        CancellationToken cancellationToken = default)
    {
        var player = await ResolvePlayerAsync(playerName, cancellationToken).ConfigureAwait(false);
        if (player is null)
        {
            return $"MusicAssistant player '{playerName}' was not found.";
        }

        try
        {
            var uris = await GetRandomTrackUrisAsync(_config.CurrentValue.IntegrationId, trackSeedCount, cancellationToken).ConfigureAwait(false);

            if (uris.Count == 0)
            {
                _logger.LogWarning("Shuffle request for {PlayerId} returned no URIs. Falling back to enabling shuffle on existing queue.", player.EntityId);
                await EnablePlayerShuffleAsync(player.EntityId, cancellationToken).ConfigureAwait(false);
                return $"Enabled shuffle on the current queue for '{player.FriendlyName}'.";
            }
            else
            {
                _logger.LogInformation("Shuffling {trackSeedCount} tracks to {playerId}...", trackSeedCount, player.EntityId);
                await EnqueueTracks(player, uris, true, cancellationToken).ConfigureAwait(false);
                return $"Shuffling {trackSeedCount} tracks to {player.FriendlyName}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start shuffle mix for {PlayerId}", player.EntityId);
            return $"I couldn't start shuffle playback on '{player.FriendlyName}' due to an error: {ex.Message}";
        }
    }

    private async Task<string> PlayMediaAsync(HomeAssistantEntity player, ServiceCallRequest payload, string successMessage, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("player.entity_id", player.EntityId);
        activity?.SetTag("player.friendly_name", player.FriendlyName);
        PlaybackRequests.Add(1);
        var start = Stopwatch.GetTimestamp();

        try
        {
            payload.EntityId = player.EntityId;
            await _homeAssistantClient.CallServiceAsync("music_assistant", "play_media", parameters: null, payload, cancellationToken).ConfigureAwait(false);
            PlaybackDurationMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return $"{successMessage} on '{player.FriendlyName}'.";
        }
        catch (Exception ex)
        {
            PlaybackDurationMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Music Assistant play_media failed for {PlayerId}", player.EntityId);
            return $"I wasn't able to start playback on '{player.FriendlyName}': {ex.Message}";
        }
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

    private async Task<HomeAssistantEntity?> ResolvePlayerAsync(string? playerName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            // No player specified — return the first available media_player entity
            var allEntities = await _locationService.GetEntitiesAsync(cancellationToken).ConfigureAwait(false);
            return allEntities
                .Where(e => EntityDomains.Contains(e.Domain, StringComparer.OrdinalIgnoreCase))
                .FirstOrDefault(e => e.IncludeForAgent is null || e.IncludeForAgent.Contains(AgentId));
        }

        var matchOptions = GetCurrentMatchOptions();
        var result = await _locationService.SearchHierarchyAsync(
            playerName, matchOptions, EntityDomains, cancellationToken).ConfigureAwait(false);

        var entity = result.ResolvedEntities
            .FirstOrDefault(e => e.IncludeForAgent is null || e.IncludeForAgent.Contains(AgentId));

        return entity;
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
}
