# Phase 2: Speaker Verification ("Diarization") & Command Routing

**Phase**: 2 of 4
**Priority**: P1 — Fast-Path Commands
**Dependencies**: Phase 1 (Wyoming Core + STT + Wake Word)
**Estimated Complexity**: Medium-High

## Objective

Build a speaker-verification engine and a command routing system that short-circuits the LLM for known command patterns. In this phase, "diarization" means single-utterance speaker verification and identification, not multi-speaker segmentation across a conversation. This dramatically reduces latency for common home automation commands from 2-5 seconds (LLM round-trip) to under 500ms (direct skill dispatch).

The engine works as a post-STT processor: it takes transcribed text + audio embeddings and decides whether to fast-path the command to a skill or hand off to the full LLM orchestrator through a metadata-rich voice invocation contract.

---

## Architecture

```
┌─────────────────────────────────────────────────────┐
│                Wyoming Session                       │
│                                                      │
│  Audio → VAD → STT → [transcript]                   │
│                  │                                    │
│                  ▼                                    │
│  Audio → Speaker Embedding → [speaker_id]            │
│                  │                                    │
│                  ▼                                    │
│  ┌──────────────────────────────────┐                │
│  │      Command Router              │                │
│  │                                  │                │
│  │  transcript + speaker_id         │                │
│  │         │                        │                │
│  │    ┌────┴──────┐                 │                │
│  │    ▼           ▼                 │                │
│  │  Pattern    No Match             │                │
│  │  Match      (< threshold)        │                │
│  │    │           │                 │                │
│  │    ▼           ▼                 │                │
│  │  Skill      LuciaEngine         │                │
│  │  Dispatch   (in-process)        │                │
│  └──────────────────────────────────┘                │
│                  │                                    │
│                  ▼                                    │
│            [response text]                           │
│                  │                                    │
│                  ▼                                    │
│          (Phase 3: TTS)                              │
└─────────────────────────────────────────────────────┘
```

---

## Deliverables

### D1: Speaker Verification Engine

**What**: Extract speaker embeddings from a single utterance to identify who is speaking

**Implementation Details**:

#### Interface
```csharp
public interface IDiarizationEngine : IDisposable
{
    /// <summary>
    /// Extract a speaker embedding from an audio segment.
    /// </summary>
    SpeakerEmbedding ExtractEmbedding(ReadOnlySpan<float> audioSamples, int sampleRate);
    
    /// <summary>
    /// Identify the speaker by comparing embedding against enrolled profiles.
    /// Returns null if no enrolled speaker matches above threshold.
    /// </summary>
    SpeakerIdentification? IdentifySpeaker(SpeakerEmbedding embedding);
    
    /// <summary>
    /// Enroll a new speaker profile from reference audio.
    /// </summary>
    Task<SpeakerProfile> EnrollSpeakerAsync(
        string speakerName, 
        ReadOnlyMemory<float> referenceAudio, 
        int sampleRate, 
        CancellationToken ct);
}
```

#### sherpa-onnx Speaker Embedding
- Use sherpa-onnx's speaker embedding extractor model
- Extract embedding from each utterance's audio (after VAD segmentation)
- Embeddings are float vectors (~256-512 dimensions)
- Compare using cosine similarity

#### Speaker Profile Storage
```csharp
public sealed record SpeakerProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required float[] Embedding { get; init; }
    public required DateTimeOffset EnrolledAt { get; init; }
    public bool IsAuthorized { get; init; } = true;
    public IReadOnlyList<string>? AllowedSkills { get; init; }
}
```

- Store profiles in MongoDB (reuse existing config DB)
- Cache in memory for fast comparison
- Support multiple embeddings per speaker (average for robustness)

#### Identification Flow
1. After STT produces transcript, extract speaker embedding from same audio
2. Compare embedding against all enrolled profiles
3. Return best match if cosine similarity > threshold (default 0.7)
4. If no match or below threshold, mark as "unknown speaker"

**Files**:
- `lucia.Wyoming/Diarization/IDiarizationEngine.cs`
- `lucia.Wyoming/Diarization/SherpaDiarizationEngine.cs`
- `lucia.Wyoming/Diarization/SpeakerEmbedding.cs`
- `lucia.Wyoming/Diarization/SpeakerIdentification.cs`
- `lucia.Wyoming/Diarization/SpeakerProfile.cs`
- `lucia.Wyoming/Diarization/SpeakerProfileStore.cs`

---

### D2: Command Pattern Registry

**What**: A registry of command templates that map transcribed text to skill invocations

**Implementation Details**:

#### Pattern Definition
```csharp
public sealed record CommandPattern
{
    /// <summary>Pattern ID for telemetry/debugging.</summary>
    public required string Id { get; init; }
    
    /// <summary>Target skill type name.</summary>
    public required string SkillId { get; init; }
    
    /// <summary>
    /// Template patterns with placeholders.
    /// E.g., "turn {action:on|off} [the] {entity}" 
    /// </summary>
    public required IReadOnlyList<string> Templates { get; init; }
    
    /// <summary>Action to invoke on the skill.</summary>
    public required string Action { get; init; }
    
    /// <summary>Minimum confidence to fast-path (0.0-1.0).</summary>
    public float MinConfidence { get; init; } = 0.8f;
}
```

#### Template Syntax
- `{name}` — Required named capture (entity, area, value)
- `{name:opt1|opt2}` — Required named capture with allowed values
- `[word]` — Optional literal word
- `[the|a|an]` — Optional word with alternatives
- Matching is case-insensitive
- STT artifacts tolerated via fuzzy matching (Levenshtein distance)

#### Built-in Patterns

