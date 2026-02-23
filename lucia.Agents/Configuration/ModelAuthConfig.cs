namespace lucia.Agents.Configuration;

/// <summary>
/// Authentication configuration for a model provider connection.
/// </summary>
public sealed class ModelAuthConfig
{
    /// <summary>
    /// Authentication method: "api-key", "azure-credential", or "none".
    /// </summary>
    public string AuthType { get; set; } = "none";

    /// <summary>
    /// API key or bearer token for the provider. Stored as plaintext (single-tenant).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// When true, uses DefaultAzureCredential for Azure-hosted scenarios.
    /// </summary>
    public bool UseDefaultCredentials { get; set; }
}
