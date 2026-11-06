using lucia.Agents.Family;

namespace lucia.AgentHost.Family;

/// <summary>
/// A newly issued pairing. <see cref="Code"/> is shown once and never stored.
/// </summary>
public sealed record PairingCode(DevicePairing Pairing, string Code);
