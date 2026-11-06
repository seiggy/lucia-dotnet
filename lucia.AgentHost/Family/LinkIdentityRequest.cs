using lucia.Agents.Family;

namespace lucia.AgentHost.Family;

/// <param name="Issuer">OIDC issuer; ignored for API keys and speaker profiles.</param>
/// <param name="Subject">API key ID, speaker profile ID, or OIDC <c>sub</c>.</param>
public sealed record LinkIdentityRequest(MemberIdentityKind Kind, string? Issuer, string Subject, string? Label);
