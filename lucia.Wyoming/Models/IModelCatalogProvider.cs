namespace lucia.Wyoming.Models;

/// <summary>
/// Provides model definitions for a specific source (e.g., sherpa-onnx built-in catalog or Hugging Face Hub).
/// </summary>
public interface IModelCatalogProvider
{
    /// <summary>The source this provider represents.</summary>
    ModelSource Source { get; }

    /// <summary>
    /// Returns available models for the specified engine type.
    /// </summary>
    Task<IReadOnlyList<WyomingModelDefinition>> GetModelsAsync(
        EngineType engineType,
        CancellationToken ct = default);

    /// <summary>
    /// Looks up a single model by ID, or returns null if not found.
    /// </summary>
    Task<WyomingModelDefinition?> GetModelByIdAsync(
        EngineType engineType,
        string modelId,
        CancellationToken ct = default);
}
