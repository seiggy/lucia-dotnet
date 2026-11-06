namespace lucia.Agents.Family;

/// <summary>
/// Binds one external identity to a family member. (Kind, Issuer, Subject) is unique.
/// </summary>
public sealed class MemberIdentityLink
{
    public required string MemberId { get; init; }

    public required MemberIdentityKind Kind { get; init; }

    /// <summary>
    /// OIDC issuer. Empty for local identities (API keys, speaker profiles).
    /// </summary>
    public string Issuer { get; init; } = string.Empty;

    /// <summary>
    /// API key ID, speaker profile ID, or OIDC <c>sub</c> claim.
    /// </summary>
    public required string Subject { get; init; }

    public string? Label { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
