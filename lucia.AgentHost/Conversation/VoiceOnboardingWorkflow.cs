using System.Collections.Concurrent;
using lucia.AgentHost.Conversation.Models;
using lucia.Agents.Abstractions;
using lucia.Wyoming.CommandRouting;
using lucia.Wyoming.Diarization;

namespace lucia.AgentHost.Conversation;

public sealed partial class VoiceOnboardingWorkflow(
    VoiceOnboardingService enrollment,
    IDiarizationEngine diarization,
    IMemoryStore memories,
    TimeProvider timeProvider,
    ILogger<VoiceOnboardingWorkflow> logger) : BackgroundService
{
    private const string ConversationPrefix = "voice-onboarding:";
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, VoiceOnboardingConversation> _conversations = new();
    // ponytail: onboarding serializes across satellites; use per-conversation gates if enrollment throughput matters.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public static bool IsStartRequest(string text)
    {
        var normalized = Normalize(text);
        if (normalized.StartsWith("hey lucia ", StringComparison.Ordinal))
        {
            normalized = normalized["hey lucia ".Length..];
        }
        else if (normalized.StartsWith("lucia ", StringComparison.Ordinal))
        {
            normalized = normalized["lucia ".Length..];
        }

        return normalized is "onboard me" or "on board me"
            or "enroll my voice" or "enroll my voice profile"
            or "i want to enroll my voice" or "learn my voice";
    }

    public static bool IsOnboardingConversation(string? conversationId) =>
        conversationId?.StartsWith(ConversationPrefix, StringComparison.Ordinal) == true;

    public async Task<ConversationResponse?> TryProcessAsync(
        string conversationId,
        string? deviceId,
        string text,
        VoiceTurn? turn,
        CancellationToken ct)
    {
        var isStart = IsStartRequest(text);
        var carriesOnboardingId = IsOnboardingConversation(conversationId);
        if (!isStart && !carriesOnboardingId
            && (string.IsNullOrWhiteSpace(deviceId)
                || !_conversations.Values.Any(conversation =>
                    string.Equals(conversation.DeviceId, deviceId, StringComparison.Ordinal))))
        {
            return null;
        }

        VoiceOnboardingConversation? state = null;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ExpireConversationsAsync(ct).ConfigureAwait(false);
            if (!_conversations.ContainsKey(conversationId))
            {
                var active = _conversations.FirstOrDefault(entry =>
                    string.Equals(entry.Value.DeviceId, deviceId, StringComparison.Ordinal));
                if (active.Value is not null)
                {
                    conversationId = active.Key;
                }
                else if (isStart)
                {
                    if (!carriesOnboardingId)
                    {
                        conversationId = ConversationPrefix + conversationId;
                    }
                }
                else if (!carriesOnboardingId)
                {
                    return null;
                }
            }
            _conversations.TryGetValue(conversationId, out state);
            // Keep an outstanding sample out of command routing even after the server loses its in-memory workflow.
            if (state is null && !isStart)
            {
                return Reply("Onboarding expired or the server restarted. Say learn my voice to start again.", false);
            }
            if (state is { Stage: VoiceOnboardingStage.Expired })
            {
                if (!isStart)
                {
                    if (!carriesOnboardingId)
                    {
                        _conversations.TryRemove(conversationId, out _);
                    }
                    return Reply("Onboarding expired. Say learn my voice to start again.", false);
                }
                _conversations.TryRemove(conversationId, out _);
                state = null;
            }

            if (turn is null || turn.Audio.IsEmpty || string.IsNullOrWhiteSpace(deviceId))
            {
                return Reply("Please use your voice satellite with Lucia speech-to-text to onboard. I need live voice samples.", state is not null);
            }
            if (!diarization.IsReady)
            {
                return Reply("Voice enrollment is unavailable until a speaker recognition model is active.", false);
            }
            if (state is null)
            {
                if (turn.Speaker is not null)
                {
                    return Reply("Your voice already has a profile. You can ask me to remember or forget a preference.", false);
                }
                if (_conversations.Count >= 32)
                {
                    return Reply("Too many onboarding conversations are active. Please try again later.", false);
                }

                state = new VoiceOnboardingConversation
                {
                    DeviceId = deviceId,
                    LastActivityAt = timeProvider.GetUtcNow(),
                };
                _conversations[conversationId] = state;
                LogStarted(logger, conversationId);
                return Reply(Prompt(state));
            }
            if (!string.Equals(state.DeviceId, deviceId, StringComparison.Ordinal))
            {
                return Reply("Please continue onboarding on the satellite where you started.", false);
            }

            state.LastActivityAt = timeProvider.GetUtcNow();
            var answer = Normalize(text);
            if (answer is "cancel" or "cancel onboarding" or "stop" or "stop onboarding" or "never mind" or "nevermind")
            {
                var cancelled = state.Enrollment is null
                    || await enrollment.CancelOnboardingAsync(state.Enrollment.Id, ct).ConfigureAwait(false);
                _conversations.TryRemove(conversationId, out _);
                return Reply(cancelled
                    ? "Onboarding cancelled. I haven't enrolled you or saved your answers."
                    : "Your voice was already enrolled. I stopped saving your onboarding answers.", false);
            }
            if (isStart || answer is "repeat" or "repeat that")
            {
                return Reply(Prompt(state));
            }
            if (turn.Speaker is { } speaker && speaker.ProfileId != state.Enrollment?.ProfileId)
            {
                return Reply("I heard a different enrolled speaker. Please have the person being onboarded continue, or say cancel.");
            }
            if (text.Length > 500 || text.Any(char.IsControl))
            {
                return Reply($"Please give a shorter answer without control characters. {Prompt(state)}");
            }

            switch (state.Stage)
            {
                case VoiceOnboardingStage.Consent:
                    if (IsNo(answer))
                    {
                        _conversations.TryRemove(conversationId, out _);
                        return Reply("No problem. I won't enroll you or save your answers.", false);
                    }
                    if (!IsYes(answer))
                    {
                        return Reply(Prompt(state));
                    }
                    state.Stage = VoiceOnboardingStage.Name;
                    break;
                case VoiceOnboardingStage.Name:
                    var name = text.Trim().TrimEnd('.', '!', '?');
                    foreach (var prefix in new[] { "my name is ", "call me ", "i am ", "i'm " })
                    {
                        if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            name = name[prefix.Length..].Trim();
                            break;
                        }
                    }
                    if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || IsSkip(answer))
                    {
                        return Reply("Please tell me a name to use, up to eighty characters.");
                    }
                    state.Name = name;
                    state.Stage = VoiceOnboardingStage.Birthday;
                    break;
                case VoiceOnboardingStage.Birthday:
                    if (string.IsNullOrWhiteSpace(answer))
                    {
                        return Reply(Prompt(state));
                    }
                    // ponytail: keep confirmed wording; parse dates when a feature needs date arithmetic.
                    state.Birthday = IsSkip(answer) || answer is "no" or "no thanks" or "i d rather not say"
                        ? null
                        : text.Trim().TrimEnd('.');
                    state.Stage = VoiceOnboardingStage.Confirm;
                    break;
                case VoiceOnboardingStage.Confirm:
                    if (IsNo(answer))
                    {
                        state.Name = state.Birthday = null;
                        state.Stage = VoiceOnboardingStage.Name;
                        break;
                    }
                    if (!IsYes(answer))
                    {
                        return Reply(Prompt(state));
                    }
                    state.Enrollment = await enrollment.StartVoiceOnboardingAsync(
                        state.Name!, turn.Audio, turn.SampleRate, ct).ConfigureAwait(false);
                    state.Stage = VoiceOnboardingStage.Samples;
                    break;
                case VoiceOnboardingStage.Samples:
                    var session = state.Enrollment!;
                    if (session.CurrentPromptIndex < session.Prompts.Count
                        && NormalizePhrase(text) != NormalizePhrase(session.Prompts[session.CurrentPromptIndex]["Please say: ".Length..]))
                    {
                        return Reply($"That didn't match the phrase. {Prompt(state)}");
                    }
                    var result = await enrollment.ProcessSampleAsync(
                        session.Id, turn.Audio, turn.SampleRate, ct).ConfigureAwait(false);
                    if (result.Status == OnboardingStepStatus.Retry)
                    {
                        return Reply($"{result.Message} {Prompt(state)}");
                    }
                    if (result.CompletedProfile is { } profile)
                    {
                        await memories.StoreAsync(profile.Id, "preferred_name", state.Name!, ct: ct).ConfigureAwait(false);
                        if (state.Birthday is not null)
                        {
                            await memories.StoreAsync(profile.Id, "birthday", state.Birthday, ct: ct).ConfigureAwait(false);
                        }
                        _conversations.TryRemove(conversationId, out _);
                        LogCompleted(logger, conversationId, profile.Id);
                        return Reply(state.Birthday is null
                            ? "You're enrolled. I've saved your name. You can ask me to remember other details as we talk."
                            : "You're enrolled. I've saved your name and birthday. You can ask me to remember other details as we talk.", false);
                    }
                    break;
            }
            return Reply(Prompt(state));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex, conversationId);
            return Reply("I couldn't finish that onboarding step. Say repeat to hear the prompt, or cancel to stop.", true);
        }
        finally
        {
            _gate.Release();
        }

        ConversationResponse Reply(string message, bool needsInput = true) => new()
        {
            Type = "onboarding",
            Text = message,
            ConversationId = needsInput ? conversationId : conversationId[ConversationPrefix.Length..],
            NeedsInput = needsInput,
            OnboardingStage = state?.Stage.ToString() ?? (isStart ? "Start" : "Expired"),
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await _gate.WaitAsync(stoppingToken).ConfigureAwait(false);
            try
            {
                await ExpireConversationsAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex, "cleanup");
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private async Task ExpireConversationsAsync(CancellationToken ct)
    {
        var cutoff = timeProvider.GetUtcNow() - IdleTimeout;
        foreach (var (id, state) in _conversations)
        {
            if (state.LastActivityAt >= cutoff)
            {
                continue;
            }
            if (state.Stage == VoiceOnboardingStage.Expired)
            {
                _conversations.TryRemove(id, out _);
                continue;
            }
            if (state.Enrollment is not null)
            {
                await enrollment.CancelOnboardingAsync(state.Enrollment.Id, ct).ConfigureAwait(false);
            }
            state.Name = state.Birthday = null;
            state.Enrollment = null;
            state.Stage = VoiceOnboardingStage.Expired;
            state.LastActivityAt = timeProvider.GetUtcNow();
        }
    }

    private static string Prompt(VoiceOnboardingConversation state) => state.Stage switch
    {
        VoiceOnboardingStage.Consent => "I need your permission to save a voice profile and the facts you choose to share. Do you agree? You can say cancel at any time.",
        VoiceOnboardingStage.Name => "What name should I call you?",
        VoiceOnboardingStage.Birthday => "When is your birthday, including the year? Say skip this question if you'd rather not share it.",
        VoiceOnboardingStage.Confirm => $"I'll call you {state.Name}."
            + (state.Birthday is null ? "" : $" Your birthday is {state.Birthday}.")
            + " Is that correct?",
        VoiceOnboardingStage.Samples when state.Enrollment!.CurrentPromptIndex < state.Enrollment.Prompts.Count =>
            state.Enrollment.Prompts[state.Enrollment.CurrentPromptIndex],
        VoiceOnboardingStage.Samples => "Your voice profile was created, but saving your answers needs another attempt. Say retry.",
        _ => "Onboarding expired. Say learn my voice to start again.",
    };

    private static string Normalize(string text) => TranscriptNormalizer.Normalize(text);

    private static string NormalizePhrase(string text) => Normalize(text)
        .Replace("seventy two", "72", StringComparison.Ordinal)
        .Replace("five", "5", StringComparison.Ordinal)
        .Replace("what s", "what is", StringComparison.Ordinal)
        .Replace("whats", "what is", StringComparison.Ordinal);

    private static bool IsYes(string text) => text is "yes" or "yes i agree" or "i agree" or "sure" or "correct" or "that s correct";
    private static bool IsNo(string text) => text is "no" or "no i don t" or "i don t agree";
    private static bool IsSkip(string text) =>
        text is "no preference" or "no preferences" or "leave it blank" or "skip this question" or "nothing to add"
        || (text.Length > 0 && text.Split(' ').All(word => word == "skip"));

    [LoggerMessage(Level = LogLevel.Information, Message = "Started voice onboarding for conversation {ConversationId}")]
    private static partial void LogStarted(ILogger logger, string conversationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Completed voice onboarding for conversation {ConversationId}, profile {ProfileId}")]
    private static partial void LogCompleted(ILogger logger, string conversationId, string profileId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Voice onboarding failed for conversation {ConversationId}")]
    private static partial void LogFailed(ILogger logger, Exception exception, string conversationId);
}