**Light Control**:
```
turn {action:on|off} [the] {entity}
{action:on|off} [the] {entity}
[the] {entity} {action:on|off}
lights {action:on|off} [in] [the] {area}
turn [the] {area} lights {action:on|off}
{action:dim|brighten} [the] {entity} [to] {value} [percent]
set [the] {entity} [brightness] [to] {value} [percent]
```

**Climate Control**:
```
set [the] {entity} [temperature] to {value} [degrees]
make [it] {action:warmer|cooler|hotter|colder} [in] [the] {area}
turn {action:on|off} [the] {entity:heater|ac|fan|air conditioner}
set [the] thermostat [in] [the] {area} to {value}
```

**Scene Control**:
```
activate [the] {scene} [scene]
set [the] scene [to] {scene}
{scene} [scene] [please]
turn on {scene} [scene]
```

**Timer/Alarm** (integrates with existing TimerAgent):
```
set [a] timer [for] {value} {unit:minutes|seconds|hours}
cancel [the] timer
set [an] alarm [for] {value} [am|pm]
```

**Music Control** (integrates with existing MusicAgent):
```
play {query} [on] [the] {entity}
pause [the] music [on] [the] {entity}
stop [the] music
next [song|track]
previous [song|track]
volume {action:up|down} [on] [the] {entity}
set volume [to] {value} [on] [the] {entity}
```

#### Interface for Skills to Register Patterns
```csharp
public interface ICommandPatternProvider
{
    IReadOnlyList<CommandPattern> GetCommandPatterns();
}
```

- Existing skills (`LightControlSkill`, `ClimateControlSkill`, etc.) implement this interface
- Patterns are collected at startup and registered with the router
- Dynamic agents can also register patterns via their agent definitions

**Files**:
- `lucia.Wyoming/CommandRouting/CommandPattern.cs`
- `lucia.Wyoming/CommandRouting/CommandPatternRegistry.cs`
- `lucia.Agents/Abstractions/ICommandPatternProvider.cs` (new, in shared project)

---

### D3: Command Pattern Matcher

**What**: Engine that matches transcribed text against registered patterns

**Implementation Details**:

#### Matching Algorithm
1. **Normalize transcript**: lowercase, remove filler words ("um", "uh", "please")
2. **Tokenize**: split into words
3. **Match against patterns**: For each registered pattern:
   a. Parse template into a regex-like matcher with capture groups
   b. Try to match normalized tokens against template
   c. Score match confidence based on:
      - Token coverage (% of transcript tokens consumed)
      - Entity resolution confidence (via `IEntityLocationService`)
      - Pattern specificity (more specific patterns score higher)
4. **Rank matches**: Sort by confidence score
5. **Apply threshold**: Only return match if above `MinConfidence`

#### Entity Resolution
- Use existing `IEntityLocationService.SearchHierarchyAsync()` to resolve entity names
- Leverage existing `StringSimilarity` for fuzzy entity matching
- Cache entity lookup results per session for repeated commands

#### Confidence Scoring
```csharp
public sealed record CommandRouteResult
{
    public required bool IsMatch { get; init; }
    public required float Confidence { get; init; }
    public CommandPattern? MatchedPattern { get; init; }
    public IReadOnlyDictionary<string, string>? CapturedValues { get; init; }
    public string? ResolvedEntityId { get; init; }
    public string? SpeakerId { get; init; }
    public TimeSpan MatchDuration { get; init; }
}
```

Confidence formula:
- Base score from template match quality (0.0 - 0.5)
- Entity resolution bonus (+0.0 to +0.3 based on match quality)
- Speaker authorization bonus (+0.1 if known speaker, +0.0 if unknown)
- Penalty for leftover unmatched tokens (-0.05 per unmatched token)

**Files**:
- `lucia.Wyoming/CommandRouting/ICommandRouter.cs`
- `lucia.Wyoming/CommandRouting/CommandPatternRouter.cs`
- `lucia.Wyoming/CommandRouting/CommandPatternMatcher.cs`
- `lucia.Wyoming/CommandRouting/CommandRouteResult.cs`
- `lucia.Wyoming/CommandRouting/TranscriptNormalizer.cs`

---

### D4: Skill Dispatcher

**What**: Execute matched commands directly against Lucia services in the same `lucia.AgentHost` process without an HTTP hop

**Implementation Details**:

#### Primary Approach: Direct In-Process Dispatch
When the command router returns a high-confidence match:
1. Resolve the target skill from the pattern's `SkillId`
2. Construct a pre-routed Lucia command from captured values
3. Invoke `ILuciaEngine` or the target skill service directly through DI
4. Return the skill's response text

```csharp
public sealed class SkillDispatcher(ILuciaEngine engine, IServiceProvider services)
{
    public async Task<string> DispatchFastPathAsync(CommandRouteResult route, CancellationToken ct)
    {
        // Direct in-process call to LuciaEngine with pre-routed command
        // No HTTP hop needed — same process
    }

    public async Task<string> FallbackToLlmAsync(string transcript, CommandRouteResult route, CancellationToken ct)
    {
        // Direct call to LuciaEngine.ProcessAsync with enriched context
    }
}
```

- The Wyoming server and Lucia engine share the same DI container, session state, and telemetry pipeline
- For hot paths, `SkillDispatcher` can resolve a specific skill service directly from `IServiceProvider`
- No loopback HTTP client or endpoint is required

