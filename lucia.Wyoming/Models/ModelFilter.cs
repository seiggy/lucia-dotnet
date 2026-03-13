namespace lucia.Wyoming.Models;

public sealed record ModelFilter
{
    public bool? StreamingOnly { get; init; }
    public string? Language { get; init; }
    public ModelArchitecture? Architecture { get; init; }
    public int? MaxSizeMb { get; init; }
    public bool? InstalledOnly { get; init; }
}
