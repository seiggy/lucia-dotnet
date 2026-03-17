using System.Text.Json;

namespace lucia.Wyoming.Stt;

/// <summary>
/// Decodes token IDs to text using a HuggingFace tokenizer.json vocabulary.
/// Supports ByteLevel (GPT-2/Whisper) and SentencePiece/Metaspace decoders.
/// </summary>
public sealed class GraniteTokenizer
{
    private static readonly Dictionary<char, byte> UnicodeToByte = BuildUnicodeToByte();
    private static readonly Dictionary<byte, char> ByteToUnicode = BuildByteToUnicode();

    private readonly Dictionary<int, string> _idToToken;
    private readonly HashSet<int> _specialTokenIds;
    private readonly string _decoderType;

    public int EosTokenId { get; private set; }
    public int BosTokenId { get; private set; }
    public int PadTokenId { get; private set; }

    public GraniteTokenizer(string tokenizerJsonPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenizerJsonPath);

        var json = File.ReadAllText(tokenizerJsonPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        _idToToken = new Dictionary<int, string>();
        _specialTokenIds = new HashSet<int>();

        ParseVocab(root);
        ParseAddedTokens(root);

        _decoderType = root.TryGetProperty("decoder", out var decoder)
            && decoder.TryGetProperty("type", out var dtype)
            ? dtype.GetString() ?? "ByteLevel"
            : "ByteLevel";

        EosTokenId = FindTokenId("<|endoftext|>", "</s>", "<eos>", "<|eot_id|>");
        BosTokenId = FindTokenId("<|startoftranscript|>", "<s>", "<bos>", "<|begin_of_text|>");
        PadTokenId = FindTokenId("<pad>", "<|pad|>");

        // Also try reading special token IDs from the config.json next to tokenizer.json
        var configPath = Path.Combine(Path.GetDirectoryName(tokenizerJsonPath) ?? "", "config.json");
        if (File.Exists(configPath))
        {
            TryReadConfigTokenIds(configPath);
        }
    }

    /// <summary>
    /// Decodes a sequence of token IDs into text, skipping special tokens.
    /// </summary>
    public string Decode(IReadOnlyList<int> tokenIds)
    {
        var tokens = new List<string>();
        foreach (var id in tokenIds)
        {
            if (_specialTokenIds.Contains(id)) continue;
            if (_idToToken.TryGetValue(id, out var token))
                tokens.Add(token);
        }

        var text = string.Join("", tokens);

        text = _decoderType switch
        {
            "ByteLevel" => DecodeByteLevel(text),
            "Metaspace" => text.Replace('\u2581', ' '),
            _ => text.Replace('\u2581', ' '),
        };

        return text.Trim();
    }

    public int VocabSize => _idToToken.Count;

    /// <summary>
    /// Encodes text to token IDs using byte-level lookup.
    /// This is a simplified encoder that maps UTF-8 bytes to the base BPE vocabulary.
    /// It won't apply BPE merges but the model still understands the output.
    /// </summary>
    public int[] Encode(string text)
    {
        if (string.IsNullOrEmpty(text)) return [];

        // Try exact token match first (for special tokens)
        var tokenToId = new Dictionary<string, int>();
        foreach (var (id, token) in _idToToken)
            tokenToId.TryAdd(token, id);

        // Greedy longest-match encoding using vocab
        var tokens = new List<int>();
        var i = 0;
        while (i < text.Length)
        {
            var bestLen = 0;
            var bestId = -1;

            // Try longest match from vocab (up to 20 chars)
            for (var len = Math.Min(20, text.Length - i); len >= 1; len--)
            {
                var candidate = text.Substring(i, len);
                if (tokenToId.TryGetValue(candidate, out var id))
                {
                    bestLen = len;
                    bestId = id;
                    break;
                }
            }

            if (bestId >= 0)
            {
                tokens.Add(bestId);
                i += bestLen;
            }
            else
            {
                // Fall back to byte-level encoding for this character
                var bytes = System.Text.Encoding.UTF8.GetBytes(text[i].ToString());
                foreach (var b in bytes)
                {
                    var byteChar = ByteToUnicode.TryGetValue(b, out var ch) ? ch.ToString() : text[i].ToString();
                    if (tokenToId.TryGetValue(byteChar, out var id))
                        tokens.Add(id);
                }
                i++;
            }
        }

        return tokens.ToArray();
    }

