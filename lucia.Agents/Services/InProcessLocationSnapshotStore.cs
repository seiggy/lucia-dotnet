using lucia.Agents.Abstractions;
using lucia.Agents.Models;

namespace lucia.Agents.Services;

/// <summary>
/// Snapshot store for a single process. The facade already keeps the snapshot in memory, so nothing is
/// stored or shared and there is no version to watch.
/// </summary>
public sealed class InProcessLocationSnapshotStore : ILocationSnapshotStore
{
    public Task<StoredLocationSnapshot?> TryLoadAsync(CancellationToken ct = default)
        => Task.FromResult<StoredLocationSnapshot?>(null);

    public Task SaveAsync(LocationSnapshot snapshot, DateTimeOffset? loadedAt, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task InvalidateAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<long?> GetVersionAsync(CancellationToken ct = default) => Task.FromResult<long?>(null);

    public Task<long?> BumpVersionAsync(CancellationToken ct = default) => Task.FromResult<long?>(null);
}
