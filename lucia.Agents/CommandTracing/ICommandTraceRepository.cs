using lucia.Agents.Training.Models;

namespace lucia.Agents.CommandTracing;

/// <summary>
/// Persistence abstraction for command trace records.
/// </summary>
public interface ICommandTraceRepository
{
    Task SaveAsync(CommandTrace trace, CancellationToken ct = default);
    Task<CommandTrace?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<PagedResult<CommandTrace>> ListAsync(CommandTraceFilter filter, CancellationToken ct = default);
    Task<CommandTraceStats> GetStatsAsync(CancellationToken ct = default);
}
