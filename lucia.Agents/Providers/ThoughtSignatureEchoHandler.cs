using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace lucia.Agents.Providers;

/// <summary>
/// Echoes Gemini thought signatures across multi-round tool-call conversations. Gemini thinking
/// models reject replayed assistant tool calls that lack the thought_signature produced when the
/// call was first made, and OpenAI-compatible client stacks (such as Microsoft.Extensions.AI) drop
/// the field during POCO round-trips. This handler captures signatures from responses and re-injects
/// them into subsequent requests for the same conversation. Non-streaming JSON chat traffic only;
/// other payloads pass through untouched.
/// </summary>
public sealed class ThoughtSignatureEchoHandler : DelegatingHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private const string SignatureField = "thought_signature";

    private readonly ThoughtSignatureStore _store;

    public ThoughtSignatureEchoHandler(ThoughtSignatureStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Creates a handler that terminates an HTTP chain: it echoes thought signatures and forwards
    /// the request over the network via <see cref="SocketsHttpHandler"/>.
    /// </summary>
    public static ThoughtSignatureEchoHandler CreateForNetwork(ThoughtSignatureStore store) =>
        new(store) { InnerHandler = new SocketsHttpHandler() };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? outboundBody = null;
        if (request.Method == HttpMethod.Post
            && request.Content is not null
            && IsJson(request.Content.Headers.ContentType?.MediaType))
        {
            var body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (body.Contains("\"tool_calls\""))
            {
                var rewritten = TryInjectSignatures(body, _store);
                if (rewritten is not null)
                {
                    request.Content = new StringContent(rewritten, Encoding.UTF8, "application/json");
                    body = rewritten;
                }
            }

            outboundBody = body;
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (outboundBody is not null
            && response.Content is not null
            && IsJson(response.Content.Headers.ContentType?.MediaType))
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (responseBody.Contains(SignatureField))
            {
                CaptureSignatures(outboundBody, responseBody, _store);
            }

            response.Content = new StringContent(responseBody, Encoding.UTF8, "application/json");
        }

        return response;
    }

    /// <summary>
    /// Computes a stable key for a message prefix preceding an assistant tool-call message.
    /// thought_signature fields are stripped before hashing so the key matches whether or not the
    /// client preserved previously injected signatures in its reconstructed history.
    /// </summary>
    public static string ComputeConversationKey(JsonArray? messages)
    {
        var normalized = new JsonArray();
        if (messages is not null)
        {
            foreach (var element in messages)
            {
                if (element is not null)
                {
                    normalized.Add(StripSignatureFields(element.DeepClone()));
                }
            }
        }

        return Hash(normalized.ToJsonString(SerializerOptions));
    }

    /// <summary>
    /// Computes a stable key for a tool call from its id (when present), function name, and
    /// canonically ordered arguments. The call content is always part of the key so a signature is
    /// never replayed onto a different logical call, even when ids collide across rounds.
    /// </summary>
    public static string ComputeToolCallKey(JsonObject toolCall)
    {
        ArgumentNullException.ThrowIfNull(toolCall);

        var id = (string?)toolCall["id"];
        var function = toolCall["function"] as JsonObject;
        var name = (string?)function?["name"] ?? string.Empty;
        var arguments = function?["arguments"] switch
        {
            JsonValue value when value.TryGetValue<string>(out var text) => text,
            _ => null,
        };

        return string.IsNullOrEmpty(id)
            ? "name:" + name + "|args:" + CanonicalizeArguments(arguments)
            : "id:" + id + "|name:" + name + "|args:" + CanonicalizeArguments(arguments);
    }

    private static string? TryInjectSignatures(string body, ThoughtSignatureStore store)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }

        if (root is not JsonObject document || document["messages"] is not JsonArray messages)
        {
            return null;
        }

        var modified = false;
        for (var index = 0; index < messages.Count; index++)
        {
            if (messages[index] is not JsonObject message
                || !string.Equals((string?)message["role"], "assistant", StringComparison.Ordinal)
                || message["tool_calls"] is not JsonArray toolCalls)
            {
                continue;
            }

            var prefix = new JsonArray();
            for (var prior = 0; prior < index; prior++)
            {
                var priorMessage = messages[prior];
                if (priorMessage is not null)
                {
                    prefix.Add(priorMessage.DeepClone());
                }
            }

            var conversationKey = ComputeConversationKey(prefix);
            foreach (var toolCall in toolCalls.OfType<JsonObject>())
            {
                if (toolCall[SignatureField] is not null
                    || !store.TryGet(conversationKey, ComputeToolCallKey(toolCall), out var signature))
                {
                    continue;
                }

                toolCall[SignatureField] = signature;
                modified = true;
            }
        }

        return modified ? document.ToJsonString(SerializerOptions) : null;
    }

    private static void CaptureSignatures(string outboundBody, string responseBody, ThoughtSignatureStore store)
    {
        JsonNode? responseRoot;
        try
        {
            responseRoot = JsonNode.Parse(responseBody);
        }
        catch (JsonException)
        {
            return;
        }

        if (responseRoot is not JsonObject document || document["choices"] is not JsonArray choices)
        {
            return;
        }

        var conversationKey = ComputeConversationKey(ExtractMessages(outboundBody));
        foreach (var choice in choices.OfType<JsonObject>())
        {
            if (choice["message"] is not JsonObject message || message["tool_calls"] is not JsonArray toolCalls)
            {
                continue;
            }

            foreach (var toolCall in toolCalls.OfType<JsonObject>())
            {
                var signature = ExtractSignature(toolCall);
                if (signature is not null)
                {
                    store.TryAdd(conversationKey, ComputeToolCallKey(toolCall), signature);
                }
            }
        }
    }

    private static string? ExtractSignature(JsonObject toolCall)
    {
        var flat = (string?)toolCall[SignatureField];
        if (flat is not null)
        {
            return flat;
        }

        var google = (toolCall["extra_content"] as JsonObject)?["google"] as JsonObject;
        return (string?)google?[SignatureField];
    }

    private static JsonArray? ExtractMessages(string body)
    {
        try
        {
            var document = JsonNode.Parse(body);
            return document is JsonObject root ? root["messages"] as JsonArray : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonNode StripSignatureFields(JsonNode node)
    {
        if (node is not JsonObject obj || obj["tool_calls"] is not JsonArray toolCalls)
        {
            return node;
        }

        foreach (var toolCall in toolCalls.OfType<JsonObject>())
        {
            toolCall.Remove(SignatureField);
            if (toolCall["extra_content"] is JsonObject extra && extra["google"] is JsonObject google)
            {
                google.Remove(SignatureField);
            }
        }

        return node;
    }

    private static string CanonicalizeArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return string.Empty;
        }

        try
        {
            return Canonicalize(JsonNode.Parse(arguments));
        }
        catch (JsonException)
        {
            return arguments;
        }
    }

    private static string Canonicalize(JsonNode? node) => node switch
    {
        null => "null",
        JsonObject obj => "{" + string.Join(",", obj
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => JsonSerializer.Serialize(pair.Key) + ":" + Canonicalize(pair.Value))) + "}",
        JsonArray array => "[" + string.Join(",", array.Select(Canonicalize)) + "]",
        _ => node.ToJsonString(SerializerOptions),
    };

    private static bool IsJson(string? mediaType) =>
        string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase)
        || (mediaType?.EndsWith("+json", StringComparison.OrdinalIgnoreCase) ?? false);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
