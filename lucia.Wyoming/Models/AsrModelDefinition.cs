namespace lucia.Wyoming.Models;

public sealed record AsrModelDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required ModelArchitecture Architecture { get; init; }
    public required bool IsStreaming { get; init; }
    public required string[] Languages { get; init; }
    public required long SizeBytes { get; init; }
    public required string Description { get; init; }
    public required string DownloadUrl { get; init; }
    public bool IsDefault { get; init; }
    public int MinMemoryMb { get; init; }
}