#### LLM Fallback
When confidence is below threshold:
- Phase 1 can temporarily bridge into the existing text-based `ILuciaEngine` entry point, but Phase 2 requires an extended voice-native orchestrator contract rather than assuming the current text API is sufficient
- Invoke that in-process contract directly through DI with the full transcript plus speaker verification and routing metadata
- Include speaker verification and routing context on the in-process request:
  ```json
  {
    "text": "what's the weather like tomorrow and remind me about groceries",
    "voice_context": {
      "speaker_id": "user-001",
      "speaker_name": "Zack",
      "partial_matches": [
        { "skill": "ListSkill", "confidence": 0.45, "reason": "groceries → list item?" }
      ],
      "audio_duration_ms": 3200,
      "stt_confidence": 0.92,
      "invocation_mode": "wyoming-voice"
    }
  }
  ```
- Preserve the existing AgentHost `contextId` / `sessionId` because Wyoming and Lucia execute inside the same process

**Files**:
- `lucia.Wyoming/CommandRouting/SkillDispatcher.cs`

---

### D5: Session Pipeline Update

**What**: Integrate speaker verification and command routing into the Wyoming session flow inside `lucia.AgentHost`

**Implementation Details**:

Update `WyomingSession` to include post-STT processing:

```
Wyoming Audio → VAD → STT → transcript
                             │
                    ┌────────┴────────┐
                    ▼                 ▼
              Speaker Embed    Command Router
                    │                 │
                    └────────┬────────┘
                             ▼
                    ┌──────────────────────┐
                    │   SkillDispatcher /  │
                    │   LuciaEngine        │
                    │ (fast-path or LLM)   │
                    └────────┬─────────────┘
                             ▼
                       [response text]
```

- Speaker embedding extraction runs in parallel with command matching
- High-confidence routes call `SkillDispatcher.DispatchFastPathAsync(...)` directly in-process
- Fallback routes call `SkillDispatcher.FallbackToLlmAsync(...)`, which invokes the extended `ILuciaEngine` voice invocation contract directly without HTTP or IPC
- Telemetry spans capture speaker verification, matching, fast-path dispatch, and LLM fallback within the shared AgentHost process

**Files**:
- `lucia.Wyoming/Wyoming/WyomingSession.cs` (modify)

---

### D6: Speaker Enrollment API

**What**: REST API for enrolling speaker profiles

**Implementation Details**:
- `POST /api/speakers/enroll` — Upload reference audio + speaker name
- `GET /api/speakers` — List enrolled speakers
- `DELETE /api/speakers/{id}` — Remove speaker profile
- `POST /api/speakers/{id}/test` — Test identification with audio sample
- Endpoints are mapped in `lucia.AgentHost` alongside the rest of the HTTP APIs while the underlying speaker-verification services live in `lucia.Wyoming`
- The same AgentHost process exposes both the HTTP API surface and the Wyoming TCP server

**Files**:
- `lucia.AgentHost/Apis/SpeakerApi.cs`
- `lucia.AgentHost/Program.cs` (map endpoints)

---

### D7: Skill Pattern Registration

**What**: Update existing Lucia skills to expose command patterns

**Implementation Details**:
- Add `ICommandPatternProvider` interface to `lucia.Agents`
- Implement on `LightControlSkill`, `ClimateControlSkill`, `SceneControlSkill`
- `ListSkill` and `MusicPlaybackSkill` also implement
- Patterns loaded by `CommandPatternRegistry` at startup

**Files**:
- `lucia.Agents/Abstractions/ICommandPatternProvider.cs` (new)
- `lucia.Agents/Skills/LightControlSkill.cs` (modify)
- `lucia.Agents/Skills/ClimateControlSkill.cs` (modify)
- `lucia.Agents/Skills/SceneControlSkill.cs` (modify)
- `lucia.Agents/Skills/ListSkill.cs` (modify)
- `lucia.MusicAgent/MusicPlaybackSkill.cs` (modify)

---

### D8: Tests

**Test Cases**:

#### Command Pattern Matching
- `PatternMatcher_ExactMatch_TurnOnLights`
- `PatternMatcher_FuzzyMatch_WithSttArtifacts`
- `PatternMatcher_EntityResolution_MatchesHAEntity`
- `PatternMatcher_BelowThreshold_ReturnsNoMatch`
- `PatternMatcher_MultiplePatterns_ReturnsHighestConfidence`
- `PatternMatcher_OptionalWords_MatchesWithAndWithout`
- `PatternMatcher_CaptureGroups_ExtractsValues`

#### Speaker Diarization
- `DiarizationEngine_ExtractEmbedding_ReturnsVector`
- `DiarizationEngine_IdentifyEnrolledSpeaker_ReturnsMatch`
- `DiarizationEngine_UnknownSpeaker_ReturnsNull`
- `DiarizationEngine_MultipleSpeakers_DistinguishesCorrectly`

#### Command Routing
- `CommandRouter_HighConfidence_DispatchesToSkill`
- `CommandRouter_LowConfidence_FallsBackToLlm`
- `CommandRouter_NoMatch_FallsBackToLlm`
- `CommandRouter_AuthorizedSpeaker_Executes`
- `CommandRouter_UnauthorizedSpeaker_Rejects`
- `CommandRouter_EnrichesLlmFallback_WithContext`

#### Integration
- `Pipeline_WakeWord_Stt_FastPath_EndToEnd`
- `Pipeline_WakeWord_Stt_LlmFallback_EndToEnd`

**Files**:
- `lucia.Tests/Wyoming/CommandPatternMatcherTests.cs`
- `lucia.Tests/Wyoming/CommandPatternRouterTests.cs`
- `lucia.Tests/Wyoming/TranscriptNormalizerTests.cs`

---

### D9: Automated Unknown Speaker Discovery

**What**: System that automatically detects, tracks, and manages unknown voice profiles

**Implementation Details**:

