namespace lucia.Wyoming.Wyoming;

public sealed record TtsVoiceInfo
{
    public string Name { get; init; } = string.Empty;

    public string Language { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;
}
