namespace lucia.Wyoming.Models;

public sealed record ModelDownloadResult
{
    public required bool Success { get; init; }
    public required string ModelId { get; init; }
    public string? LocalPath { get; init; }
    public bool AlreadyExisted { get; init; }
    public string? Error { get; init; }

    public static ModelDownloadResult AlreadyExists(string modelId, string localPath) =>
        new()
        {
            Success = true,
            ModelId = modelId,
            LocalPath = localPath,
            AlreadyExisted = true,
        };

    public static ModelDownloadResult Failure(string modelId, string error) =>
        new()
        {
            Success = false,
            ModelId = modelId,
            Error = error,
        };

    public static ModelDownloadResult Successful(string modelId, string localPath) =>
        new()
        {
            Success = true,
            ModelId = modelId,
            LocalPath = localPath,
        };
}
