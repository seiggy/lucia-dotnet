using lucia.Agents.Family;

namespace lucia.AgentHost.Family;

/// <summary>
/// The device key issued by redeeming a pairing code. <see cref="ApiKey"/> is shown once.
/// </summary>
public sealed record PairingRedemption(string ApiKey, string KeyId, FamilyMember Member);