#### Provisional Profile Creation
When the speaker-verification engine encounters a voice embedding that doesn't match any enrolled speaker:

```csharp
public sealed class UnknownSpeakerTracker
{
    private readonly SpeakerProfileStore _profileStore;
    private readonly ILogger<UnknownSpeakerTracker> _logger;
    
    public async Task<SpeakerProfile> TrackUnknownSpeakerAsync(
        SpeakerEmbedding embedding, CancellationToken ct)
    {
        // Check if this embedding matches an existing provisional profile
        var provisionalProfiles = await _profileStore.GetProvisionalProfilesAsync(ct);
        
        foreach (var profile in provisionalProfiles)
        {
            var similarity = CosineSimilarity(embedding.Vector, profile.AverageEmbedding);
            if (similarity > _options.ProvisionalMatchThreshold) // default: 0.65
            {
                // Known unknown speaker — update their profile
                profile.AddEmbedding(embedding);
                profile.InteractionCount++;
                profile.LastSeenAt = DateTimeOffset.UtcNow;
                await _profileStore.UpdateAsync(profile, ct);
                
                // Check if enrollment suggestion threshold reached
                if (profile.InteractionCount >= _options.SuggestEnrollmentAfter) // default: 5
                {
                    await SuggestEnrollmentAsync(profile, ct);
                }
                
                return profile;
            }
        }
        
        // Truly new unknown speaker — create provisional profile
        var newProfile = new SpeakerProfile
        {
            Id = $"unknown-{Guid.NewGuid():N}",
            Name = $"Unknown Speaker {provisionalProfiles.Count + 1}",
            IsProvisional = true,
            IsAuthorized = false,
            Embeddings = [embedding.Vector],
            AverageEmbedding = embedding.Vector,
            InteractionCount = 1,
            EnrolledAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow,
        };
        
        await _profileStore.CreateAsync(newProfile, ct);
        _logger.LogInformation("Created provisional profile {ProfileId} for new unknown speaker", newProfile.Id);
        
        return newProfile;
    }
    
    private async Task SuggestEnrollmentAsync(SpeakerProfile profile, CancellationToken ct)
    {
        // Emit event for dashboard notification
        // Optionally queue TTS prompt for next interaction:
        // "I've noticed a new voice around. Would you like to set up a voice profile?"
    }
}
```

#### Provisional Profile Lifecycle
- Created automatically on first unknown voice detection
- Embeddings accumulated with each subsequent interaction
- Enrollment suggested after configurable interaction count (default: 5)
- Auto-expired after configurable inactivity period (default: 30 days)
- Promoted to full profile via onboarding flow
- Cleanup job runs daily to purge expired provisionals

#### Background Cleanup Service
```csharp
public sealed class ProvisionalProfileCleanupService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromHours(24), ct);
            var expired = await _profileStore.GetExpiredProvisionalProfilesAsync(
                _options.ProvisionalRetentionDays, ct); // default: 30
            foreach (var profile in expired)
            {
                await _profileStore.DeleteAsync(profile.Id, ct);
            }
        }
    }
}
```

**Files**:
- `lucia.Wyoming/Diarization/UnknownSpeakerTracker.cs`
- `lucia.Wyoming/Diarization/ProvisionalProfileCleanupService.cs`

---

### D10: Voice Onboarding Flow

**What**: Guided setup experience for training a user's voiceprint through spoken prompts

**Implementation Details**:

#### Onboarding Session Manager
```csharp
public sealed class VoiceOnboardingService
{
    private static readonly string[] OnboardingPrompts =
    [
        "Please say: Turn on the living room lights",
        "Please say: What's the weather like today",
        "Please say: Set the thermostat to seventy two degrees",
        "Please say: Play some music in the kitchen",
        "Please say: Hey Lucia, good morning",
        "Please say: Set a timer for five minutes",
        "Please say: Turn off all the lights",
    ];
    
    public async Task<OnboardingSession> StartOnboardingAsync(
        string speakerName, string? provisionalProfileId, CancellationToken ct)
    {
        var session = new OnboardingSession
        {
            Id = Guid.NewGuid().ToString("N"),
            SpeakerName = speakerName,
            ProvisionalProfileId = provisionalProfileId,
            Prompts = SelectPrompts(count: 5), // Randomly pick 5 of 7
            CollectedEmbeddings = [],
            CurrentPromptIndex = 0,
            StartedAt = DateTimeOffset.UtcNow,
            Status = OnboardingStatus.InProgress,
        };
        
        await _sessionStore.SaveAsync(session, ct);
        return session;
    }
    
    public async Task<OnboardingStepResult> ProcessSampleAsync(
        string sessionId, ReadOnlyMemory<float> audioSamples, int sampleRate,
        CancellationToken ct)
    {
        var session = await _sessionStore.GetAsync(sessionId, ct);
        
        // Validate audio quality
        var quality = AnalyzeAudioQuality(audioSamples);
        if (quality.IsTooQuiet)
            return OnboardingStepResult.Retry("That was a bit quiet. Could you speak a little louder?");
        if (quality.IsTooShort)
            return OnboardingStepResult.Retry("I need a longer sample. Please say the full phrase.");
        if (quality.IsNoisy)
            return OnboardingStepResult.Retry("There's too much background noise. Could you try a quieter spot?");
        
        // Extract embedding
        var embedding = _speakerVerificationEngine.ExtractEmbedding(audioSamples.Span, sampleRate);
        session.CollectedEmbeddings.Add(embedding.Vector);
        session.CurrentPromptIndex++;
        
        if (session.CurrentPromptIndex >= session.Prompts.Count)
        {
            // All samples collected — finalize profile
            var profile = await FinalizeEnrollmentAsync(session, ct);
            return OnboardingStepResult.Complete(
                $"Voice profile created for {session.SpeakerName}. I'll recognize your voice from now on.");
        }
        
        // More prompts to go
        var nextPrompt = session.Prompts[session.CurrentPromptIndex];
        return OnboardingStepResult.NextPrompt(nextPrompt);
    }
    
    private async Task<SpeakerProfile> FinalizeEnrollmentAsync(
        OnboardingSession session, CancellationToken ct)
    {
        // Compute averaged embedding from all samples
        var averageEmbedding = ComputeAverageEmbedding(session.CollectedEmbeddings);
        
        SpeakerProfile profile;
        if (session.ProvisionalProfileId is not null)
        {
            // Promote provisional profile to full
            profile = await _profileStore.GetAsync(session.ProvisionalProfileId, ct);
            profile = profile with
            {
                Name = session.SpeakerName,
                IsProvisional = false,
                IsAuthorized = true,
                Embeddings = session.CollectedEmbeddings.ToArray(),
                AverageEmbedding = averageEmbedding,
            };
            await _profileStore.UpdateAsync(profile, ct);
        }
        else
        {
            // Create new full profile
            profile = new SpeakerProfile
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = session.SpeakerName,
                IsProvisional = false,
                IsAuthorized = true,
                Embeddings = session.CollectedEmbeddings.ToArray(),
                AverageEmbedding = averageEmbedding,
                EnrolledAt = DateTimeOffset.UtcNow,
            };
            await _profileStore.CreateAsync(profile, ct);
        }
        
        return profile;
    }
}
```

