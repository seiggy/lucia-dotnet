using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace lucia.Agents.Training;

/// <summary>Sanitizes tool-result previews before persistence and again before export.</summary>
public sealed class ToolResultCapturePolicy
{
    private const int MaxInputBytes = 65536;
    private readonly int _maxBytes;
    private readonly Regex[]? _patterns;
    private static readonly HashSet<string> SecretKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "apikey", "token", "accesstoken", "refreshtoken", "secret", "clientsecret",
        "password", "authorization", "cookie", "setcookie", "privatekey"
    };

    public ToolResultCapturePolicy(TraceCaptureOptions options)
    {
        _maxBytes = options.MaxToolResultBytes;
        try
        {
            _patterns = options.RedactionPatterns?.Select(pattern => new Regex(
                pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))).ToArray();
        }
        catch (ArgumentException)
        {
            // An invalid sanitizer must never turn into raw capture.
            _patterns = null;
        }
    }

    /// <summary>
    /// Accepts text and JSON only. Oversized input, invalid policy, malformed JSON and
    /// redaction failures suppress the preview. Does not call arbitrary object ToString methods.
    /// </summary>
    public string? Sanitize(object? value, out bool truncated)
    {
        truncated = false;
        if (_patterns is null || _maxBytes is < 1 or > MaxInputBytes)
            return null;

        try
        {
            var text = value switch
            {
                string input => input,
                JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
                JsonElement element => element.GetRawText(),
                _ => null
            };
            if (text is null || text.Length > MaxInputBytes || Encoding.UTF8.GetByteCount(text) > MaxInputBytes)
                return null;

            var trimmed = text.AsSpan().TrimStart();
            if (!trimmed.IsEmpty && trimmed[0] is '{' or '[')
            {
                var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { MaxDepth = 32 });
                text = RedactSecretProperties(node)!.ToJsonString();
            }

            text = ApplyPatterns(text);

            var preview = new StringBuilder(Math.Min(text.Length, _maxBytes));
            var bytes = 0;
            foreach (var rune in text.EnumerateRunes())
            {
                if (bytes + rune.Utf8SequenceLength > _maxBytes)
                {
                    truncated = true;
                    break;
                }
                preview.Append(rune.ToString());
                bytes += rune.Utf8SequenceLength;
            }
            return preview.ToString();
        }
        catch (Exception ex) when (ex is JsonException or RegexMatchTimeoutException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private string ApplyPatterns(string text)
    {
        foreach (var pattern in _patterns!)
            text = pattern.Replace(text, "[REDACTED]");
        return text;
    }

    private JsonNode? RedactSecretProperties(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToList())
            {
                var key = property.Key.Replace("_", "").Replace("-", "");
                if (SecretKeys.Contains(key))
                    obj[property.Key] = "[REDACTED]";
                else
                {
                    var redacted = RedactSecretProperties(property.Value);
                    if (!ReferenceEquals(redacted, property.Value))
                        obj[property.Key] = redacted;
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var redacted = RedactSecretProperties(array[i]);
                if (!ReferenceEquals(redacted, array[i]))
                    array[i] = redacted;
            }
        }
        else if (node is JsonValue value && value.TryGetValue<string>(out var text))
            return JsonValue.Create(ApplyPatterns(text));

        return node;
    }
}
