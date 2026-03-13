namespace lucia.Wyoming.WakeWord;

/// <summary>
/// Tokenizes plain text wake word phrases into model token sequences.
/// Uses greedy longest-match against the model vocabulary.
/// </summary>
public sealed class WakeWordTokenizer
{
    private readonly Dictionary<string, int> _vocabulary = new();
    private bool _loaded;

    public bool IsLoaded => _loaded;

    /// <summary>
    /// Load vocabulary from the model's tokens.txt file.
    /// Each line: "token index" or just "token" (index = line number).
    /// </summary>
    public void LoadVocabulary(string tokensFilePath)
    {
        if (!File.Exists(tokensFilePath))
        {
            throw new FileNotFoundException($"Tokens file not found: {tokensFilePath}");
        }

        var lines = File.ReadAllLines(tokensFilePath);
        for (var i = 0; i < lines.Length; i++)
        {
            var parts = lines[i].Split(' ', 2);
            var token = parts[0];
            if (!string.IsNullOrEmpty(token))
            {
                _vocabulary[token] = i;
            }
        }

        _loaded = true;
    }

    /// <summary>
    /// Convert a plain text phrase to model token sequence.
    /// Falls back to character-level tokenization if BPE tokens not found.
    /// </summary>
    public string Tokenize(string phrase)
    {
        if (!_loaded)
        {
            throw new InvalidOperationException("Vocabulary not loaded. Call LoadVocabulary first.");
        }

        var normalized = phrase.Trim().ToUpperInvariant();
        var tokens = new List<string>();

        foreach (var word in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var wordWithPrefix = "▁" + word;
            if (_vocabulary.ContainsKey(wordWithPrefix))
            {
                tokens.Add(wordWithPrefix);
            }
            else
            {
                var first = true;
                foreach (var c in word)
                {
                    var charToken = first ? "▁" + c : c.ToString();
                    tokens.Add(charToken);
                    first = false;
                }
            }
        }

        return string.Join(" ", tokens);
    }
}
