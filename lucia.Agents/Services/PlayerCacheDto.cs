namespace lucia.Agents.Services;

internal sealed record PlayerCacheDto(string EntityId, string FriendlyName, string? ConfigEntryId, bool IsSatellite);