#### Voice-Initiated Onboarding
When a user says "Hey Lucia, set up my voice" or "Hey Lucia, learn my voice":
1. The command router detects the onboarding intent (registered as a command pattern)
2. Starts an onboarding session
3. TTS speaks the first prompt
4. System enters onboarding mode — each subsequent utterance is processed as a sample (not a command)
5. After all prompts, confirms enrollment via TTS

#### Dashboard-Initiated Onboarding
REST API endpoints (in AgentHost):
- `POST /api/onboarding/start` — Start session with speaker name
- `POST /api/onboarding/{sessionId}/sample` — Upload audio sample
- `GET /api/onboarding/{sessionId}` — Get session status/next prompt
- `POST /api/onboarding/{sessionId}/finish` — Finalize enrollment and wake word selection
- `POST /api/onboarding/{sessionId}/cancel` — Cancel session

The dashboard can show a guided UI with visual prompts and audio recording.

#### Audio Quality Validation
Check each sample for:
- **Duration**: Minimum 1.5 seconds of speech (after VAD)
- **Volume**: RMS energy above noise floor threshold
- **Noise**: SNR estimate above minimum (10 dB)
- **Embedding quality**: Extracted embedding must have reasonable norm

**Files**:
- `lucia.Wyoming/Diarization/VoiceOnboardingService.cs`
- `lucia.Wyoming/Diarization/OnboardingSession.cs`
- `lucia.Wyoming/Diarization/OnboardingStepResult.cs`
- `lucia.Wyoming/Diarization/AudioQualityAnalyzer.cs`
- `lucia.AgentHost/Apis/OnboardingApi.cs`

---

### D11: Ignore Unknown Voices Mode

**What**: Configurable filter that silently drops commands from unrecognized speakers

**Implementation Details**:

#### Configuration
```json
{
  "Wyoming": {
    "VoiceProfiles": {
      "IgnoreUnknownVoices": false,
      "SpeakerVerificationThreshold": 0.7,
      "AdaptiveProfiles": true,
      "ProvisionalRetentionDays": 30,
      "SuggestEnrollmentAfter": 5
    }
  }
}
```

#### Filtering Logic
Integrated into the Wyoming session pipeline, after STT but before command routing:

```csharp
// In the command routing pipeline
public async Task<CommandRouteResult?> ProcessWithSpeakerFilterAsync(
    string transcript,
    SpeakerIdentification? speaker,
    CancellationToken ct)
{
    if (_options.IgnoreUnknownVoices && speaker is null)
    {
        _logger.LogDebug(
            "Ignoring command from unknown speaker (ignore_unknown_voices=true): {Transcript}",
            transcript);
        
        _metrics.UnknownVoiceFiltered();
        
        // Still track the unknown speaker for provisional profiles
        // but don't process the command
        return null; // Signal to session: no response needed
    }
    
    // Speaker is known, or ignore mode is off — proceed normally
    return await _commandRouter.RouteAsync(transcript, speaker, ct);
}
```

#### Interaction with Wake Word
The filtering happens AFTER wake word + STT, not before:
1. Wake word detected → start recording
2. STT transcribes the utterance
3. Speaker embedding extracted from utterance audio
4. Speaker identification attempted
5. **If ignore-unknown enabled AND speaker unrecognized → silently discard**
6. If speaker recognized (or ignore-unknown disabled) → proceed to routing

This means wake word detection still fires for unknown voices, but the command is dropped at the verification step. This is intentional:
- Wake word is cheap (lightweight KWS model)
- STT is needed to extract good audio for speaker embedding
- The small overhead of STT on rejected audio is acceptable vs. the alternative (running speaker ID on raw wake word audio is less accurate)

#### Adaptive Profile Updates
When an enrolled speaker is positively identified with high confidence (> 0.85):
```csharp
if (_options.AdaptiveProfiles && speaker.Similarity > 0.85f)
{
    // Incrementally update the speaker's embedding with this new sample
    // Uses exponential moving average to slowly adapt to voice changes
    var alpha = 0.05f; // Small weight for new sample
    var updatedEmbedding = new float[speaker.AverageEmbedding.Length];
    for (int i = 0; i < updatedEmbedding.Length; i++)
    {
        updatedEmbedding[i] = (1 - alpha) * profile.AverageEmbedding[i] 
                            + alpha * currentEmbedding.Vector[i];
    }
    
    await _profileStore.UpdateEmbeddingAsync(speaker.ProfileId, updatedEmbedding, ct);
}
```

