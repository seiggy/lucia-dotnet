using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using lucia.Agents.Configuration;
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
    private const string? ReturnResponseToken = "return_response=1";
    private readonly IHomeAssistantClient _homeAssistantClient;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly ILogger<MusicPlaybackSkill> _logger;
    private readonly List<MusicPlayerEntity> _cachedPlayers = new();
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private readonly TimeSpan _cacheRefreshInterval = TimeSpan.FromMinutes(30);
    private readonly MusicAssistantConfig _config;
    private DateTime _lastCacheUpdate = DateTime.MinValue;
    
    public MusicPlaybackSkill(
        IHomeAssistantClient homeAssistantClient,
        IOptions<MusicAssistantConfig> config,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        ILogger<MusicPlaybackSkill> logger)
    {
        _homeAssistantClient = homeAssistantClient;
        _embeddingGenerator = embeddingGenerator;
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
        string playerName)
    {
        var player = await ResolvePlayerAsync(playerName).ConfigureAwait(false);

        if (player is null)
        {
            return $"I couldn't find a Satellite player that matches '{playerName}.";
        }

        await _homeAssistantClient.CallServiceAsync("media_player", "media_stop", null, new ServiceCallRequest { EntityId = player.EntityId }, CancellationToken.None).ConfigureAwait(false);
        return string.Empty;
    }

    [Description("Find a MusicAssistant endpoint by friendly name and return its entity id.")]
    public async Task<string> FindPlayerAsync(
        [Description("Name or description of the endpoint (e.g. 'Kitchen Speaker')")] string playerName)
    {
        var player = await ResolvePlayerAsync(playerName).ConfigureAwait(false);
        if (player is null)
        {
            return $"I couldn't find a MusicAssistant player that matches '{playerName}'.";
        }

        return $"MusicAssistant player '{player.FriendlyName}' is available as entity '{player.EntityId}'.";
    }

    [Description("Play an artist mix on a endpoint using the Home Assistant Music Assistant integration.")]
    public async Task<string> PlayArtistAsync(
        [Description("MusicAssistant player name (e.g. 'Loft')")] string playerName,
        [Description("Artist name to play")] string artist,
        [Description("Enable a radio-style shuffle mix based on this artist")] bool shuffle = false)
    {
        var player = await ResolvePlayerAsync(playerName).ConfigureAwait(false);
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

        return await PlayMediaAsync(player, payload, $"Started an artist station for '{artist}'").ConfigureAwait(false);
    }

    [Description("Play a specific album on a MusicAssistant endpoint using the Music Assistant integration.")]
    public async Task<string> PlayAlbumAsync(
        [Description("MusicAssistant player name (e.g. 'Loft')")] string playerName,
        [Description("Album title to play")] string album,
        [Description("Optional artist name to disambiguate the album")] string? artist = null,
        [Description("Enable a shuffled radio mode after the album queue")] bool shuffle = false)
    {
        var player = await ResolvePlayerAsync(playerName).ConfigureAwait(false);
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

        return await PlayMediaAsync(player, payload, $"Queued album '{album}'").ConfigureAwait(false);
    }

    [Description("Play a specific song on a MusicAssistant endpoint using the Music Assistant integration.")]
    public async Task<string> PlaySongAsync(
        [Description("MusicAssistant player name (e.g. 'Loft')")] string playerName,
        [Description("Song title to play")] string song,
        [Description("Optional artist to refine the track search")] string? artist = null,
        [Description("Optional album to refine the track search")] string? album = null,
        [Description("Enable a radio-style mix after the song")] bool shuffle = false)
    {
        var player = await ResolvePlayerAsync(playerName).ConfigureAwait(false);
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

        return await PlayMediaAsync(player, payload, $"Started playing '{song}'").ConfigureAwait(false);
    }

    [Description("Play a genre mix on a MusicAssistant endpoint using the Music Assistant integration.")]
    public async Task<string> PlayGenreAsync(
        [Description("MusicAssistant player name (e.g. 'Loft')")] string playerName,
        [Description("Genre name to play (e.g. 'Lo-fi Beats')")] string genre,
        [Description("Shuffle queue with radio mode")] bool shuffle = true)
    {
        var player = await ResolvePlayerAsync(playerName).ConfigureAwait(false);
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

        return await PlayMediaAsync(player, payload, $"Started a '{genre}' station").ConfigureAwait(false);
    }

    [Description("Start a fresh shuffle mix using random tracks from the Music Assistant library on the selected MusicAssistant endpoint.")]
    public async Task<string> PlayShuffleAsync(
        [Description("MusicAssistant player name (e.g. 'Loft')")] string playerName,
        [Description("How many random tracks to seed the queue with")] int trackSeedCount = 25)
    {
        var player = await ResolvePlayerAsync(playerName).ConfigureAwait(false);
        if (player is null)
        {
            return $"MusicAssistant player '{playerName}' was not found.";
        }

        try
        {
            var uris = await GetRandomTrackUrisAsync(_config.IntegrationId, trackSeedCount).ConfigureAwait(false);

            if (uris.Count == 0)
            {
                _logger.LogWarning("Shuffle request for {PlayerId} returned no URIs. Falling back to enabling shuffle on existing queue.", player.EntityId);
                await EnablePlayerShuffleAsync(player.EntityId).ConfigureAwait(false);
                return $"Enabled shuffle on the current queue for '{player.FriendlyName}'.";
            }
            else
            {
                _logger.LogInformation("Shuffling {trackSeedCount} tracks to {playerId}...", trackSeedCount, player.EntityId);
                await EnqueueTracks(player, uris, true).ConfigureAwait(false);
                return $"Shuffling {trackSeedCount} tracks to {player.FriendlyName}";
            }

            var payload = new ServiceCallRequest
            {
                ["media_type"] = "track",
                ["media_id"] = uris,
                ["enqueue"] = "replace",
                ["radio_mode"] = true
            };

            return await PlayMediaAsync(player, payload, $"Queued a fresh shuffle mix with {uris.Count} tracks").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start shuffle mix for {PlayerId}", player.EntityId);
            return $"I couldn't start shuffle playback on '{player.FriendlyName}' due to an error: {ex.Message}";
        }
    }

    private async Task<string> PlayMediaAsync(MusicPlayerEntity player, ServiceCallRequest payload, string successMessage)
    {
        try
        {
            payload.EntityId = player.EntityId;
            await _homeAssistantClient.CallServiceAsync("music_assistant", "play_media", ReturnResponseToken, payload, CancellationToken.None).ConfigureAwait(false);
            return $"{successMessage} on '{player.FriendlyName}'.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Music Assistant play_media failed for {PlayerId}", player.EntityId);
            return $"I wasn't able to start playback on '{player.FriendlyName}': {ex.Message}";
        }
    }

    private async Task EnqueueTracks(MusicPlayerEntity player, IReadOnlyList<string> tracks, bool clearQueue = false)
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
            await _homeAssistantClient.CallServiceAsync("music_assistant", "play_media", null, payload, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Music Assistant play_media failed for {PlayerId}", player.EntityId);
            throw;
        }
    }
    
    private async Task EnablePlayerShuffleAsync(string entityId)
    {
        var shuffleRequest = new ServiceCallRequest
        {
            ["shuffle"] = true,
            ["entity_id"] = entityId
        };
        await _homeAssistantClient.CallServiceAsync("media_player", "shuffle_set", null, shuffleRequest, CancellationToken.None).ConfigureAwait(false);
        await _homeAssistantClient.CallServiceAsync("media_player", "media_play", null, new ServiceCallRequest { EntityId = entityId }, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<MusicPlayerEntity?> ResolvePlayerAsync(string? playerName)
    {
        await EnsureCacheIsCurrentAsync().ConfigureAwait(false);

        if (!_cachedPlayers.Any())
        {
            await RefreshPlayerCacheAsync(CancellationToken.None)
                .ConfigureAwait(false);
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
            var searchEmbedding = await _embeddingGenerator.GenerateAsync(trimmed).ConfigureAwait(false);
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

    private async Task EnsureCacheIsCurrentAsync()
    {
        if (DateTime.UtcNow - _lastCacheUpdate < _cacheRefreshInterval)
        {
            return;
        }

        await _cacheLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (DateTime.UtcNow - _lastCacheUpdate < _cacheRefreshInterval)
            {
                return;
            }

            await RefreshPlayerCacheAsync(CancellationToken.None).ConfigureAwait(false);
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
        catch (Exception e)
        {
            
        }
    }
    
    private async Task RefreshPlayerCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Refreshing Music Assistant player cache...");
            var states = await _homeAssistantClient.GetAllEntityStatesAsync(cancellationToken).ConfigureAwait(false);
            var players = states.Where(IsMusicAssistantPlayer).ToList();
            
            _cachedPlayers.Clear();

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

                    _cachedPlayers.Add(entity);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to build embedding for music player {EntityId}", state.EntityId);
                }
            }

            _lastCacheUpdate = DateTime.UtcNow;
            _logger.LogInformation("Cached {Count} Music Assistant players", _cachedPlayers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to refresh Music Assistant player cache");
        }
    }

    private async Task<IReadOnlyList<string>> GetRandomTrackUrisAsync(string? configEntryId, int trackSeedCount)
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

            var response = await _homeAssistantClient.CallServiceAsync<MusicLibraryResponse>("music_assistant", "get_library", ReturnResponseToken, request, CancellationToken.None).ConfigureAwait(false);

            return response.ServiceResponse.Items.Select(item => item.Uri).ToList();
        }
        catch (Exception e)
        {
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
}
