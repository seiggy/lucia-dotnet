using System.Diagnostics;
using System.Text.Json;

namespace lucia.Agents.Skills;

/// <summary>Reads Home Assistant state attributes that integrations report in inconsistent shapes.</summary>
public static class AttributeParser
{
    /// <summary>
    /// Reads a JSON array attribute as a list of strings. Numbers keep their text, so a SmartThings device
    /// that reports fan modes as <c>[4,1,2,3]</c> yields <c>"4","1","2","3"</c>. A missing key, a non-array
    /// value, or text that is not valid JSON yields an empty list.
    /// </summary>
    public static List<string> ParseStringList(IReadOnlyDictionary<string, object> attributes, string key)
    {
        if (!attributes.TryGetValue(key, out var value))
            return [];

        try
        {
            if (value is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind != JsonValueKind.Array)
                    return [];

                return jsonElement.EnumerateArray()
                    .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText())
                    .OfType<string>()
                    .ToList();
            }

            // Fallback: the value may be a string holding a JSON array.
            var json = value?.ToString() ?? "[]";
            var modes = JsonSerializer.Deserialize<JsonElement>(json);
            if (modes.ValueKind == JsonValueKind.Array)
            {
                return modes.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : e.GetRawText())
                    .ToList();
            }
        }
        catch (JsonException ex)
        {
            Trace.TraceWarning("Failed to parse attribute '{0}' as a string list: {1}", key, ex.Message);
        }

        return [];
    }
}
