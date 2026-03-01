namespace lucia.Agents.Services;

/// <summary>
/// Flattened view of a plugin available for installation from a repository.
/// </summary>
public sealed class AvailablePlugin
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public List<string> Tags { get; set; } = [];
    public required string PluginPath { get; set; }
    public string? Homepage { get; set; }
    public required string RepositoryId { get; set; }
    public required string RepositoryName { get; set; }
}
