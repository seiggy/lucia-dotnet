using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace lucia.Agents.Providers;

/// <summary>
/// Rewrites outgoing JSON request bodies so nullable JSON Schema type arrays (for example
/// "type": ["string", "null"]) are expressed as anyOf. Gemini-compatible OpenAI endpoints
/// reject type arrays, and some proxies (litellm) silently drop enum/min/max constraints on
/// them. See https://github.com/BerriAI/litellm/issues/43325.
/// </summary>
public sealed class ToolSchemaCompatHandler : DelegatingHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post
            && request.Content is not null
            && IsJson(request.Content.Headers.ContentType?.MediaType))
        {
            var body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (body.Contains("\"tools\""))
            {
                var rewritten = NormalizeToolSchemas(body);
                if (rewritten is not null)
                {
                    request.Content = new StringContent(rewritten, Encoding.UTF8, "application/json");
                }
            }
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns a rewritten document with every JSON Schema type array converted to anyOf form,
    /// or null when the document contains no type arrays.
    /// </summary>
    public static string? NormalizeToolSchemas(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (root is null || !Rewrite(root))
        {
            return null;
        }

        return root.ToJsonString(SerializerOptions);
    }

    private static bool IsJson(string? mediaType) =>
        string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase)
        || (mediaType?.EndsWith("+json", StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool Rewrite(JsonNode node)
    {
        switch (node)
        {
            case JsonArray array:
                var changed = false;
                foreach (var element in array)
                {
                    if (element is not null && Rewrite(element))
                    {
                        changed = true;
                    }
                }

                return changed;

            case JsonObject obj:
                if (TryConvertTypeArray(obj))
                {
                    foreach (var pair in obj.ToList())
                    {
                        if (pair.Value is not null && Rewrite(pair.Value))
                        {
                            return true;
                        }
                    }

                    return true;
                }

                var objChanged = false;
                foreach (var pair in obj.ToList())
                {
                    if (pair.Value is not null && Rewrite(pair.Value))
                    {
                        objChanged = true;
                    }
                }

                return objChanged;
        }

        return false;
    }

    private static bool TryConvertTypeArray(JsonObject obj)
    {
        if (obj["type"] is not JsonArray types || types.Count == 0 || obj.ContainsKey("anyOf"))
        {
            return false;
        }

        var typeStrings = new List<string>(types.Count);
        foreach (var element in types)
        {
            if (element is not JsonValue value || !value.TryGetValue<string>(out var typeName))
            {
                return false;
            }

            typeStrings.Add(typeName);
        }

        // Detach sibling keywords (enum, maxLength, ...) so they can be reattached to the non-null branches.
        var siblings = new List<KeyValuePair<string, JsonNode>>();
        foreach (var pair in obj.ToList())
        {
            if (pair.Key == "type")
            {
                continue;
            }

            var node = pair.Value;
            if (node is not null)
            {
                obj.Remove(pair.Key);
                siblings.Add(new KeyValuePair<string, JsonNode>(pair.Key, node));
            }
        }

        var firstNonNullIndex = -1;
        for (var i = 0; i < typeStrings.Count; i++)
        {
            if (!string.Equals(typeStrings[i], "null", StringComparison.Ordinal))
            {
                firstNonNullIndex = i;
                break;
            }
        }

        var branches = new JsonArray();
        for (var i = 0; i < typeStrings.Count; i++)
        {
            var branch = new JsonObject { ["type"] = typeStrings[i] };
            if (!string.Equals(typeStrings[i], "null", StringComparison.Ordinal))
            {
                foreach (var sibling in siblings)
                {
                    // The first non-null branch takes the detached instances; later branches get clones.
                    branch[sibling.Key] = i == firstNonNullIndex ? sibling.Value : sibling.Value.DeepClone();
                }
            }

            branches.Add(branch);
        }

        if (firstNonNullIndex < 0)
        {
            // Degenerate all-null type array: keep the keywords on the parent instead of dropping them.
            foreach (var sibling in siblings)
            {
                obj[sibling.Key] = sibling.Value;
            }
        }

        obj.Remove("type");
        obj["anyOf"] = branches;
        return true;
    }
}