#### Telemetry
- `wyoming.speaker.unknown_filtered` — Counter for filtered unknown voice commands
- `wyoming.speaker.identified` — Counter by speaker name
- `wyoming.speaker.adaptive_update` — Counter for profile updates
- `wyoming.speaker.onboarding_completed` — Counter for successful enrollments
- `wyoming.speaker.provisional_created` — Counter for new provisional profiles

**Files**:
- `lucia.Wyoming/Diarization/SpeakerVerificationFilter.cs`
- `lucia.Wyoming/Diarization/AdaptiveProfileUpdater.cs`

---

### D12: Browser-Based Onboarding UI

**What**: A responsive web page served by AgentHost that guides users through voice profile enrollment AND wake word calibration using their device's microphone (phone, tablet, laptop)

**Implementation Details**:

#### Architecture
The onboarding UI is a standalone HTML/JS page served from AgentHost's static files, not part of the React dashboard bundle. This keeps it lightweight while still requiring a fully configured Lucia deployment and the normal dashboard/auth setup before onboarding begins.

```
User's Phone/Laptop Browser
    │
    │  getUserMedia() → Mic Audio
    │  WebSocket or chunked POST → Audio samples
    │
    ▼
lucia.AgentHost
    ├── GET /onboarding                       → Serves onboarding HTML page
    ├── POST /api/onboarding/start            → Start session (name, wake word)
    ├── POST /api/onboarding/{id}/sample      → Upload audio sample (WAV/PCM)
    ├── GET  /api/onboarding/{id}             → Session status + next prompt
    ├── POST /api/onboarding/{id}/finish      → Finalize profile + wake word
    └── POST /api/onboarding/{id}/cancel      → Cancel session
```

#### Onboarding Flow (Single Guided Session)

The page walks through 4 steps in order:

**Step 1: Welcome & Name**
```
┌──────────────────────────────────────┐
│  Welcome to Lucia Voice Setup        │
│                                      │
│  What should I call you?             │
│  ┌──────────────────────────────┐    │
│  │ Zack                         │    │
│  └──────────────────────────────┘    │
│                                      │
│           [ Next → ]                 │
└──────────────────────────────────────┘
```

**Step 2: Choose Wake Word**
```
┌──────────────────────────────────────┐
│  Choose your wake word               │
│                                      │
│  ○ Hey Lucia (default)               │
│  ○ Hey Jarvis                        │
│  ○ Computer                          │
│  ○ OK Home                           │
│  ● Custom: ┌──────────────────┐      │
│            │ Hey Ziggy         │      │
│            └──────────────────┘      │
│                                      │
│  Tip: Phrases with 3+ syllables      │
│  work best for avoiding false        │
│  triggers.                           │
│                                      │
│      [ ← Back ]  [ Next → ]         │
└──────────────────────────────────────┘
```

**Step 3: Voice Profile Training**
```
┌──────────────────────────────────────┐
│  Voice Profile Training (3/5)        │
│  ═══════════════════ 60%             │
│                                      │
│  Please say:                         │
│  "Set the thermostat to 72 degrees"  │
│                                      │
│  ┌──────────────────────────────┐    │
│  │  🎤 ████████░░░░  Listening  │    │
│  │     ▁▂▃▅▇▅▃▂▁  (audio meter)│    │
│  └──────────────────────────────┘    │
│                                      │
│  ✅ Sample 1: "Turn on the lights"   │
│  ✅ Sample 2: "What's the weather"   │
│  🔵 Sample 3: Recording...          │
│  ⬜ Sample 4                         │
│  ⬜ Sample 5                         │
│                                      │
│      [ ← Back ]  [ Skip step ]      │
└──────────────────────────────────────┘
```

**Step 4: Wake Word Calibration**
```
┌──────────────────────────────────────┐
│  Wake Word Calibration (2/3)         │
│  ═══════════════ 66%                 │
│                                      │
│  Say your wake word:                 │
│  "Hey Ziggy"                         │
│                                      │
│  ┌──────────────────────────────┐    │
│  │  🎤 ████████░░░░  Listening  │    │
│  └──────────────────────────────┘    │
│                                      │
│  ✅ Try 1: Detected (conf: 0.82)     │
│  🔵 Try 2: Recording...             │
│  ⬜ Try 3                            │
│                                      │
│  [ ← Back ] [ Skip calibration ]    │
└──────────────────────────────────────┘
```

**Completion**
```
┌──────────────────────────────────────┐
│  🎉 All set, Zack!                  │
│                                      │
│  ✅ Voice profile created            │
│  ✅ Wake word "Hey Ziggy" active     │
│  ✅ Detection sensitivity: High      │
│                                      │
│  Your setup is complete. Try saying  │
│  "Hey Ziggy" to any satellite!       │
│                                      │
│           [ Done ]                   │
└──────────────────────────────────────┘
```

#### Browser Audio Recording Implementation

