namespace lucia.Wyoming.Models;

public sealed record ModelDownloadProgress
{
    public required string ModelId { get; init; }
    public required long BytesDownloaded { get; init; }
    public required long TotalBytes { get; init; }
    public required double PercentComplete { get; init; }
}
