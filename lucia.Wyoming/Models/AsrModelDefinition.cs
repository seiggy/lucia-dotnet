namespace lucia.Wyoming.Models;

/// <summary>
/// STT-specific model definition extending the base Wyoming model with ASR metadata.
/// </summary>
public sealed record AsrModelDefinition : WyomingModelDefinition
{
    public required ModelArchitecture Architecture { get; init; }
    public required bool IsStreaming { get; init; }

    /// <summary>
    /// Whether this model is compatible with sherpa-onnx OnlineRecognizer.
    /// </summary>
    public bool IsOnlineCompatible => Architecture switch
    {
        ModelArchitecture.ZipformerTransducer => IsStreaming,
        ModelArchitecture.ZipformerCtc => IsStreaming,
        ModelArchitecture.Paraformer => IsStreaming,
        ModelArchitecture.Conformer => IsStreaming,
        _ => false,
    };
}
