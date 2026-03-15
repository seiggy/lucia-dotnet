using lucia.AgentHost.Models;
using lucia.Wyoming.Diarization;
using Microsoft.Extensions.Options;

namespace lucia.AgentHost.Apis;

public static class VoiceConfigApi
{
    public static void MapVoiceConfigEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/wyoming/voice-config")
            .WithTags("Voice Configuration")
            .RequireAuthorization();

        group.MapGet("/", (
            IOptionsMonitor<VoiceProfileOptions> profileOptions,
            IOptionsMonitor<DiarizationOptions> diarizationOptions) =>
        {
            var profile = profileOptions.CurrentValue;
            var diarization = diarizationOptions.CurrentValue;
            return Results.Ok(new
            {
                profile.IgnoreUnknownVoices,
                profile.AutoCreateProvisionalProfiles,
                profile.MaxAutoProfiles,
                profile.SpeakerVerificationThreshold,
                profile.ProvisionalMatchThreshold,
                profile.AdaptiveProfiles,
                profile.ProvisionalRetentionDays,
                profile.SuggestEnrollmentAfter,
                DiarizationEnabled = diarization.Enabled,
            });
        }).WithName("GetVoiceConfig");

        group.MapPut("/", async (
            VoiceConfigUpdateRequest request,
            IOptionsMonitor<VoiceProfileOptions> profileOptions,
            IOptionsMonitor<DiarizationOptions> diarizationOptions,
            IWebHostEnvironment env) =>
        {
            var profile = profileOptions.CurrentValue;

            if (request.IgnoreUnknownVoices.HasValue)
                profile.IgnoreUnknownVoices = request.IgnoreUnknownVoices.Value;
            if (request.AutoCreateProvisionalProfiles.HasValue)
                profile.AutoCreateProvisionalProfiles = request.AutoCreateProvisionalProfiles.Value;
            if (request.MaxAutoProfiles.HasValue)
                profile.MaxAutoProfiles = request.MaxAutoProfiles.Value;
            if (request.SpeakerVerificationThreshold.HasValue)
                profile.SpeakerVerificationThreshold = request.SpeakerVerificationThreshold.Value;
            if (request.ProvisionalMatchThreshold.HasValue)
                profile.ProvisionalMatchThreshold = request.ProvisionalMatchThreshold.Value;
            if (request.AdaptiveProfiles.HasValue)
                profile.AdaptiveProfiles = request.AdaptiveProfiles.Value;
            if (request.ProvisionalRetentionDays.HasValue)
                profile.ProvisionalRetentionDays = request.ProvisionalRetentionDays.Value;
            if (request.SuggestEnrollmentAfter.HasValue)
                profile.SuggestEnrollmentAfter = request.SuggestEnrollmentAfter.Value;

            // Persist to voiceconfig.json for survival across restarts
            var configPath = Path.Combine(env.ContentRootPath, "voiceconfig.json");
            var json = System.Text.Json.JsonSerializer.Serialize(
                new { Wyoming = new { VoiceProfiles = profile } },
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(configPath, json).ConfigureAwait(false);

            return Results.Ok(new { Saved = true });
        }).WithName("UpdateVoiceConfig");
    }
}
