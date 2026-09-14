using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace lucia.Wyoming.Diarization;

/// <summary>
/// Transfers an utterance from Wyoming to the conversation API without putting audio or identity in the transcript.
/// </summary>
public sealed partial class VoiceTurnStore(TimeProvider? timeProvider = null) : IDisposable
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    // ponytail: instance-local handoff; use a shared expiring store if STT and the API run on separate replicas.
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 32 * 1024 * 1024 });
    private readonly object _consumeLock = new();

    public string Capture(string text, ReadOnlyMemory<float> audio, int sampleRate, SpeakerIdentification? speaker)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        if (audio.Length > (long)sampleRate * 60)
        {
            throw new ArgumentOutOfRangeException(nameof(audio), "Voice turns cannot exceed one minute.");
        }

        var token = Guid.NewGuid().ToString("N");
        var turn = new VoiceTurn(text, audio.ToArray(), sampleRate, speaker, _timeProvider.GetUtcNow());
        _cache.Set(token, turn, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = Lifetime,
            Size = (long)audio.Length * sizeof(float) + text.Length * sizeof(char) + 256,
        });
        return $"<lucia-voice token=\"{token}\" />{text}";
    }

    public static (string? Token, string Text) Parse(string text)
    {
        var match = VoiceTag().Match(text);
        return match.Success
            ? (match.Groups[1].Value, text[match.Length..].TrimStart())
            : (null, text);
    }

    public VoiceTurn? Consume(string token)
    {
        lock (_consumeLock)
        {
            _cache.TryGetValue(token, out VoiceTurn? turn);
            _cache.Remove(token);
            return turn is not null && _timeProvider.GetUtcNow() - turn.CapturedAt < Lifetime ? turn : null;
        }
    }

    public void Dispose() => _cache.Dispose();

    [GeneratedRegex("^<lucia-voice token=\"([a-f0-9]{32})\"\\s*/>", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex VoiceTag();
}
