using System.Text.Json;
using lucia.Agents.Training;
using lucia.Agents.Training.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Extensions;

/// <summary>
/// Minimal API endpoints for exporting conversation traces as JSONL datasets.
/// </summary>
public static class DatasetExportApi
{
    private static readonly JsonSerializerOptions _jsonlOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapDatasetExportApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/exports")
            .WithTags("Exports")
            .RequireAuthorization();

        group.MapPost("/", CreateExportAsync);

        group.MapGet("/", ListExportsAsync);

        group.MapGet("/{id}", GetExportAsync);

        group.MapGet("/{id}/download", DownloadExportAsync);

        return endpoints;
    }

    private static async Task<Results<Created<DatasetExportRecord>, BadRequest<string>>> CreateExportAsync(
        [FromServices] ITraceRepository repository,
        [FromBody] ExportFilterCriteria filter,
        CancellationToken ct)
    {
        var traces = await repository.GetTracesForExportAsync(filter, ct).ConfigureAwait(false);

        if (traces.Count == 0)
        {
            return TypedResults.BadRequest("No traces match the specified filter criteria.");
        }

        var exportDir = Path.Combine(Path.GetTempPath(), "lucia-exports");
        Directory.CreateDirectory(exportDir);

        var exportId = Guid.NewGuid().ToString("N");
        var filePath = Path.Combine(exportDir, $"{exportId}.jsonl");

        await using (var writer = new StreamWriter(filePath))
        {
            foreach (var trace in traces)
            {
                var jsonlLine = ConvertTraceToJsonl(trace, filter.IncludeCorrections, filter.AgentFilter);
                await writer.WriteLineAsync(jsonlLine).ConfigureAwait(false);
            }
        }

        var fileInfo = new FileInfo(filePath);

        var record = new DatasetExportRecord
        {
            Id = exportId,
            Timestamp = DateTime.UtcNow,
            FilterCriteria = filter,
            RecordCount = traces.Count,
            FileSizeBytes = fileInfo.Length,
            FilePath = filePath,
            IsComplete = true
        };

        await repository.InsertExportRecordAsync(record, ct).ConfigureAwait(false);

        return TypedResults.Created($"/api/exports/{record.Id}", record);
    }

    private static async Task<Ok<List<DatasetExportRecord>>> ListExportsAsync(
        [FromServices] ITraceRepository repository,
        CancellationToken ct)
    {
        var exports = await repository.ListExportRecordsAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(exports);
    }

    private static async Task<Results<Ok<DatasetExportRecord>, NotFound>> GetExportAsync(
        [FromServices] ITraceRepository repository,
        [FromRoute] string id,
        CancellationToken ct)
    {
        var record = await repository.GetExportRecordAsync(id, ct).ConfigureAwait(false);

        if (record is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(record);
    }

    private static async Task<Results<FileStreamHttpResult, NotFound>> DownloadExportAsync(
        [FromServices] ITraceRepository repository,
        [FromRoute] string id,
        CancellationToken ct)
    {
        var record = await repository.GetExportRecordAsync(id, ct).ConfigureAwait(false);

        if (record is null || record.FilePath is null || !File.Exists(record.FilePath))
        {
            return TypedResults.NotFound();
        }

        var stream = new FileStream(record.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return TypedResults.Stream(stream, "application/jsonl", $"export-{id}.jsonl");
    }

    /// <summary>
    /// Converts a conversation trace to an OpenAI fine-tuning JSONL line.
    /// </summary>
    private static string ConvertTraceToJsonl(ConversationTrace trace, bool includeCorrections, string? agentFilter = null) =>
        JsonlConverter.ConvertTraceToJsonl(trace, includeCorrections, agentFilter);
}
