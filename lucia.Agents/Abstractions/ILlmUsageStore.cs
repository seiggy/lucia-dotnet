using lucia.Agents.Family;

namespace lucia.Agents.Abstractions;

public interface ILlmUsageStore
{
    Task RecordAsync(LlmUsageRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sums the member's usage with timestamps at or after <paramref name="since"/>.
    /// </summary>
    Task<LlmUsageTotals> GetTotalsAsync(
        string memberId,
        DateTime since,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the member's most recent records, newest first.
    /// </summary>
    Task<IReadOnlyList<LlmUsageRecord>> ListRecentAsync(
        string memberId,
        int limit,
        CancellationToken cancellationToken = default);
}
