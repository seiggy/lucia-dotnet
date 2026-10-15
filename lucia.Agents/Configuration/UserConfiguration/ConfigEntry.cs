
namespace lucia.Agents.Configuration.UserConfiguration;

/// <summary>
/// Represents a single persisted configuration entry.
/// Uses flat ASP.NET Core configuration key format (e.g., "HomeAssistant:BaseUrl").
/// </summary>
public sealed class ConfigEntry
{
    /// <summary>
    /// Document ID. Uses the configuration key as the natural ID.
    /// Example: "HomeAssistant:BaseUrl"
    /// </summary>
    public string Key { get; set; } = default!;

    /// <summary>
    /// The configuration value as a string. ASP.NET Core configuration is string-based.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// The top-level section this key belongs to (e.g., "HomeAssistant", "ConnectionStrings").
    /// Derived from the key prefix before the first colon. Used for querying by section.
    /// </summary>
    public string Section { get; set; } = default!;

    /// <summary>
    /// UTC timestamp of when this entry was last modified.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Who last modified this entry (e.g., "system-seed", "admin-ui").
    /// </summary>
    public string UpdatedBy { get; set; } = "system";

    /// <summary>
    /// Whether this value contains sensitive data (tokens, keys, passwords).
    /// Sensitive values are masked when returned from the API.
    /// </summary>
    public bool IsSensitive { get; set; }
}
