using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace lucia.Wyoming.Wyoming;

/// <summary>
/// Writes Wyoming protocol events to a stream.
/// Thread-safe: uses a semaphore to serialize concurrent writes.
/// </summary>
public sealed class WyomingEventWriter
{
    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public WyomingEventWriter(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        _stream = stream;
    }

    /// <summary>
    /// Write a Wyoming event to the stream.
    /// </summary>
    public async Task WriteEventAsync(WyomingEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var header = BuildHeader(evt);
            var headerJson = JsonSerializer.Serialize(header, JsonOptions);
            var headerBytes = Encoding.UTF8.GetBytes($"{headerJson}\n");

            await _stream.WriteAsync(headerBytes, ct).ConfigureAwait(false);

            if (evt.Payload is { Length: > 0 } payload)
            {
                await _stream.WriteAsync(payload, ct).ConfigureAwait(false);
            }

            await _stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static WyomingEventHeader BuildHeader(WyomingEvent evt)
    {
        return new WyomingEventHeader
        {
            Type = evt.Type,
            Data = BuildEventData(evt),
            PayloadLength = evt.Payload?.Length ?? 0,
        };
    }

    private static Dictionary<string, object>? BuildEventData(WyomingEvent evt)
    {
        return evt switch
        {
            AudioStartEvent e => new Dictionary<string, object>
            {
                ["rate"] = e.Rate,
                ["width"] = e.Width,
                ["channels"] = e.Channels,
            },
            AudioChunkEvent e => new Dictionary<string, object>
            {
                ["rate"] = e.Rate,
                ["width"] = e.Width,
                ["channels"] = e.Channels,
            },
            TranscriptEvent e => BuildTranscriptData(e),
            PartialTranscriptEvent e => BuildPartialTranscriptData(e),
            DetectionEvent e => BuildDetectionData(e),
            ErrorEvent e => BuildErrorData(e),
            InfoEvent e => BuildInfoData(e),
            VoiceStartedEvent e => BuildTimestampData(e.Timestamp),
            VoiceStoppedEvent e => BuildTimestampData(e.Timestamp),
            _ => null,
        };
    }

    private static Dictionary<string, object> BuildTranscriptData(TranscriptEvent evt)
    {
        return new Dictionary<string, object>
        {
            ["text"] = evt.Text,
            ["confidence"] = evt.Confidence,
        };
    }

    private static Dictionary<string, object> BuildPartialTranscriptData(PartialTranscriptEvent evt)
    {
        return new Dictionary<string, object>
        {
            ["text"] = evt.Text,
            ["confidence"] = evt.Confidence,
            ["is_final"] = evt.IsFinal,
        };
    }

    private static Dictionary<string, object> BuildDetectionData(DetectionEvent evt)
    {
        var data = new Dictionary<string, object>
        {
            ["name"] = evt.Name,
        };

        if (evt.Timestamp.HasValue)
        {
            data["timestamp"] = evt.Timestamp.Value;
        }

        return data;
    }

    private static Dictionary<string, object> BuildErrorData(ErrorEvent evt)
    {
        var data = new Dictionary<string, object>
        {
            ["text"] = evt.Text,
        };

        if (evt.Code is not null)
        {
            data["code"] = evt.Code;
        }

        return data;
    }

    private static Dictionary<string, object>? BuildInfoData(InfoEvent evt)
    {
        var data = new Dictionary<string, object>();

        if (evt.Asr is not null)
        {
            data["asr"] = evt.Asr;
        }

        if (evt.Tts is not null)
        {
            data["tts"] = evt.Tts;
        }

        if (evt.Wake is not null)
        {
            data["wake"] = evt.Wake;
        }

        if (evt.Version is not null)
        {
            data["version"] = evt.Version;
        }

        return data.Count > 0 ? data : null;
    }

    private static Dictionary<string, object>? BuildTimestampData(long? timestamp)
    {
        return timestamp.HasValue
            ? new Dictionary<string, object> { ["timestamp"] = timestamp.Value }
            : null;
    }
}