```javascript
// Core recording logic using Web Audio API
class AudioRecorder {
    constructor() {
        this.audioContext = null;
        this.mediaStream = null;
        this.processor = null;
        this.chunks = [];
    }
    
    async requestMicAccess() {
        this.mediaStream = await navigator.mediaDevices.getUserMedia({
            audio: {
                sampleRate: 16000,
                channelCount: 1,
                echoCancellation: true,
                noiseSuppression: true,
            }
        });
        this.audioContext = new AudioContext({ sampleRate: 16000 });
    }
    
    startRecording() {
        const source = this.audioContext.createMediaStreamSource(this.mediaStream);
        this.processor = this.audioContext.createScriptProcessor(4096, 1, 1);
        
        this.chunks = [];
        this.processor.onaudioprocess = (event) => {
            const samples = event.inputBuffer.getChannelData(0);
            this.chunks.push(new Float32Array(samples));
            
            // Update audio level meter
            const rms = Math.sqrt(samples.reduce((sum, s) => sum + s * s, 0) / samples.length);
            this.onLevelUpdate?.(rms);
        };
        
        source.connect(this.processor);
        this.processor.connect(this.audioContext.destination);
    }
    
    stopRecording() {
        this.processor?.disconnect();
        // Concatenate chunks into single Float32Array
        const totalLength = this.chunks.reduce((sum, c) => sum + c.length, 0);
        const audio = new Float32Array(totalLength);
        let offset = 0;
        for (const chunk of this.chunks) {
            audio.set(chunk, offset);
            offset += chunk.length;
        }
        return audio;
    }
    
    async uploadSample(sessionId, audio) {
        // Convert Float32 to 16-bit PCM WAV
        const wavBlob = encodeWav(audio, 16000);
        
        const formData = new FormData();
        formData.append('audio', wavBlob, 'sample.wav');
        
        const response = await fetch(`/api/onboarding/${sessionId}/sample`, {
            method: 'POST',
            body: formData,
        });
        
        return response.json();
    }
}
```

#### Real-Time Audio Level Visualization

```javascript
// Audio level meter using CSS custom properties
function updateMeter(rms) {
    const level = Math.min(1.0, rms * 10); // Normalize to 0-1
    document.querySelector('.audio-meter').style.setProperty('--level', level);
    
    // Provide feedback on recording quality
    if (level < 0.02) {
        showHint("Speak a bit louder");
    } else if (level > 0.8) {
        showHint("You're a bit close to the mic");
    }
}
```

#### REST API Endpoints for Onboarding

```csharp
// In lucia.AgentHost
public static class OnboardingApi
{
    public static void MapOnboardingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/onboarding");
        
        // Serve the onboarding HTML page
        app.MapGet("/onboarding", () => Results.File(
            "wwwroot/onboarding.html", "text/html"));
        
        // Start a new onboarding session
        group.MapPost("/start", async (
            OnboardingStartRequest request,
            VoiceOnboardingService onboarding,
            CustomWakeWordManager wakeWords) =>
        {
            var session = await onboarding.StartOnboardingAsync(
                request.SpeakerName, provisionalProfileId: null, default);
            
            // Also register the wake word (text only, no calibration yet)
            CustomWakeWord? wakeWord = null;
            if (!string.IsNullOrEmpty(request.WakeWordPhrase))
            {
                wakeWord = await wakeWords.RegisterWakeWordAsync(
                    request.WakeWordPhrase, session.Id, default);
            }
            
            return Results.Ok(new
            {
                SessionId = session.Id,
                WakeWordId = wakeWord?.Id,
                FirstPrompt = session.Prompts[0],
                TotalSteps = session.Prompts.Count + (request.CalibrationEnabled ? 3 : 0),
            });
        });
        
        // Upload an audio sample (voice profile or wake word calibration)
        group.MapPost("/{sessionId}/sample", async (
            string sessionId,
            HttpRequest request,
            VoiceOnboardingService onboarding) =>
        {
            var form = await request.ReadFormAsync();
            var audioFile = form.Files["audio"];
            
            // Read WAV and convert to float samples
            var samples = await AudioFileHelper.ReadWavAsFloatAsync(audioFile.OpenReadStream());
            
            var result = await onboarding.ProcessSampleAsync(sessionId, samples, 16000, default);
            
            return Results.Ok(result);
        });
        
        // Upload a wake word calibration sample
        group.MapPost("/{sessionId}/calibrate", async (
            string sessionId,
            HttpRequest request,
            CustomWakeWordManager wakeWords,
            VoiceOnboardingService onboarding) =>
        {
            var form = await request.ReadFormAsync();
            var audioFile = form.Files["audio"];
            var wakeWordId = form["wakeWordId"].ToString();
            
            var samples = await AudioFileHelper.ReadWavAsFloatAsync(audioFile.OpenReadStream());
            
            // Accumulate calibration samples, calibrate when enough collected
            var session = await onboarding.GetSessionAsync(sessionId, default);
            session.CalibrationSamples.Add(samples);
            
            if (session.CalibrationSamples.Count >= 3)
            {
                var result = await wakeWords.CalibrateAsync(
                    wakeWordId, session.CalibrationSamples, 16000, default);
                return Results.Ok(new { Step = "calibration_complete", result });
            }
            
            return Results.Ok(new
            {
                Step = "calibration_sample_accepted",
                SamplesCollected = session.CalibrationSamples.Count,
                SamplesNeeded = 3,
            });
        });
        
        // Get session status
        group.MapGet("/{sessionId}", async (
            string sessionId,
            VoiceOnboardingService onboarding) =>
        {
            var session = await onboarding.GetSessionAsync(sessionId, default);
            return Results.Ok(session);
        });
        
        // Finalize onboarding
        group.MapPost("/{sessionId}/finish", async (
            string sessionId,
            VoiceOnboardingService onboarding) =>
        {
            var summary = await onboarding.FinalizeSessionAsync(sessionId, default);
            return Results.Ok(summary);
        });
    }
}
```

#### Static HTML Page

