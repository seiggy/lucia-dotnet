using lucia.Agents.Family;

namespace lucia.Agents.Abstractions;

public interface IDevicePairingStore
{
    Task CreateAsync(DevicePairing pairing, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DevicePairing>> ListForMemberAsync(
        string memberId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically marks an unexpired, unredeemed pairing as redeemed at <paramref name="now"/>
    /// and returns it. Returns null for unknown, expired, or already redeemed codes, so a code
    /// can be redeemed at most once even under concurrent requests.
    /// </summary>
    Task<DevicePairing?> TryRedeemAsync(
        string codeHash,
        DateTime now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the device key issued for a redeemed pairing.
    /// </summary>
    Task SetApiKeyAsync(string pairingId, string apiKeyId, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string pairingId, CancellationToken cancellationToken = default);
}
