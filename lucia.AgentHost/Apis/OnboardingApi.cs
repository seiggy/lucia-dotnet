using lucia.Wyoming.Diarization;
using lucia.Wyoming.WakeWord;

namespace lucia.AgentHost.Apis;

/// <summary>
/// REST API for voice profile onboarding and wake word calibration.
/// Unified flow at /api/onboarding/*.
/// </summary>
public static class OnboardingApi
{
    private const int MaxUploadBytes = 10 * 1024 * 1024; // 10MB

    public static void MapOnboardingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/onboarding")
            .WithTags("Voice Onboarding")
            .RequireAuthorization();

        group.MapPost("/start", async (
            OnboardingStartRequest request,
            VoiceOnboardingService onboarding,
            CustomWakeWordManager? wakeWords,
            CancellationToken ct) =>
        {
            var session = await onboarding.StartOnboardingAsync(
                request.SpeakerName,
                request.ProvisionalProfileId,
                ct).ConfigureAwait(false);

            CustomWakeWord? wakeWord = null;
            if (!string.IsNullOrEmpty(request.WakeWordPhrase) && wakeWords is not null)
            {
                wakeWord = await wakeWords.RegisterWakeWordAsync(
                    request.WakeWordPhrase,
                    session.Id,
                    ct).ConfigureAwait(false);
            }

            return Results.Ok(new
            {
                session.Id,
                WakeWordId = wakeWord?.Id,
                FirstPrompt = session.Prompts.Count > 0 ? session.Prompts[0] : null,
                TotalPrompts = session.Prompts.Count,
            });
        });

        group.MapPost("/{sessionId}/sample", async (
            string sessionId,
            HttpRequest request,
            VoiceOnboardingService onboarding,
            CancellationToken ct) =>
        {
            var form = await request.ReadFormAsync(ct).ConfigureAwait(false);
            var audioFile = form.Files["audio"];
            if (audioFile is null)
            {
                return Results.BadRequest("No audio file provided. Send as multipart form with field name 'audio'.");
            }

            try
            {
                var samples = await ReadWavAsFloatAsync(audioFile.OpenReadStream()).ConfigureAwait(false);
                var result = await onboarding.ProcessSampleAsync(sessionId, samples, 16000, ct).ConfigureAwait(false);
                return Results.Ok(result);
            }
            catch (BadHttpRequestException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        group.MapGet("/{sessionId}", async (
            string sessionId,
            VoiceOnboardingService onboarding,
            CancellationToken ct) =>
        {
            var session = await onboarding.GetSessionAsync(sessionId, ct).ConfigureAwait(false);
            if (session is null)
            {
                return Results.NotFound($"Session '{sessionId}' not found");
            }

            return Results.Ok(new
            {
                session.Id,
                session.SpeakerName,
                session.Status,
                session.CurrentPromptIndex,
                TotalPrompts = session.Prompts.Count,
                NextPrompt = session.CurrentPromptIndex < session.Prompts.Count
                    ? session.Prompts[session.CurrentPromptIndex]
                    : null,
            });
        });

        app.MapGet("/api/speakers", async (
            ISpeakerProfileStore store,
            CancellationToken ct) =>
        {
            var profiles = await store.GetEnrolledProfilesAsync(ct).ConfigureAwait(false);
            return Results.Ok(profiles.Select(p => new
            {
                p.Id,
                p.Name,
                p.IsProvisional,
                p.IsAuthorized,
                p.InteractionCount,
                p.EnrolledAt,
                p.LastSeenAt,
            }));
        }).WithTags("Voice Onboarding")
            .RequireAuthorization();

        app.MapDelete("/api/speakers/{id}", async (
            string id,
            ISpeakerProfileStore store,
            CancellationToken ct) =>
        {
            await store.DeleteAsync(id, ct).ConfigureAwait(false);
            return Results.NoContent();
        }).WithTags("Voice Onboarding")
            .RequireAuthorization();

        app.MapGet("/api/wake-words", async (
            IWakeWordStore store,
            CancellationToken ct) =>
        {
            var words = await store.GetAllAsync(ct).ConfigureAwait(false);
            return Results.Ok(words);
        }).WithTags("Voice Onboarding")
            .RequireAuthorization();

        app.MapDelete("/api/wake-words/{id}", async (
            string id,
            IWakeWordStore store,
            CustomWakeWordManager manager,
            CancellationToken ct) =>
        {
            await store.DeleteAsync(id, ct).ConfigureAwait(false);
            await manager.ReloadKeywordsAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        }).WithTags("Voice Onboarding")
            .RequireAuthorization();
    }

    private static async Task<ReadOnlyMemory<float>> ReadWavAsFloatAsync(Stream stream)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms).ConfigureAwait(false);
        var bytes = ms.ToArray();

        if (bytes.Length > MaxUploadBytes)
        {
            throw new BadHttpRequestException($"Upload exceeds maximum size of {MaxUploadBytes / 1024 / 1024}MB");
        }

        if (bytes.Length < 44)
        {
            throw new BadHttpRequestException("Invalid WAV file: too small");
        }

        if (bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F')
        {
            throw new BadHttpRequestException("Invalid WAV file: missing RIFF header");
        }

        if (bytes[8] != 'W' || bytes[9] != 'A' || bytes[10] != 'V' || bytes[11] != 'E')
        {
            throw new BadHttpRequestException("Invalid WAV file: missing WAVE format");
        }

        var audioFormat = BitConverter.ToInt16(bytes, 20);
        if (audioFormat != 1)
        {
            throw new BadHttpRequestException("Invalid WAV file: only PCM format supported");
        }

        var bitsPerSample = BitConverter.ToInt16(bytes, 34);
        if (bitsPerSample != 16)
        {
            throw new BadHttpRequestException($"Invalid WAV file: expected 16-bit PCM, got {bitsPerSample}-bit");
        }

        var dataOffset = 44;
        var dataSize = bytes.Length - dataOffset;
        if (dataSize <= 0)
        {
            throw new BadHttpRequestException("Invalid WAV file: no audio data");
        }

        if (dataSize % 2 != 0)
        {
            throw new BadHttpRequestException("Invalid WAV file: PCM data size must be even");
        }

        var pcmBytes = bytes.AsSpan(dataOffset, dataSize);
        var samples = new float[pcmBytes.Length / 2];

        for (var i = 0; i < samples.Length; i++)
        {
            var sample = BitConverter.ToInt16(pcmBytes.Slice(i * 2, 2));
            samples[i] = sample / 32768.0f;
        }

        return samples;
    }
}
