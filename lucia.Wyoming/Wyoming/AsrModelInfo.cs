namespace lucia.Wyoming.Wyoming;

public sealed record AsrModelInfo
{
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string[] Languages { get; init; } = [];

    public bool Installed { get; init; }

    public Attribution Attribution { get; init; } = new();
}
