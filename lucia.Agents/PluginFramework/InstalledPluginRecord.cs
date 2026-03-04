using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace lucia.Agents.PluginFramework;

/// <summary>
/// MongoDB document tracking an installed plugin's metadata.
/// The source of truth for enabled/disabled state is <c>lucia-plugins.json</c>;
/// this record caches metadata for dashboard display.
/// </summary>
public sealed class InstalledPluginRecord
{
    public const string CollectionName = "installedPlugins";
    public const string DatabaseName = "luciaconfig";

    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = default!;

    public string Name { get; set; } = default!;

    public string? Version { get; set; }

    /// <summary>
    /// "bundled" for built-in plugins, or the repository ID for downloaded plugins.
    /// </summary>
    public string Source { get; set; } = "bundled";

    public string? RepositoryId { get; set; }

    public string? Description { get; set; }

    public string? Author { get; set; }

    public string PluginPath { get; set; } = default!;

    public bool Enabled { get; set; } = true;

    public DateTime InstalledAt { get; set; } = DateTime.UtcNow;
}
