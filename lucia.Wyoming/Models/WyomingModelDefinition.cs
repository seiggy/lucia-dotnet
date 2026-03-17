namespace lucia.Wyoming.Models;

/// <summary>
/// Generalized model definition for any Wyoming engine type.
/// Engine-specific subtypes (e.g. <see cref="AsrModelDefinition"/>) extend this with additional metadata.
/// </summary>
public record WyomingModelDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required EngineType EngineType { get; init; }
    public required string Description { get; init; }
    public required string[] Languages { get; init; }
    public required long SizeBytes { get; init; }
    public required string DownloadUrl { get; init; }
    public bool IsDefault { get; init; }
    public int MinMemoryMb { get; init; }

    /// <summary>
    /// Whether the download URL points to an archive (tar.bz2, tar.gz, zip) or a single file (.onnx).
    /// </summary>
    public bool IsArchive { get; init; } = true;

    /// <summary>
    /// The origin of this model definition — curated sherpa-onnx or Hugging Face Hub.
    /// </summary>
    public ModelSource Source { get; init; } = ModelSource.SherpaOnnx;

    /// <summary>
    /// Hugging Face repository ID (e.g. "onnx-community/whisper-tiny"). Only set for HF-sourced models.
    /// </summary>
    public string? RepoId { get; init; }

    /// <summary>
    /// Last modification timestamp from the Hugging Face Hub. Used for update detection.
    /// </summary>
    public DateTimeOffset? LastModified { get; init; }
}
