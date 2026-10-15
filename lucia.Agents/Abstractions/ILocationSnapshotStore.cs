using lucia.Agents.Models;

namespace lucia.Agents.Abstractions;

/// <summary>
/// Shares a loaded <see cref="LocationSnapshot"/> between processes and restarts. The version counter
/// lets other instances notice that the snapshot changed.
/// </summary>
public interface ILocationSnapshotStore
{
    /// <summary>Returns the stored snapshot, or <see langword="null"/> when nothing usable is stored.</summary>
    Task<StoredLocationSnapshot?> TryLoadAsync(CancellationToken ct = default);

    Task SaveAsync(LocationSnapshot snapshot, DateTimeOffset? loadedAt, CancellationToken ct = default);

    /// <summary>Drops the stored snapshot so the next load reads Home Assistant.</summary>
    Task InvalidateAsync(CancellationToken ct = default);

    /// <summary>Returns the current version, or <see langword="null"/> when the store has no shared version.</summary>
    Task<long?> GetVersionAsync(CancellationToken ct = default);

    /// <summary>Increments the shared version and returns it, or <see langword="null"/> when unsupported.</summary>
    Task<long?> BumpVersionAsync(CancellationToken ct = default);
}