    /// <summary>
    /// Finds token IDs whose decoded text matches a keyword, either as an exact word
    /// or as a BPE sub-token that starts a keyword. Returns a dictionary of token ID → bias weight.
    /// </summary>
    public Dictionary<int, float> FindKeywordTokenWeights(IReadOnlyList<(string keyword, float weight)> keywordsWithWeights)
    {
        var result = new Dictionary<int, float>();
        foreach (var (id, token) in _idToToken)
        {
            if (_specialTokenIds.Contains(id)) continue;

            var decoded = _decoderType == "ByteLevel"
                ? DecodeByteLevel(token).Trim()
                : token.Replace('\u2581', ' ').Trim();

            if (string.IsNullOrEmpty(decoded)) continue;
            var trimmed = decoded.TrimStart();

            foreach (var (keyword, weight) in keywordsWithWeights)
            {
                // Exact whole-word match (case-insensitive)
                if (string.Equals(trimmed, keyword, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(decoded, keyword, StringComparison.OrdinalIgnoreCase))
                {
                    if (!result.TryGetValue(id, out var existing) || weight > existing)
                        result[id] = weight;
                    break;
                }

                // Sub-token match: the keyword starts with this token's text.
                // This handles BPE splits like "Z" + "ack" for "Zack".
                // Use a fraction of the weight for sub-tokens to avoid over-biasing.
                if (trimmed.Length >= 1 && trimmed.Length < keyword.Length
                    && keyword.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    var subWeight = weight * 0.6f;
                    if (!result.TryGetValue(id, out var existing) || subWeight > existing)
                        result[id] = subWeight;
                    break;
                }
            }
        }
        return result;
    }

    private void ParseVocab(JsonElement root)
    {
        if (!root.TryGetProperty("model", out var model)) return;
        if (!model.TryGetProperty("vocab", out var vocab)) return;

        foreach (var prop in vocab.EnumerateObject())
        {
            _idToToken[prop.Value.GetInt32()] = prop.Name;
        }
    }

    private void ParseAddedTokens(JsonElement root)
    {
        if (!root.TryGetProperty("added_tokens", out var addedTokens)) return;

        foreach (var token in addedTokens.EnumerateArray())
        {
            var id = token.GetProperty("id").GetInt32();
            var content = token.GetProperty("content").GetString() ?? "";
            var isSpecial = token.TryGetProperty("special", out var sp) && sp.GetBoolean();

            _idToToken[id] = content;
            if (isSpecial) _specialTokenIds.Add(id);
        }
    }

    private void TryReadConfigTokenIds(string configPath)
    {
        try
        {
            var json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (EosTokenId < 0 && root.TryGetProperty("eos_token_id", out var eos) && eos.ValueKind == JsonValueKind.Number)
                EosTokenId = eos.GetInt32();
            if (BosTokenId < 0 && root.TryGetProperty("bos_token_id", out var bos) && bos.ValueKind == JsonValueKind.Number)
                BosTokenId = bos.GetInt32();
            if (BosTokenId < 0 && root.TryGetProperty("decoder_start_token_id", out var decStart) && decStart.ValueKind == JsonValueKind.Number)
                BosTokenId = decStart.GetInt32();
        }
        catch
        {
            // Non-critical: config.json parsing failure doesn't block tokenizer operation
        }
    }

    private int FindTokenId(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            foreach (var (id, token) in _idToToken)
            {
                if (string.Equals(token, candidate, StringComparison.Ordinal))
                    return id;
            }
        }
        return -1;
    }

    /// <summary>
    /// Decodes GPT-2 style byte-level BPE tokens back to UTF-8 text.
    /// </summary>
    private static string DecodeByteLevel(string text)
    {
        var bytes = new List<byte>(text.Length);
        foreach (var ch in text)
        {
            if (UnicodeToByte.TryGetValue(ch, out var b))
                bytes.Add(b);
            else
                bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(ch.ToString()));
        }
        return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
    }

    /// <summary>
    /// Builds the reverse GPT-2 byte-to-unicode mapping.
    /// </summary>
    private static Dictionary<char, byte> BuildUnicodeToByte()
    {
        var bytesToUnicode = new Dictionary<byte, char>();

        // Printable ASCII and Latin-1 supplement ranges
        for (var b = 33; b <= 126; b++) bytesToUnicode[(byte)b] = (char)b;
        for (var b = 161; b <= 172; b++) bytesToUnicode[(byte)b] = (char)b;
        for (var b = 174; b <= 255; b++) bytesToUnicode[(byte)b] = (char)b;

        // Non-printable bytes get mapped to Unicode code points starting at 256
        var n = 0;
        for (var b = 0; b < 256; b++)
        {
            if (!bytesToUnicode.ContainsKey((byte)b))
            {
                bytesToUnicode[(byte)b] = (char)(256 + n);
                n++;
            }
        }

        return bytesToUnicode.ToDictionary(kv => kv.Value, kv => kv.Key);
    }

    private static Dictionary<byte, char> BuildByteToUnicode()
    {
        var map = new Dictionary<byte, char>();
        for (var b = 33; b <= 126; b++) map[(byte)b] = (char)b;
        for (var b = 161; b <= 172; b++) map[(byte)b] = (char)b;
        for (var b = 174; b <= 255; b++) map[(byte)b] = (char)b;

        var n = 0;
        for (var b = 0; b < 256; b++)
        {
            if (!map.ContainsKey((byte)b))
            {
                map[(byte)b] = (char)(256 + n);
                n++;
            }
        }
        return map;
    }
}
