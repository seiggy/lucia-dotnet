using lucia.Agents.Family;

namespace lucia.Agents.Abstractions;

/// <summary>
/// Durable action and approval records. Status changes use compare-and-set so a decision or
/// an execution claim happens at most once.
/// </summary>
public interface IActionRecordStore
{
    Task CreateAsync(ActionRecord record, CancellationToken cancellationToken = default);

    Task<ActionRecord?> GetAsync(string actionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves <paramref name="updated"/> only if the stored record is still in
    /// <paramref name="expectedStatus"/>. Returns false if another writer got there first.
    /// </summary>
    Task<bool> TryUpdateAsync(
        ActionRecord updated,
        ActionStatus expectedStatus,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records awaiting a decision that <paramref name="approverId"/> may make, oldest first.
    /// </summary>
    Task<IReadOnlyList<ActionRecord>> ListAwaitingDecisionAsync(
        string approverId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The requester's most recent records, newest first.
    /// </summary>
    Task<IReadOnlyList<ActionRecord>> ListForRequesterAsync(
        string requesterId,
        int limit,
        CancellationToken cancellationToken = default);
}
