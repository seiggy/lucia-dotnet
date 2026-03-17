namespace lucia.Wyoming.Models;

/// <summary>
/// Configuration for Hugging Face Hub integration.
/// The presence of a non-empty <see cref="ApiToken"/> enables HF model discovery and download.
/// </summary>
public sealed class HuggingFaceOptions
{
    public const string SectionName = "Wyoming:HuggingFace";

    /// <summary>
    /// Hugging Face API token (User Access Token).
    /// When set, enables browsing and downloading ONNX models from the onnx-community organization.
    /// </summary>
    public string ApiToken { get; set; } = string.Empty;

    /// <summary>Whether Hugging Face integration is effectively enabled.</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(ApiToken);
}
