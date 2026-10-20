using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using lucia.Agents.Integration;
using lucia.Agents.Skills.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.MusicAgent;

public sealed partial class MusicAssistantClient(
    HttpClient httpClient,
    IOptionsMonitor<MusicAssistantConfig> options,
    ILogger<MusicAssistantClient> logger)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(options.CurrentValue.Url)
        && !string.IsNullOrWhiteSpace(options.CurrentValue.Token);

    public async Task<string?> FindPlayerNameAsync(
        string searchTerm,
        CancellationToken cancellationToken = default)
    {
        var player = await FindPlayerAsync(searchTerm, cancellationToken).ConfigureAwait(false);
        return player?.PlayerName;
    }

    public async Task<int> TestConnectionAsync(
        string? url = null,
        string? token = null,
        CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(
            "music/tracks/library_items",
            new
            {
                limit = 1,
                offset = 0,
                order_by = "random"
            },
            cancellationToken,
            url,
            token).ConfigureAwait(false);

        return document.RootElement.ValueKind is JsonValueKind.Array
            ? document.RootElement.GetArrayLength()
            : 0;
    }

    public async Task<(string MediaName, string PlayerName)?> TryPlayAsync(
        string playerName,
        string mediaType,
        string searchTerm,
        bool shuffle,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var player = await FindPlayerAsync(playerName, cancellationToken).ConfigureAwait(false);
        if (player is null)
        {
            return null;
        }

        var media = await SearchAsync(mediaType, searchTerm, cancellationToken).ConfigureAwait(false);
        if (media is null)
        {
            return null;
        }

        await SendAsync(
            "player_queues/play_media",
            new
            {
                queue_id = player.Value.PlayerId,
                media = media.Uri,
                option = "replace",
                shuffle,
                radio_mode = shuffle && mediaType is not "audiobook"
            },
            cancellationToken).ConfigureAwait(false);

        return (media.Name, player.Value.PlayerName);
    }

    public async Task<string?> TryPlayRandomTracksAsync(
        string playerName,
        int trackCount,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var player = await FindPlayerAsync(playerName, cancellationToken).ConfigureAwait(false);
        if (player is null)
        {
            return null;
        }

        var tracks = await GetLibraryItemsAsync(
            "track",
            search: null,
            Math.Clamp(trackCount, 5, 50),
            orderBy: "random",
            cancellationToken).ConfigureAwait(false);

        if (tracks.Count == 0)
        {
            return null;
        }

        await SendAsync(
            "player_queues/play_media",
            new
            {
                queue_id = player.Value.PlayerId,
                media = tracks.Select(static track => track.Uri).ToArray(),
                option = "replace",
                shuffle = true
            },
            cancellationToken).ConfigureAwait(false);

        return player.Value.PlayerName;
    }

    public async Task<IReadOnlyList<LibraryItems>> GetLibraryItemsAsync(
        string mediaType,
        string? search,
        int limit,
        string orderBy = "name",
        CancellationToken cancellationToken = default)
    {
        var collection = ToCollectionName(mediaType);
        using var document = await SendAsync(
            $"music/{collection}/library_items",
            new
            {
                search,
                limit,
                offset = 0,
                order_by = orderBy
            },
            cancellationToken).ConfigureAwait(false);

        return ReadItems(document.RootElement);
    }

    private async Task<(string PlayerId, string PlayerName)?> FindPlayerAsync(
        string searchTerm,
        CancellationToken cancellationToken)
    {
        var candidates = await FindPlayerCandidatesAsync(searchTerm, cancellationToken).ConfigureAwait(false);
        if (candidates.Count > 1)
        {
            LogAmbiguousPlayer(searchTerm);
            return null;
        }

        return candidates.Count is 1 ? candidates[0] : null;
    }

    public async Task<IReadOnlyList<string>> FindPlayerNamesAsync(
        string searchTerm,
        CancellationToken cancellationToken = default)
    {
        var candidates = await FindPlayerCandidatesAsync(searchTerm, cancellationToken).ConfigureAwait(false);
        return candidates.Select(static candidate => candidate.PlayerName).ToArray();
    }

    private async Task<IReadOnlyList<(string PlayerId, string PlayerName)>> FindPlayerCandidatesAsync(
        string searchTerm,
        CancellationToken cancellationToken)
    {
        using var document = await SendAsync(
            "players/all",
            new { },
            cancellationToken).ConfigureAwait(false);

        var players = document.RootElement
            .EnumerateArray()
            .Where(static item =>
                !item.TryGetProperty("available", out var available)
                || available.ValueKind is not JsonValueKind.False)
            .Select(static item =>
            {
                var id = item.GetProperty("player_id").GetString()!;
                var name = item.TryGetProperty("display_name", out var displayName)
                    ? displayName.GetString()
                    : null;
                name ??= item.GetProperty("name").GetString();
                return (PlayerId: id, PlayerName: name!);
            })
            .ToArray();

        var exact = players.FirstOrDefault(player =>
            string.Equals(player.PlayerName, searchTerm, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(exact.PlayerId))
        {
            return [exact];
        }

        var contains = players
            .Where(player => player.PlayerName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (contains.Length is 1)
        {
            return contains;
        }

        var ranked = players
            .Select(player => (
                Player: player,
                Score: StringSimilarity.TokenCoreSimilarity(searchTerm, player.PlayerName)))
            .OrderByDescending(static match => match.Score)
            .ToArray();

        if (ranked.Length == 0 || ranked[0].Score < 0.55)
        {
            return [];
        }

        return ranked
            .Where(match => ranked[0].Score - match.Score < 0.10)
            .Select(static match => match.Player)
            .ToArray();
    }

    private async Task<LibraryItems?> SearchAsync(
        string mediaType,
        string searchTerm,
        CancellationToken cancellationToken)
    {
        using var document = await SendAsync(
            "music/search",
            new
            {
                search_query = searchTerm,
                media_types = new[] { mediaType },
                limit = 10,
                library_only = false
            },
            cancellationToken).ConfigureAwait(false);

        var propertyName = mediaType switch
        {
            "artist" => "artists",
            "album" => "albums",
            "track" => "tracks",
            "genre" => "genres",
            "audiobook" => "audiobooks",
            _ => throw new ArgumentOutOfRangeException(nameof(mediaType), mediaType, "Unsupported media type.")
        };

        if (!document.RootElement.TryGetProperty(propertyName, out var items))
        {
            return null;
        }

        var matches = ReadItems(items);
        return matches.FirstOrDefault(item =>
                   string.Equals(item.Name, searchTerm, StringComparison.OrdinalIgnoreCase))
               ?? matches.FirstOrDefault();
    }

    private async Task<JsonDocument> SendAsync(
        string command,
        object args,
        CancellationToken cancellationToken,
        string? url = null,
        string? token = null)
    {
        var config = options.CurrentValue;
        var requestUrl = string.IsNullOrWhiteSpace(url) ? config.Url : url;
        var requestToken = string.IsNullOrWhiteSpace(token) ? config.Token : token;
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"{requestUrl.TrimEnd('/')}/api"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", requestToken);
        request.Content = JsonContent.Create(new
        {
            message_id = Guid.NewGuid().ToString("N"),
            command,
            args
        });

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<LibraryItems> ReadItems(JsonElement items)
    {
        return items.ValueKind is JsonValueKind.Array
            ? items.EnumerateArray()
                .Select(static item => new LibraryItems
                {
                    MediaType = item.GetProperty("media_type").GetString()!,
                    Uri = item.GetProperty("uri").GetString()!,
                    Name = item.GetProperty("name").GetString()!
                })
                .ToArray()
            : [];
    }

    private static string ToCollectionName(string mediaType) => mediaType switch
    {
        "artist" => "artists",
        "album" => "albums",
        "track" => "tracks",
        "audiobook" => "audiobooks",
        _ => throw new ArgumentOutOfRangeException(nameof(mediaType), mediaType, "Unsupported library media type.")
    };

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Direct Music Assistant player lookup for {SearchTerm} was ambiguous")]
    private partial void LogAmbiguousPlayer(string searchTerm);
}
