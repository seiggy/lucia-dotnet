using Microsoft.EntityFrameworkCore;

using lucia.Agents.Training;
using lucia.Agents.Training.Models;

namespace lucia.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ITraceRepository"/>.
/// Uses client-side filtering for JSON-stored properties (AgentExecutions, Label)
/// that cannot be efficiently queried in SQL.
/// </summary>
public sealed class EfTraceRepository(IDbContextFactory<LuciaDbContext> dbFactory) : ITraceRepository
{
    private readonly IDbContextFactory<LuciaDbContext> _dbFactory = dbFactory;

    public async Task InsertTraceAsync(ConversationTrace trace, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.ConversationTraces.Add(trace);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<ConversationTrace?> GetTraceAsync(string traceId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.ConversationTraces
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == traceId, ct)
            .ConfigureAwait(false);
    }

    public async Task<List<ConversationTrace>> GetTracesBySessionIdAsync(string sessionId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.ConversationTraces
            .AsNoTracking()
            .Where(t => t.SessionId == sessionId)
            .OrderBy(t => t.Timestamp)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<PagedResult<ConversationTrace>> ListTracesAsync(TraceFilterCriteria filter, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var query = BuildBaseTraceQuery(db, filter);

        var allFiltered = await query
            .OrderByDescending(t => t.Timestamp)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        allFiltered = ApplyJsonFilters(allFiltered, filter.AgentFilter, filter.ModelFilter, filter.LabelFilter);

        var totalCount = allFiltered.Count;
        var skip = (filter.Page - 1) * filter.PageSize;
        var items = allFiltered.Skip(skip).Take(filter.PageSize).ToList();

        return new PagedResult<ConversationTrace>
        {
            Items = items,
            TotalCount = totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize,
        };
    }

    public async Task UpdateLabelAsync(string traceId, TraceLabel label, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var trace = await db.ConversationTraces
            .FirstOrDefaultAsync(t => t.Id == traceId, ct)
            .ConfigureAwait(false);

        if (trace is null)
            return;

        trace.Label = label;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteTraceAsync(string traceId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var trace = await db.ConversationTraces
            .FirstOrDefaultAsync(t => t.Id == traceId, ct)
            .ConfigureAwait(false);

        if (trace is null)
            return;

        db.ConversationTraces.Remove(trace);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> DeleteOldUnlabeledAsync(int retentionDays, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        // Label.Status is stored as JSON, so filter client-side
        var candidates = await db.ConversationTraces
            .Where(t => t.Timestamp < cutoff)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var unlabeled = candidates
            .Where(t => t.Label.Status == LabelStatus.Unlabeled)
            .ToList();

        db.ConversationTraces.RemoveRange(unlabeled);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return unlabeled.Count;
    }

    public async Task InsertExportRecordAsync(DatasetExportRecord export, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.DatasetExportRecords.Add(export);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<DatasetExportRecord?> GetExportRecordAsync(string exportId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.DatasetExportRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == exportId, ct)
            .ConfigureAwait(false);
    }

    public async Task<List<DatasetExportRecord>> ListExportRecordsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.DatasetExportRecords
            .AsNoTracking()
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<ConversationTrace>> GetTracesForExportAsync(ExportFilterCriteria filter, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var query = db.ConversationTraces.AsNoTracking().AsQueryable();

        if (filter.FromDate.HasValue)
            query = query.Where(t => t.Timestamp >= filter.FromDate.Value);

        if (filter.ToDate.HasValue)
            query = query.Where(t => t.Timestamp <= filter.ToDate.Value);

        var results = await query
            .OrderByDescending(t => t.Timestamp)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return ApplyJsonFilters(results, filter.AgentFilter, filter.ModelFilter, filter.LabelFilter);
    }

    public async Task<TraceStats> GetStatsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var all = await db.ConversationTraces
            .AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var byAgent = all
            .SelectMany(t => t.AgentExecutions)
            .Where(a => !string.IsNullOrEmpty(a.AgentId))
            .GroupBy(a => a.AgentId)
            .ToDictionary(g => g.Key, g => g.Count());

        var errorsByAgent = all
            .SelectMany(t => t.AgentExecutions)
            .Where(a => !a.Success && !string.IsNullOrEmpty(a.AgentId))
            .GroupBy(a => a.AgentId)
            .ToDictionary(g => g.Key, g => g.Count());

        return new TraceStats
        {
            TotalTraces = all.Count,
            UnlabeledCount = all.Count(t => t.Label.Status == LabelStatus.Unlabeled),
            PositiveCount = all.Count(t => t.Label.Status == LabelStatus.Positive),
            NegativeCount = all.Count(t => t.Label.Status == LabelStatus.Negative),
            ErroredCount = all.Count(t => t.IsErrored),
            ByAgent = byAgent,
            ErrorsByAgent = errorsByAgent,
        };
    }

    /// <summary>
    /// Builds a queryable with SQL-translatable filters (dates, search text, error flag).
    /// </summary>
    private static IQueryable<ConversationTrace> BuildBaseTraceQuery(LuciaDbContext db, TraceFilterCriteria filter)
    {
        var query = db.ConversationTraces.AsNoTracking().AsQueryable();

        if (filter.FromDate.HasValue)
            query = query.Where(t => t.Timestamp >= filter.FromDate.Value);

        if (filter.ToDate.HasValue)
            query = query.Where(t => t.Timestamp <= filter.ToDate.Value);

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
            query = query.Where(t => t.UserInput != null && t.UserInput.Contains(filter.SearchText));

        return query;
    }

    /// <summary>
    /// Applies client-side filters for JSON-stored properties that cannot be
    /// efficiently translated to SQL (AgentExecutions array, Label object).
    /// </summary>
    private static List<ConversationTrace> ApplyJsonFilters(
        List<ConversationTrace> traces,
        string? agentFilter,
        string? modelFilter,
        LabelStatus? labelFilter)
    {
        var result = traces;

        if (!string.IsNullOrWhiteSpace(agentFilter))
            result = result.Where(t => t.AgentExecutions.Any(a => a.AgentId == agentFilter)).ToList();

        if (!string.IsNullOrWhiteSpace(modelFilter))
            result = result.Where(t => t.AgentExecutions.Any(a => a.ModelDeploymentName == modelFilter)).ToList();

        if (labelFilter.HasValue)
            result = result.Where(t => t.Label.Status == labelFilter.Value).ToList();

        return result;
    }
}