The onboarding page is a single `wwwroot/onboarding.html` file using:
- Vanilla JS + Web Audio API (no framework dependencies)
- Tailwind CSS via CDN for styling
- Progressive step-by-step flow with back/skip navigation
- Responsive design (mobile-first)
- Real-time audio level meter with visual feedback
- Error handling with retry for failed recordings
- Animated transitions between steps

The page is intentionally NOT part of the React dashboard bundle to ensure:
- Lucia onboarding stays available after the main deployment and dashboard/auth configuration are complete
- No additional front-end build step is required
- It can be opened from any browser that can reach the configured Lucia deployment
- It can be linked from the dashboard without duplicating onboarding UI code

**Files**:
- `lucia.AgentHost/wwwroot/onboarding.html` (static HTML + JS + CSS)
- `lucia.AgentHost/Apis/OnboardingApi.cs`
- `lucia.Wyoming/Audio/AudioFileHelper.cs` (WAV parsing utility)

---

## Task Breakdown

| ID | Task | Parallel | Description |
|----|------|----------|-------------|
| P2-DIAR-001 | Implement IDiarizationEngine | Yes | Speaker embedding abstraction |
| P2-DIAR-002 | Implement SherpaDiarizationEngine | No | sherpa-onnx speaker embedding wrapper |
| P2-DIAR-003 | Implement SpeakerProfileStore | Yes | MongoDB-backed speaker profile persistence |
| P2-DIAR-004 | Implement SpeakerApi | No | AgentHost REST endpoints for enrollment/management |
| P2-CMD-001 | Define ICommandPatternProvider | Yes | Interface in lucia.Agents |
| P2-CMD-002 | Implement CommandPattern model | Yes | Pattern definition with template syntax |
| P2-CMD-003 | Implement CommandPatternMatcher | No | Template matching + fuzzy + entity resolution |
| P2-CMD-004 | Implement CommandPatternRouter | No | Routing decision engine with confidence scoring |
| P2-CMD-005 | Implement SkillDispatcher | No | Direct in-process dispatch via ILuciaEngine / skill services with the new voice-native invocation contract |
| P2-CMD-007 | Implement TranscriptNormalizer | Yes | Text normalization for matching |
| P2-CMD-008 | Register patterns on existing skills | No | LightControl, Climate, Scene, List, Music |
| P2-INT-001 | Update WyomingSession for routing | No | Integrate speaker verification + routing into session |
| P2-INT-002 | Add OpenTelemetry instrumentation | No | Spans for speaker verification, matching, routing |
| P2-VP-001 | Implement UnknownSpeakerTracker | No | Auto-discovery and provisional profile management |
| P2-VP-002 | Implement ProvisionalProfileCleanupService | Yes | Background cleanup of expired provisional profiles |
| P2-VP-003 | Implement VoiceOnboardingService | No | Guided voice enrollment with multi-prompt flow |
| P2-VP-004 | Implement AudioQualityAnalyzer | Yes | Audio sample quality validation for onboarding |
| P2-VP-005 | Implement SpeakerVerificationFilter | No | Ignore-unknown-voices command filtering |
| P2-VP-006 | Implement AdaptiveProfileUpdater | Yes | Incremental profile embedding updates |
| P2-VP-007 | Add onboarding API endpoints | No | REST endpoints for dashboard-driven enrollment |
| P2-VP-008 | Register voice onboarding command pattern | No | "set up my voice" / "learn my voice" patterns |
| P2-VP-009 | Write voice profile tests | Yes | Unknown tracking, onboarding flow, filter tests |
| P2-OB-001 | Create onboarding HTML page | No | Responsive browser UI with Web Audio mic recording |
| P2-OB-002 | Implement OnboardingApi | No | REST endpoints for session, sample upload, calibration |
| P2-OB-003 | Implement AudioFileHelper | Yes | WAV file parsing and float sample extraction |
| P2-OB-004 | Integrate voice + wake word flow | No | Combined session for profile enrollment + wake calibration |
| P2-OB-005 | Write onboarding integration tests | Yes | API endpoint tests, audio upload, session flow |
| P2-TEST-001 | Write unit tests | Yes | Pattern matcher, router, normalizer tests |
| P2-TEST-002 | Write integration tests | No | End-to-end fast-path + fallback tests |

---

## Acceptance Criteria

Phase 2 is complete when:
1. ✅ Known command patterns (lights, climate, scenes) are fast-pathed without LLM
2. ✅ Fast-path commands execute in < 200ms from transcript availability
3. ✅ Unrecognized commands fall back to the LLM orchestrator through the richer voice-native invocation contract with enriched context
4. ✅ Speaker enrollment and single-utterance speaker verification work with > 85% accuracy
5. ✅ At least 70% of common home automation commands are fast-pathable
6. ✅ Telemetry distinguishes fast-path from LLM-routed commands
7. ✅ All unit and integration tests pass
8. ✅ Pattern matching tolerates common STT artifacts (homophones, missing articles)
9. ✅ Unknown speakers are automatically tracked with provisional profiles
10. ✅ Voice onboarding flow completes successfully with 5 spoken prompts
11. ✅ "Ignore unknown voices" mode filters unrecognized speakers with ≥ 98% accuracy
12. ✅ Adaptive profiles update enrolled speaker embeddings over time
13. ✅ Provisional profiles auto-expire after configured retention period
14. ✅ Browser onboarding page works on mobile Chrome, Safari, and Firefox
15. ✅ Full onboarding (voice profile + wake word + calibration) completes in < 3 minutes
16. ✅ Audio level meter provides real-time feedback during recording
17. ✅ Skip options available for calibration steps (voice-only or wake-only possible)
