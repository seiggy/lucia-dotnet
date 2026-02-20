using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using lucia.Agents.Configuration;
using lucia.Agents.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using lucia.Agents.Models;
using lucia.Agents.Skills.Models;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.Options;

namespace lucia.MusicAgent;

/// <summary>
/// Semantic Kernel plugin that controls Music Assistant playback on Satellite endpoints via Home Assistant.
/// </summary>
public class MusicPlaybackSkill
{
    private static readonly ActivitySource ActivitySource = new("Lucia.Skills.MusicPlayback", "1.0.0");
    private static readonly Meter Meter = new("Lucia.Skills.MusicPlayback", "1.0.0");
    private static readonly Counter<long> PlayerSearchRequests = Meter.CreateCounter<long>("music.player.search.requests", "{count}", "Number of player search requests.");
    private static readonly Counter<long> PlaybackRequests = Meter.CreateCounter<long>("music.playback.requests", "{count}", "Number of playback requests.");
    private static readonly Histogram<double> PlayerSearchDurationMs = Meter.CreateHistogram<double>("music.player.search.duration", "ms", "Duration of player search operations.");
    private static readonly Histogram<double> PlaybackDurationMs = Meter.CreateHistogram<double>("music.playback.duration", "ms", "Duration of playback operations.");

    private const string? ReturnResponseToken = "return_response=1";
    private readonly IHomeAssistantClient _homeAssistantClient;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly ILogger<MusicPlaybackSkill> _logger;
    private readonly IDeviceCacheService _deviceCache;
    private volatile IReadOnlyList<MusicPlayerEntity> _cachedPlayers = Array.Empty<MusicPlayerEntity>();
    private readonly ConcurrentDictionary<string, Embedding<float>> _searchTermEmbeddingCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private readonly TimeSpan _cacheRefreshInterval = TimeSpan.FromMinutes(30);
    private readonly MusicAssistantConfig _config;
    private long _lastCacheUpdateTicks = DateTime.MinValue.Ticks;
    
