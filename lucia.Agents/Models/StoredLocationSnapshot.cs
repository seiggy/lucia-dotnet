namespace lucia.Agents.Models;

/// <summary>A snapshot read back from an <see cref="Abstractions.ILocationSnapshotStore"/>.</summary>
public sealed record StoredLocationSnapshot(LocationSnapshot Snapshot, DateTimeOffset? LoadedAt, long? Version);
