using lucia.AgentHost.Models;
using lucia.Wyoming.Diarization;

namespace lucia.AgentHost.Apis;

/// <summary>
/// REST endpoints for managing voice audio clips associated with speaker profiles.
/// </summary>
public static class VoiceClipApi
{
    public static void MapVoiceClipEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/speakers/{profileId}/clips")
            .WithTags("Voice Clips")
            .RequireAuthorization();

        group.MapGet("/", (string profileId, AudioClipService clipService) =>
        {
            var clips = clipService.GetClips(profileId);
            return Results.Ok(clips);
        });

        group.MapGet("/{clipId}", (string profileId, string clipId, AudioClipService clipService) =>
        {
            var filePath = clipService.GetClipFilePath(profileId, clipId);
            if (filePath is null)
            {
                return Results.NotFound($"Clip '{clipId}' not found for profile '{profileId}'");
            }

            var fullPath = Path.GetFullPath(filePath);
            return Results.File(fullPath, contentType: "audio/wav", fileDownloadName: $"{clipId}.wav");
        });

        group.MapDelete("/{clipId}", (string profileId, string clipId, AudioClipService clipService) =>
        {
            clipService.DeleteClip(profileId, clipId);
            return Results.NoContent();
        });

        group.MapPost("/{clipId}/reassign", async (
            string profileId,
            string clipId,
            ReassignClipRequest request,
            AudioClipService clipService,
            CancellationToken ct) =>
        {
            var filePath = clipService.GetClipFilePath(profileId, clipId);
            if (filePath is null)
            {
                return Results.NotFound($"Clip '{clipId}' not found for profile '{profileId}'");
            }

            // MoveClipsAsync moves all clips; for single-clip reassign we move just the one
            // by using a temp intermediary approach: save reference, delete, then move.
            // Instead, leverage the existing MoveClips for a single-clip profile subfolder.
            await clipService.ReassignClipAsync(profileId, clipId, request.TargetProfileId, ct)
                .ConfigureAwait(false);

            return Results.Ok(new { clipId, newProfileId = request.TargetProfileId });
        });
    }
}
