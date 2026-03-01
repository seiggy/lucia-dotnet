using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace lucia.Agents.Configuration;

/// <summary>
/// MongoDB document representing a configured plugin repository.
/// Caches the fetched manifest for dashboard queries.
/// </summary>
public sealed class PluginRepositoryDefinition
{
    public const string CollectionName = "pluginRepositories";
    public const string DatabaseName = "luciaconfig";

    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = default!;

    public string Name { get; set; } = default!;

    /// <summary>
    /// Repository type: "git" for remote Git repositories, "local" for local filesystem.
    /// Determines which <see cref="lucia.Agents.Abstractions.IPluginRepositorySource"/> handles this repo.
    /// </summary>
    public string Type { get; set; } = "git";

    /// <summary>
    /// For "git" repositories: the remote URL (e.g. "https://github.com/user/repo").
    /// For "local" repositories: the absolute path to the local directory.
    /// </summary>
    public string? Url { get; set; }

    public string Branch { get; set; } = "main";

    /// <summary>
    /// How plugin archives are downloaded from git repositories.
    /// <list type="bullet">
    ///   <item><c>"release"</c> (default) — GitHub Releases API: look for per-plugin asset, fall back to release zipball.</item>
    ///   <item><c>"tag"</c> — Archive at <c>refs/tags/{Branch}</c>.</item>
    ///   <item><c>"branch"</c> — Archive at <c>refs/heads/{Branch}</c>.</item>
    /// </list>
    /// Ignored for "local" repositories.
    /// </summary>
    public string BlobSource { get; set; } = "release";

    public string ManifestPath { get; set; } = "lucia-plugins.json";

    public bool Enabled { get; set; } = true;

    public DateTime? LastSyncedAt { get; set; }

    /// <summary>
    /// Cached manifest entries from the last successful sync.
    /// </summary>
    public List<PluginManifestEntry> CachedPlugins { get; set; } = [];
}
