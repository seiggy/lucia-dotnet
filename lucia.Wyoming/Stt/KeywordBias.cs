namespace lucia.Wyoming.Stt;

/// <summary>
/// A keyword with an associated bias weight for speech recognition decoding.
/// Higher weights push the decoder toward recognizing this keyword when acoustically plausible.
/// </summary>
/// <param name="Keyword">The word or phrase to bias toward.</param>
/// <param name="Weight">Logit boost (e.g., 2.0 for names, 0.5 for common HA words).</param>
public readonly record struct KeywordBias(string Keyword, float Weight);
