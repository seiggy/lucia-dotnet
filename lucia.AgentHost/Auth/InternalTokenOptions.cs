namespace lucia.AgentHost.Auth;

/// <summary>
/// Configuration for the internal service-to-service token.
/// Token is typically injected by the Aspire AppHost or Kubernetes Secret.
/// </summary>
public sealed class InternalTokenOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "InternalAuth";

    /// <summary>
    /// The shared secret token used for internal service-to-service communication.
    /// Read from environment variable <c>InternalAuth__Token</c> or config.
    /// </summary>
    public string Token { get; set; } = string.Empty;
}
