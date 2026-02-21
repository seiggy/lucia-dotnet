using lucia.Agents.Training.Models;

namespace lucia.Agents.Training;

/// <summary>
/// Repository interface for conversation trace persistence.
/// </summary>
public interface ITraceRepository
{
    Task InsertTraceAsync(ConversationTrace trace, CancellationToken ct = default);
    Task<ConversationTrace?> GetTraceAsync(string traceId, CancellationToken ct = default);
    Task<PagedResult<ConversationTrace>> ListTracesAsync(TraceFilterCriteria filter, CancellationToken ct = default);
    Task UpdateLabelAsync(string traceId, TraceLabel label, CancellationToken ct = default);
    Task DeleteTraceAsync(string traceId, CancellationToken ct = default);
    Task<int> DeleteOldUnlabeledAsync(int retentionDays, CancellationToken ct = default);
    Task InsertExportRecordAsync(DatasetExportRecord export, CancellationToken ct = default);
    Task<DatasetExportRecord?> GetExportRecordAsync(string exportId, CancellationToken ct = default);
    Task<List<DatasetExportRecord>> ListExportRecordsAsync(CancellationToken ct = default);
    Task<List<ConversationTrace>> GetTracesForExportAsync(ExportFilterCriteria filter, CancellationToken ct = default);
    Task<TraceStats> GetStatsAsync(CancellationToken ct = default);
}