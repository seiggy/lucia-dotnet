namespace lucia.Wyoming.Wyoming;

using System.Text.Json;

/// <summary>
/// Factory for creating typed Wyoming events from raw headers.
/// </summary>
public static class WyomingEventFactory
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static WyomingEvent Create(WyomingEventHeader header, byte[]? payload = null)
    {
        ArgumentNullException.ThrowIfNull(header);

        return header.Type switch
        {
            "audio-start" => new AudioStartEvent
            {
                Rate = GetInt32(header.Data, "rate"),
                Width = GetInt32(header.Data, "width"),
                Channels = GetInt32(header.Data, "channels"),
            },
            "audio-chunk" => new AudioChunkEvent
            {
                Payload = payload,
                Rate = GetInt32(header.Data, "rate"),
                Width = GetInt32(header.Data, "width"),
                Channels = GetInt32(header.Data, "channels"),
            },
            "audio-stop" => new AudioStopEvent(),
            "transcribe" => new TranscribeEvent
            {
                Name = GetString(header.Data, "name"),
                Language = GetString(header.Data, "language"),
            },
            "transcript" => new TranscriptEvent
            {
                Text = GetString(header.Data, "text") ?? string.Empty,
                Confidence = GetSingle(header.Data, "confidence"),
            },
            "partial-transcript" => new PartialTranscriptEvent
            {
                Text = GetString(header.Data, "text") ?? string.Empty,
                Confidence = GetSingle(header.Data, "confidence"),
                IsFinal = GetBoolean(header.Data, "is_final"),
            },
            "detect" => new DetectEvent
            {
                Names = GetStringArray(header.Data, "names"),
            },
            "detection" => new DetectionEvent
            {
                Name = GetString(header.Data, "name") ?? string.Empty,
                Timestamp = GetNullableInt64(header.Data, "timestamp"),
            },
            "not-detected" => new NotDetectedEvent(),
            "synthesize" => new SynthesizeEvent
            {
                Text = GetString(header.Data, "text") ?? string.Empty,
                Voice = GetString(header.Data, "voice"),
                Language = GetString(header.Data, "language"),
            },
            "describe" => new DescribeEvent(),
            "info" => new InfoEvent
            {
                Asr = GetRecordArray<AsrInfo>(header.Data, "asr"),
                Tts = GetRecordArray<TtsInfo>(header.Data, "tts"),
                Wake = GetRecordArray<WakeInfo>(header.Data, "wake"),
                Version = GetString(header.Data, "version"),
            },
            "error" => new ErrorEvent
            {
                Text = GetString(header.Data, "text") ?? string.Empty,
                Code = GetString(header.Data, "code"),
            },
            "voice-started" => new VoiceStartedEvent
            {
                Timestamp = GetNullableInt64(header.Data, "timestamp"),
            },
            "voice-stopped" => new VoiceStoppedEvent
            {
                Timestamp = GetNullableInt64(header.Data, "timestamp"),
            },
            _ => throw new WyomingProtocolException($"Unknown event type: {header.Type}"),
        };
    }

    private static bool GetBoolean(Dictionary<string, object>? data, string key, bool defaultValue = false)
    {
        return TryGetValue(data, key, out var value)
            ? ToBoolean(value, defaultValue)
            : defaultValue;
    }

    private static int GetInt32(Dictionary<string, object>? data, string key, int defaultValue = 0)
    {
        return TryGetValue(data, key, out var value)
            ? ToInt32(value, defaultValue)
            : defaultValue;
    }

    private static long? GetNullableInt64(Dictionary<string, object>? data, string key)
    {
        return TryGetValue(data, key, out var value)
            ? ToNullableInt64(value)
            : null;
    }

    private static float GetSingle(Dictionary<string, object>? data, string key, float defaultValue = 0)
    {
        return TryGetValue(data, key, out var value)
            ? ToSingle(value, defaultValue)
            : defaultValue;
    }

    private static string? GetString(Dictionary<string, object>? data, string key)
    {
        return TryGetValue(data, key, out var value)
            ? ToNullableString(value)
            : null;
    }

    private static string[]? GetStringArray(Dictionary<string, object>? data, string key)
    {
        if (!TryGetValue(data, key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string[] items => items,
            IEnumerable<string> items => [.. items],
            JsonElement { ValueKind: JsonValueKind.Array } element => [.. element.EnumerateArray().Select(static item => item.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : item.ToString()).Where(static item => item is not null).Select(static item => item!)],
            IEnumerable<object> items => [.. items.Select(ToNullableString).Where(static item => item is not null).Select(static item => item!)],
            _ => null,
        };
    }

    private static T[]? GetRecordArray<T>(Dictionary<string, object>? data, string key)
        where T : class
    {
        if (!TryGetValue(data, key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            T[] items => items,
            JsonElement { ValueKind: JsonValueKind.Array } element => JsonSerializer.Deserialize<T[]>(element.GetRawText(), SerializerOptions),
            IEnumerable<object> items => [.. items.Select(DeserializeRecord<T>).Where(static item => item is not null).Select(static item => item!)],
            _ => null,
        };
    }

    private static T? DeserializeRecord<T>(object value)
        where T : class
    {
        return value switch
        {
            T item => item,
            JsonElement { ValueKind: JsonValueKind.Object } element => JsonSerializer.Deserialize<T>(element.GetRawText(), SerializerOptions),
            Dictionary<string, object> dictionary => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(dictionary), SerializerOptions),
            _ => null,
        };
    }

    private static bool TryGetValue(Dictionary<string, object>? data, string key, out object? value)
    {
        if (data is not null && data.TryGetValue(key, out value))
        {
            return true;
        }

        value = null;
        return false;
    }

    private static bool ToBoolean(object? value, bool defaultValue)
    {
        return value switch
        {
            null => defaultValue,
            bool boolean => boolean,
            string text when bool.TryParse(text, out var parsed) => parsed,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            JsonElement { ValueKind: JsonValueKind.String } element when bool.TryParse(element.GetString(), out var parsed) => parsed,
            JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var number) => number != 0,
            sbyte number => number != 0,
            byte number => number != 0,
            short number => number != 0,
            ushort number => number != 0,
            int number => number != 0,
            uint number => number != 0,
            long number => number != 0,
            ulong number => number != 0,
            _ => defaultValue,
        };
    }

    private static int ToInt32(object? value, int defaultValue)
    {
        return value switch
        {
            null => defaultValue,
            int number => number,
            long number when number is >= int.MinValue and <= int.MaxValue => (int)number,
            float number when number is >= int.MinValue and <= int.MaxValue => (int)number,
            double number when number is >= int.MinValue and <= int.MaxValue => (int)number,
            decimal number when number is >= int.MinValue and <= int.MaxValue => (int)number,
            string text when int.TryParse(text, out var parsed) => parsed,
            JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var parsed) => parsed,
            JsonElement { ValueKind: JsonValueKind.String } element when int.TryParse(element.GetString(), out var parsed) => parsed,
            _ => defaultValue,
        };
    }

    private static long? ToNullableInt64(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is long longNumber)
        {
            return longNumber;
        }

        if (value is int intNumber)
        {
            return intNumber;
        }

        if (value is float floatNumber && floatNumber >= long.MinValue && floatNumber <= long.MaxValue)
        {
            return (long)floatNumber;
        }

        if (value is double doubleNumber && doubleNumber >= long.MinValue && doubleNumber <= long.MaxValue)
        {
            return (long)doubleNumber;
        }

        if (value is decimal decimalNumber && decimalNumber >= long.MinValue && decimalNumber <= long.MaxValue)
        {
            return (long)decimalNumber;
        }

        if (value is string text && long.TryParse(text, out var parsedText))
        {
            return parsedText;
        }

        if (value is JsonElement { ValueKind: JsonValueKind.Number } numberElement && numberElement.TryGetInt64(out var parsedNumber))
        {
            return parsedNumber;
        }

        if (value is JsonElement { ValueKind: JsonValueKind.String } stringElement && long.TryParse(stringElement.GetString(), out var parsedString))
        {
            return parsedString;
        }

        return null;
    }

    private static float ToSingle(object? value, float defaultValue)
    {
        if (value is null)
        {
            return defaultValue;
        }

        if (value is float floatNumber)
        {
            return floatNumber;
        }

        if (value is double doubleNumber && doubleNumber >= float.MinValue && doubleNumber <= float.MaxValue)
        {
            return (float)doubleNumber;
        }

        if (value is decimal decimalNumber)
        {
            try
            {
                return decimal.ToSingle(decimalNumber);
            }
            catch (OverflowException)
            {
                return defaultValue;
            }
        }

        if (value is int intNumber)
        {
            return intNumber;
        }

        if (value is long longNumber && longNumber >= int.MinValue && longNumber <= int.MaxValue)
        {
            return longNumber;
        }

        if (value is string text && float.TryParse(text, out var parsedText))
        {
            return parsedText;
        }

        if (value is JsonElement { ValueKind: JsonValueKind.Number } numberElement && numberElement.TryGetSingle(out var parsedNumber))
        {
            return parsedNumber;
        }

        if (value is JsonElement { ValueKind: JsonValueKind.String } stringElement && float.TryParse(stringElement.GetString(), out var parsedString))
        {
            return parsedString;
        }

        return defaultValue;
    }

    private static string? ToNullableString(object? value)
    {
        return value switch
        {
            null => null,
            string text => text,
            JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
            JsonElement element => element.ToString(),
            _ => value.ToString(),
        };
    }
}
