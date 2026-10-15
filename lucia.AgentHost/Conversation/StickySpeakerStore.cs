using lucia.Wyoming.Diarization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace lucia.AgentHost.Conversation;

/// <summary>
/// Remembers the last recognized speaker profile per conversation so short follow-up turns
/// that fail speaker verification ("The kitchen.", "Okay.") continue in the same session and
/// history instead of forking into an anonymous one. Entries expire after a configurable
/// lifetime (Wyoming:VoiceProfiles:StickySpeakerTtlSeconds); a newly recognized speaker always
/// overrides the remembered entry.
/// </summary>
public sealed class StickySpeakerStore(TimeProvider? timeProvider = null, IOptions<VoiceProfileOptions>? voiceProfileOptions = null) : IDisposable
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan _lifetime = ResolveLifetime(voiceProfileOptions?.Value);
    // ponytail: instance-local memory like VoiceTurnStore; use a shared store if the API runs on multiple replicas.
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public void Remember(string conversationId, string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        _cache.Set(conversationId, new StickyEntry(profileId, _timeProvider.GetUtcNow()),
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = _lifetime });
    }

    public string? Get(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return null;
        }

        _cache.TryGetValue(conversationId, out StickyEntry? entry);
        return entry is not null && _timeProvider.GetUtcNow() - entry.RememberedAt < _lifetime
            ? entry.ProfileId
            : null;
    }

    public void Dispose() => _cache.Dispose();

    private static TimeSpan ResolveLifetime(VoiceProfileOptions? options) =>
        options is { StickySpeakerTtlSeconds: > 0 }
            ? TimeSpan.FromSeconds(options.StickySpeakerTtlSeconds)
            : DefaultLifetime;

    private sealed record StickyEntry(string ProfileId, DateTimeOffset RememberedAt);
}
