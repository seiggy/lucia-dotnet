namespace lucia.Agents.Family;

/// <summary>
/// A one-time code an administrator issues so an app can obtain a device key for a member.
/// Only the code's hash is stored.
/// </summary>
public sealed class DevicePairing
{
    public required string Id { get; init; }

    public required string MemberId { get; init; }

    public required string CodeHash { get; init; }

    public string? DeviceName { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public required DateTime ExpiresAt { get; init; }

    public DateTime? RedeemedAt { get; set; }

    /// <summary>
    /// The device API key issued when the code was redeemed.
    /// </summary>
    public string? ApiKeyId { get; set; }
}
