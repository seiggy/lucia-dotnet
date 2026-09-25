using lucia.Agents.Abstractions;
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
            IConfigStoreWriter configStore) =>
        {
            const string prefix = VoiceProfileOptions.SectionName;

            if (request.SpeakerVerificationThreshold.HasValue)
            {
                var threshold = request.SpeakerVerificationThreshold.Value;
                if (!VoiceProfileOptions.IsValidSpeakerVerificationThreshold(threshold))
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        [nameof(VoiceConfigUpdateRequest.SpeakerVerificationThreshold)] =
                        [$"The value must be a finite number between {0.10f:0.00} and {0.95f:0.00}."]
                    });

                await configStore.SetAsync($"{prefix}:SpeakerVerificationThreshold", threshold.ToString("F2"), "voice-config-ui").ConfigureAwait(false);
            }

            if (request.ProvisionalMatchThreshold.HasValue)
            {
                var threshold = request.ProvisionalMatchThreshold.Value;
                if (!VoiceProfileOptions.IsValidProvisionalMatchThreshold(threshold))
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        [nameof(VoiceConfigUpdateRequest.ProvisionalMatchThreshold)] =
                        [$"The value must be a finite number between {0.10f:0.00} and {0.95f:0.00}."]
                    });

                await configStore.SetAsync($"{prefix}:ProvisionalMatchThreshold", threshold.ToString("F2"), "voice-config-ui").ConfigureAwait(false);
            }

            if (request.IgnoreUnknownVoices.HasValue)
                await configStore.SetAsync($"{prefix}:IgnoreUnknownVoices", request.IgnoreUnknownVoices.Value.ToString(), "voice-config-ui").ConfigureAwait(false);
            if (request.AutoCreateProvisionalProfiles.HasValue)
                await configStore.SetAsync($"{prefix}:AutoCreateProvisionalProfiles", request.AutoCreateProvisionalProfiles.Value.ToString(), "voice-config-ui").ConfigureAwait(false);
            if (request.MaxAutoProfiles.HasValue)
                await configStore.SetAsync($"{prefix}:MaxAutoProfiles", request.MaxAutoProfiles.Value.ToString(), "voice-config-ui").ConfigureAwait(false);
            if (request.AdaptiveProfiles.HasValue)
                await configStore.SetAsync($"{prefix}:AdaptiveProfiles", request.AdaptiveProfiles.Value.ToString(), "voice-config-ui").ConfigureAwait(false);
            if (request.ProvisionalRetentionDays.HasValue)
                await configStore.SetAsync($"{prefix}:ProvisionalRetentionDays", request.ProvisionalRetentionDays.Value.ToString(), "voice-config-ui").ConfigureAwait(false);
            if (request.SuggestEnrollmentAfter.HasValue)
                await configStore.SetAsync($"{prefix}:SuggestEnrollmentAfter", request.SuggestEnrollmentAfter.Value.ToString(), "voice-config-ui").ConfigureAwait(false);

            return Results.Ok(new { Saved = true });
        }).WithName("UpdateVoiceConfig");
    }
}