    public MusicPlaybackSkill(
        IHomeAssistantClient homeAssistantClient,
        IOptions<MusicAssistantConfig> config,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IDeviceCacheService deviceCache,
        ILogger<MusicPlaybackSkill> logger)
    {
        _homeAssistantClient = homeAssistantClient;
        _embeddingGenerator = embeddingGenerator;
        _deviceCache = deviceCache;
        _logger = logger;
        _config = config.Value;
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
            AIFunctionFactory.Create(StopMusicAsync)
        ];
    }

    /// <summary>
    /// Pre-loads Satellite music player metadata so requests are fast and resilient.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RefreshPlayerCacheAsync(cancellationToken).ConfigureAwait(false);
    }

    [Description("Stops music on the identified player")]
    public async Task<string> StopMusicAsync(
        [Description("MusicAssistant player name (e.g. 'Loft Speaker')")]
        string playerName,
        CancellationToken cancellationToken = default)
    {
        var player = await ResolvePlayerAsync(playerName, cancellationToken).ConfigureAwait(false);

        if (player is null)
        {
            return $"I couldn't find a Satellite player that matches '{playerName}.";
        }

        await _homeAssistantClient.CallServiceAsync("media_player", "media_stop", null, new ServiceCallRequest { EntityId = player.EntityId }, cancellationToken).ConfigureAwait(false);
        return string.Empty;
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
        [Description("Optional artist name to disambiguate the album")] string? artist = null,
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
        [Description("Optional artist to refine the track search")] string? artist = null,
        [Description("Optional album to refine the track search")] string? album = null,
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
            var uris = await GetRandomTrackUrisAsync(_config.IntegrationId, trackSeedCount, cancellationToken).ConfigureAwait(false);

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

    private async Task<string> PlayMediaAsync(MusicPlayerEntity player, ServiceCallRequest payload, string successMessage, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("MusicPlaybackSkill.PlayMedia", ActivityKind.Internal);
        activity?.SetTag("player.entity_id", player.EntityId);
        activity?.SetTag("player.friendly_name", player.FriendlyName);
        PlaybackRequests.Add(1);
        var start = Stopwatch.GetTimestamp();

        try
        {
            payload.EntityId = player.EntityId;
            await _homeAssistantClient.CallServiceAsync("music_assistant", "play_media", ReturnResponseToken, payload, cancellationToken).ConfigureAwait(false);
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

    private async Task EnqueueTracks(MusicPlayerEntity player, IReadOnlyList<string> tracks, bool clearQueue = false, CancellationToken cancellationToken = default)
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

    private async Task<MusicPlayerEntity?> ResolvePlayerAsync(string? playerName, CancellationToken cancellationToken = default)
    {
        await EnsureCacheIsCurrentAsync(cancellationToken).ConfigureAwait(false);

        if (!_cachedPlayers.Any())
        {
            await _cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_cachedPlayers.Any())
                {
                    await RefreshPlayerCacheAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                _cacheLock.Release();
            }

            if (!_cachedPlayers.Any())
                return null;
        }

        if (string.IsNullOrWhiteSpace(playerName))
        {
            return _cachedPlayers.FirstOrDefault(p => p.IsSatellite) ?? _cachedPlayers.First();
        }

        var trimmed = playerName.Trim();

        // Prefer exact matches on entity id
        var exactEntity = _cachedPlayers.FirstOrDefault(p => string.Equals(p.EntityId, trimmed, StringComparison.OrdinalIgnoreCase));
        if (exactEntity is not null)
        {
            return exactEntity;
        }

        // Prefer exact friendly name matches
        var exactFriendly = _cachedPlayers.FirstOrDefault(p => string.Equals(p.FriendlyName, trimmed, StringComparison.OrdinalIgnoreCase));
        if (exactFriendly is not null)
        {
            return exactFriendly;
        }

        // Partial match prioritising Satellite endpoints
        var satellitePartial = _cachedPlayers
            .Where(p => p.IsSatellite)
            .FirstOrDefault(p => p.FriendlyName.Contains(trimmed, StringComparison.OrdinalIgnoreCase) || p.EntityId.Contains(trimmed, StringComparison.OrdinalIgnoreCase));
        if (satellitePartial is not null)
        {
            return satellitePartial;
        }

        var genericPartial = _cachedPlayers
            .FirstOrDefault(p => p.FriendlyName.Contains(trimmed, StringComparison.OrdinalIgnoreCase) || p.EntityId.Contains(trimmed, StringComparison.OrdinalIgnoreCase));
        if (genericPartial is not null)
        {
            return genericPartial;
        }

        // Fallback to semantic similarity
        try
        {
            var searchEmbedding = await GetOrCreateSearchEmbeddingAsync(trimmed).ConfigureAwait(false);
            var bestMatch = _cachedPlayers
                .Select(player => new
                {
                    Player = player,
                    Similarity = CosineSimilarity(searchEmbedding, player.NameEmbedding)
                })
                .OrderByDescending(x => x.Similarity)
                .FirstOrDefault();

            if (bestMatch is not null && bestMatch.Similarity >= 0.6)
            {
                return bestMatch.Player;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embedding lookup failed for MusicAssistant player '{PlayerName}'", trimmed);
        }

        return null;
    }

    private async Task EnsureCacheIsCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (DateTime.UtcNow - new DateTime(Volatile.Read(ref _lastCacheUpdateTicks), DateTimeKind.Utc) < _cacheRefreshInterval)
        {
            return;
        }

        await _cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (DateTime.UtcNow - new DateTime(Volatile.Read(ref _lastCacheUpdateTicks), DateTimeKind.Utc) < _cacheRefreshInterval)
            {
                return;
            }

            await RefreshPlayerCacheAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task FindMusicAssistantInstanceAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Finding music assistant instance");
            // need to use the Home Assistant WebSocket API to call {"type":"config_entries/get","type_filter":["device","hub","service"],"domain":"music_assistant","id": <<generated_int>> }
            // we don't have the websocket api integated yet, so just return the config added by the service config.
            
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to find Music Assistant instance");
        }
    }
    
    private async Task RefreshPlayerCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Refreshing Music Assistant player cache...");

            // Try Redis cache first
            var cachedPlayers = await _deviceCache.GetCachedPlayersAsync(cancellationToken);
            if (cachedPlayers is { Count: > 0 })
            {
                _logger.LogInformation("Loaded {PlayerCount} players from Redis cache", cachedPlayers.Count);
                var restoredPlayers = new List<MusicPlayerEntity>(cachedPlayers.Count);
                
                // Re-attach embeddings from Redis for each player
                var allRestored = true;
                foreach (var player in cachedPlayers)
                {
                    var embedding = await _deviceCache.GetEmbeddingAsync($"player:{player.EntityId}", cancellationToken);
                    if (embedding is not null)
                    {
                        player.NameEmbedding = embedding;
                        restoredPlayers.Add(player);
                    }
                    else
                    {
                        _logger.LogWarning("Missing embedding for player {EntityId} in Redis, will re-fetch", player.EntityId);
                        allRestored = false;
                        break;
                    }
                }
                
                if (allRestored && restoredPlayers.Count == cachedPlayers.Count)
                {
                    _cachedPlayers = restoredPlayers;
                    Volatile.Write(ref _lastCacheUpdateTicks, DateTime.UtcNow.Ticks);
                    return; // Cache hit
                }
                
                // Partial — fall through to full refresh
            }

            var states = await _homeAssistantClient.GetAllEntityStatesAsync(cancellationToken).ConfigureAwait(false);
            var players = states.Where(IsMusicAssistantPlayer).ToList();
            
            var newPlayers = new List<MusicPlayerEntity>(players.Count);

            foreach (var state in players)
            {
                var friendlyName = GetFriendlyName(state);
                try
                {
                    var embedding = await _embeddingGenerator.GenerateAsync(friendlyName, cancellationToken: cancellationToken).ConfigureAwait(false);
                    var entity = new MusicPlayerEntity
                    {
                        EntityId = state.EntityId,
                        FriendlyName = friendlyName,
                        ConfigEntryId = TryGetString(state.Attributes, "config_entry_id"),
                        IsSatellite = ContainsSatelliteHint(state),
                        NameEmbedding = embedding
                    };

                    newPlayers.Add(entity);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to build embedding for music player {EntityId}", state.EntityId);
                }
            }

            // Atomic swap — readers see either the old or new snapshot, never a partial list
            _cachedPlayers = newPlayers;
            Volatile.Write(ref _lastCacheUpdateTicks, DateTime.UtcNow.Ticks);

            // Save to Redis
            var playerCacheTtl = TimeSpan.FromMinutes(30);
            var embeddingCacheTtl = TimeSpan.FromHours(24);
            await _deviceCache.SetCachedPlayersAsync(newPlayers, playerCacheTtl, cancellationToken);
            foreach (var player in newPlayers)
            {
                await _deviceCache.SetEmbeddingAsync($"player:{player.EntityId}", player.NameEmbedding, embeddingCacheTtl, cancellationToken);
            }
            _logger.LogInformation("Saved {PlayerCount} players to Redis cache", newPlayers.Count);

            _logger.LogInformation("Cached {Count} Music Assistant players", newPlayers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to refresh Music Assistant player cache");
        }
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

    private static bool IsMusicAssistantPlayer(HomeAssistantState state)
    {
        if (!state.EntityId.StartsWith("media_player.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (state.Attributes.TryGetValue("music_assistant_player_id", out _))
        {
            return true;
        }

        if (state.EntityId.Contains("ma_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (state.Attributes.TryGetValue("app_id", out var appId) &&
            appId?.ToString()?.Contains("music_assistant", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        if (state.Attributes.TryGetValue("source", out var source) && source?.ToString()?.Contains("Music Assistant", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        if (state.Attributes.TryGetValue("integration", out var integration) && integration?.ToString()?.Contains("music_assistant", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return false;
    }

    private static string GetFriendlyName(HomeAssistantState state)
    {
        if (state.Attributes.TryGetValue("friendly_name", out var friendlyObj) && TryConvertToString(friendlyObj) is { Length: > 0 } friendly)
        {
            return friendly;
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(state.EntityId.Replace("_", " ").Replace("media_player.", string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryGetString(Dictionary<string, object> attributes, string key)
    {
        return attributes.TryGetValue(key, out var value) ? TryConvertToString(value) : null;
    }

    private static string? TryConvertToString(object? value)
    {
        return value switch
        {
            null => null,
            string s => s,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            JsonElement element when element.ValueKind == JsonValueKind.Number => element.GetDouble().ToString(CultureInfo.InvariantCulture),
            JsonElement element when element.ValueKind == JsonValueKind.True => bool.TrueString,
            JsonElement element when element.ValueKind == JsonValueKind.False => bool.FalseString,
            _ => value.ToString()
        };
    }

    private static bool ContainsSatelliteHint(HomeAssistantState state)
    {
        var friendly = GetFriendlyName(state);
        return state.EntityId.Contains("satellite", StringComparison.OrdinalIgnoreCase) ||
               friendly.Contains("Satellite", StringComparison.OrdinalIgnoreCase);
    }

    private static double CosineSimilarity(Embedding<float> vector1, Embedding<float> vector2)
    {
        var span1 = vector1.Vector.Span;
        var span2 = vector2.Vector.Span;

        if (span1.Length != span2.Length)
        {
            return 0.0;
        }

        double dotProduct = 0;
        double magnitude1 = 0;
        double magnitude2 = 0;

        for (var i = 0; i < span1.Length; i++)
        {
            dotProduct += span1[i] * span2[i];
            magnitude1 += span1[i] * span1[i];
            magnitude2 += span2[i] * span2[i];
        }

        if (magnitude1 == 0 || magnitude2 == 0)
        {
            return 0;
        }

        return dotProduct / (Math.Sqrt(magnitude1) * Math.Sqrt(magnitude2));
    }

    /// <summary>
    /// Returns a cached embedding for the search term, or generates and caches a new one.
    /// </summary>
    private async Task<Embedding<float>> GetOrCreateSearchEmbeddingAsync(string searchTerm)
    {
        if (_searchTermEmbeddingCache.TryGetValue(searchTerm, out var cached))
        {
            _logger.LogDebug("Search term embedding cache hit for '{SearchTerm}'", searchTerm);
            return cached;
        }

        var embedding = await _embeddingGenerator.GenerateAsync(searchTerm).ConfigureAwait(false);
        _searchTermEmbeddingCache.TryAdd(searchTerm, embedding);
        _logger.LogDebug("Cached search term embedding for '{SearchTerm}' ({Dimensions} dims)",
            searchTerm, embedding.Vector.Length);
        return embedding;
    }
}
