namespace lucia.Agents.Auth;

/// <summary>
/// Summary of an API key for listing. Never includes the full key or hash.
/// </summary>
public sealed record ApiKeySummaryDto
{
    public required string Id { get; init; }
    public required string KeyPrefix { get; init; }
    public required string Name { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime? LastUsedAt { get; init; }
    public required DateTime? ExpiresAt { get; init; }
    public required bool IsRevoked { get; init; }
    public required DateTime? RevokedAt { get; init; }
    public required string[] Scopes { get; init; }
}
