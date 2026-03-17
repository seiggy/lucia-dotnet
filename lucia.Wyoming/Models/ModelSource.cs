namespace lucia.Wyoming.Models;

/// <summary>
/// Identifies the origin of a model definition — curated sherpa-onnx releases or Hugging Face Hub.
/// </summary>
public enum ModelSource
{
    SherpaOnnx,
    HuggingFace,
}
