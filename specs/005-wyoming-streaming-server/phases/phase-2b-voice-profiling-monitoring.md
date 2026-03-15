# Phase 2b: Advanced Voice Profiling, Live Monitoring & Configuration

**Phase**: 2b (extends Phase 2 — Speaker Verification & Command Routing)
**Priority**: P1 — Voice Platform Management
**Dependencies**: Phase 2 (Diarization Engine), Model Management Platform (implemented)
**Estimated Complexity**: High

## Objective

Extend the voice platform with three capabilities that turn the existing diarization infrastructure into a manageable, observable system:

1. **Diarization configuration UI** — expose existing settings (enrolled-only mode, auto-profiling, thresholds) in the dashboard so users can tune voice recognition behavior without editing config files.
2. **Audio clip capture & profile management** — save utterance audio clips when auto-profiling creates provisional speakers so users can listen, identify, merge, and reassign voice samples from the dashboard.
3. **Real-time monitoring dashboard** — SSE-powered live view of connected Wyoming clients, incoming transcripts, detected speakers, and voice count per stream.

---

## Architecture

```
                     ┌──────────────────────────────────────────┐
                     │          Voice Platform Dashboard         │
                     │                                          │
                     │  ┌─────────┐ ┌──────────┐ ┌──────────┐  │
                     │  │Settings │ │Profiles  │ │ Monitor  │  │
                     │  │ Panel   │ │+ Clips   │ │  (Live)  │  │
                     │  └────┬────┘ └────┬─────┘ └────┬─────┘  │
                     │       │           │            │ SSE     │
                     └───────┼───────────┼────────────┼─────────┘
                             │ REST      │ REST       │
                     ┌───────┼───────────┼────────────┼─────────┐
                     │       ▼           ▼            ▼         │
                     │  VoiceConfig  ProfileMgmt  SessionStream │
                     │    API          API          SSE API     │
                     │       │           │            ▲         │
                     │       ▼           ▼            │         │
                     │  IOptions     ISpeaker     SessionEvent  │
                     │  Monitor      ProfileStore    Channel    │
                     │       │           │            ▲         │
                     │       │      ┌────┴───┐        │         │
                     │       │      │ Audio  │        │         │
                     │       │      │ Clips  │        │         │
                     │       │      │ (disk) │        │         │
                     │       │      └────────┘        │         │
                     │       │                        │         │
                     │       ▼                        │         │
                     │  Wyoming Session ──────────────┘         │
                     │  (transcript + speaker + audio capture)  │
                     └──────────────────────────────────────────┘
```

---

## Deliverables

### D1: Diarization Configuration API & UI

**What**: REST endpoints to read and update voice profile settings at runtime, with a Settings panel in the Voice Platform dashboard.

#### Backend

**New file**: `lucia.AgentHost/Apis/VoiceConfigApi.cs`

Endpoints:
```
GET  /api/wyoming/voice-config        → Current VoiceProfileOptions + DiarizationOptions
PUT  /api/wyoming/voice-config        → Update settings at runtime
```

The PUT endpoint updates `IOptionsMonitor<VoiceProfileOptions>` values in-memory. For persistence across restarts, write a `voiceconfig.json` override file to the data directory and load it via the configuration builder at startup.

**Exposed settings**:

| Setting | Type | Description | UI Control |
|---------|------|-------------|------------|
| `IgnoreUnknownVoices` | bool | Reject commands from unidentified speakers ("enrolled only" mode) | Toggle |
| `AutoCreateProvisionalProfiles` | bool | Whether `UnknownSpeakerTracker` creates provisional profiles (NEW) | Toggle |
| `MaxAutoProfiles` | int | Maximum auto-created provisional profiles with audio clips (NEW) | Number input (1–50) |
| `SpeakerVerificationThreshold` | float | Similarity threshold for speaker matching | Slider (0.5–0.95) |
| `ProvisionalMatchThreshold` | float | Threshold for matching unknowns to existing provisionals | Slider (0.5–0.9) |
| `AdaptiveProfiles` | bool | Incrementally improve speaker models during use | Toggle |
| `ProvisionalRetentionDays` | int | Auto-expiry for unused provisional profiles | Number input |
| `SuggestEnrollmentAfter` | int | Interaction count before suggesting enrollment | Number input |

**New option**: Add `AutoCreateProvisionalProfiles` (bool, default true) to `VoiceProfileOptions`. The `UnknownSpeakerTracker` checks this before creating new provisional profiles.

#### Frontend

**Location**: New "Settings" section in the Profiles tab (or a new Settings tab) on `VoicePlatformPage.tsx`.

Controls:
- **"Enrolled voices only"** toggle → `IgnoreUnknownVoices`
- **"Auto-profile new voices"** toggle → `AutoCreateProvisionalProfiles`
- **"Speaker match threshold"** slider → `SpeakerVerificationThreshold`
- **"Adaptive learning"** toggle → `AdaptiveProfiles`
- **"Keep unknown profiles for"** number → `ProvisionalRetentionDays` days
- Save button → PUT to API

---

### D2: Audio Clip Capture & Storage

**What**: When `UnknownSpeakerTracker` creates or updates a provisional profile, save the utterance audio as a WAV file on disk. Users can play clips in the dashboard to identify speakers.

#### Audio Writer Utility

**New file**: `lucia.Wyoming/Audio/WavWriter.cs`

```csharp
public static class WavWriter
{
    public static async Task WriteAsync(
        string filePath,
        ReadOnlyMemory<float> samples,
        int sampleRate,
        CancellationToken ct = default);
}
```

Writes standard 16-bit PCM mono WAV files from float32 samples.

#### Clip Storage

**Directory structure**:
```
{AudioClipBasePath}/
  {profileId}/
    {clipId}.wav          # 16-bit PCM mono WAV
    {clipId}.json         # Metadata (timestamp, duration, sampleRate, transcript)
```

**New option** in `VoiceProfileOptions`:
```csharp
public string AudioClipBasePath { get; set; } = "./data/voice-clips";
public int MaxClipsPerProfile { get; set; } = 20;
```

#### Integration Point

In `WyomingSession.IdentifySpeakerAsync` (or `ProcessTranscriptAsync`), after `UnknownSpeakerTracker.TrackUnknownSpeakerAsync` returns:
1. If a provisional profile was created or updated:
2. Save the utterance audio buffer as a WAV clip
3. Write metadata JSON alongside

**New service**: `lucia.Wyoming/Diarization/AudioClipService.cs`

```csharp
public sealed class AudioClipService
{
    public Task SaveClipAsync(string profileId, ReadOnlyMemory<float> audio, int sampleRate,
        string? transcript, CancellationToken ct);
    public IReadOnlyList<AudioClipInfo> GetClips(string profileId);
    public string GetClipFilePath(string profileId, string clipId);
    public void DeleteClip(string profileId, string clipId);
    public Task MoveClipsAsync(string sourceProfileId, string targetProfileId, CancellationToken ct);
}
```

**Clip rotation**: `SaveClipAsync` enforces `MaxClipsPerProfile` (default 3). When saving a new clip to a profile that already has the maximum, the oldest clip (by `CapturedAt` timestamp) is deleted before the new one is written. This keeps only the 3 most recent voice samples per profile.

**Auto-profile cap awareness**: The caller (`UnknownSpeakerTracker`) checks the current provisional profile count against `MaxAutoProfiles` before calling `SaveClipAsync`. Profiles beyond the cap are tracked for interaction counting only — no audio clips, no stored embeddings.

#### API Endpoints

**New file**: `lucia.AgentHost/Apis/VoiceClipApi.cs`

```
GET    /api/speakers/{profileId}/clips           → List clips with metadata
GET    /api/speakers/{profileId}/clips/{clipId}  → Stream WAV file (audio/wav)
DELETE /api/speakers/{profileId}/clips/{clipId}  → Delete clip
POST   /api/speakers/{profileId}/clips/{clipId}/reassign
       Body: { "targetProfileId": "..." }        → Move clip to another profile
```

#### Frontend

In the Profiles tab, each profile card shows:
- Audio clip count badge
- Expandable clip list with play buttons (HTML5 `<audio>` elements)
- "Reassign to..." dropdown per clip (moves clip + recalculates embeddings)
- For provisional profiles: prominent audio playback to help identify

---

### D3: Profile Merge & Reassignment

**What**: Merge two speaker profiles (combine embeddings) or reassign individual audio clips between profiles.

#### Backend

**New endpoint** in `OnboardingApi.cs` (or new `VoiceProfileManagementApi.cs`):

```
POST /api/speakers/merge
Body: { "sourceProfileId": "...", "targetProfileId": "..." }
```

**Merge logic** (`VoiceOnboardingService` or new `ProfileMergeService`):
1. Load both profiles
2. Combine `Embeddings` arrays from source into target
3. Recalculate `AverageEmbedding` from combined set
4. Sum `InteractionCount`
5. Move audio clips from source to target directory
6. Delete source profile
7. Return merged profile

**Reassign clip logic** (called from clip reassign endpoint):
1. Move WAV + JSON files from source profile dir to target profile dir
2. Re-extract embedding from the WAV audio
3. Add embedding to target profile's `Embeddings` array
4. Remove corresponding embedding from source profile (by index/timestamp)
5. Recalculate `AverageEmbedding` for both profiles

#### Frontend

- **Merge button** on provisional profile cards → opens modal to select target profile
- **Drag-and-drop** or dropdown reassignment per audio clip
- Confirmation dialogs before destructive operations

---

### D4: Real-Time Session Monitoring Dashboard

**What**: SSE-powered live view of all connected Wyoming clients showing real-time transcripts, speaker identification, and voice detection per device.

#### Session Event Bus

**New file**: `lucia.Wyoming/Wyoming/SessionEventBus.cs`

```csharp
public sealed class SessionEventBus
{
    private readonly Channel<SessionEvent> _channel;

    public void Publish(SessionEvent evt);
    public IAsyncEnumerable<SessionEvent> SubscribeAsync(CancellationToken ct);
}
```

**Event types** (`SessionEvent` discriminated union or record hierarchy):

```csharp
public abstract record SessionEvent
{
    public required string SessionId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

public sealed record SessionConnectedEvent : SessionEvent
{
    public required string RemoteEndPoint { get; init; }
}

public sealed record SessionDisconnectedEvent : SessionEvent;

public sealed record SessionStateChangedEvent : SessionEvent
{
    public required WyomingSessionState State { get; init; }
}

public sealed record TranscriptEvent : SessionEvent
{
    public required string Text { get; init; }
    public required float Confidence { get; init; }
    public string? SpeakerId { get; init; }
    public string? SpeakerName { get; init; }
}

public sealed record SpeakerDetectedEvent : SessionEvent
{
    public required string ProfileId { get; init; }
    public required string ProfileName { get; init; }
    public required float Similarity { get; init; }
    public required bool IsProvisional { get; init; }
}

public sealed record AudioLevelEvent : SessionEvent
{
    public required float RmsLevel { get; init; }
    public required int ActiveVoiceCount { get; init; }
}
```

#### Integration with WyomingSession

Inject `SessionEventBus` into `WyomingSession`. Publish events at key points:
- **Session created** → `SessionConnectedEvent`
- **Session disposed** → `SessionDisconnectedEvent`
- **State change** → `SessionStateChangedEvent`
- **Transcript finalized** → `TranscriptEvent` (with speaker info if available)
- **Speaker identified** → `SpeakerDetectedEvent`
- **Audio chunk processed** → `AudioLevelEvent` (throttled, e.g., 4Hz)

#### SSE Endpoint

**New file**: `lucia.AgentHost/Apis/WyomingSessionApi.cs`

```
GET /api/wyoming/sessions                → List active sessions (REST)
GET /api/wyoming/sessions/live           → SSE stream of all session events
GET /api/wyoming/sessions/{id}           → Single session state (REST)
```

SSE format:
```
event: transcript
data: {"sessionId":"abc","text":"turn on the lights","confidence":0.92,"speakerName":"Zack"}

event: speaker_detected
data: {"sessionId":"abc","profileId":"xyz","profileName":"Zack","similarity":0.89}

event: session_connected
data: {"sessionId":"def","remoteEndPoint":"192.168.1.42:54321"}
```

#### Frontend — New "Monitor" Tab

**Location**: New tab on `VoicePlatformPage.tsx` (or a separate dedicated page).

**Layout**:

```
┌─────────────────────────────────────────────────────────┐
│  Connected Devices (3)                                   │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────────┐ │
│  │ 📡 Kitchen   │ │ 📡 Bedroom   │ │ 📡 Living Room  │ │
│  │ Listening    │ │ Transcribing │ │ Idle             │ │
│  │ 🎤 ████░░░░  │ │ 🎤 ██████░░  │ │ 🎤 ░░░░░░░░░░  │ │
│  │ Voices: 1    │ │ Voices: 2    │ │ Voices: 0       │ │
│  └──────────────┘ └──────────────┘ └──────────────────┘ │
├─────────────────────────────────────────────────────────┤
│  Live Transcript Feed                              [🔴] │
│                                                         │
│  12:34:05  [Kitchen]  Zack (0.91): "turn on the lights" │
│  12:34:12  [Bedroom]  Unknown (provisional): "hey lucia"│
│  12:34:18  [Kitchen]  Zack (0.88): "what's the weather" │
│  12:34:25  [Bedroom]  Unknown: "set timer five minutes" │
│                                                         │
│  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░ │
└─────────────────────────────────────────────────────────┘
```

**Components**:
- **Device cards**: One per connected Wyoming session, showing state, audio level meter, voice count
- **Transcript feed**: Scrolling log of transcripts with timestamps, device labels, speaker names, confidence scores
- **Filter controls**: Filter by device, speaker, confidence threshold
- **SSE connection**: `EventSource` to `/api/wyoming/sessions/live`

---

## Data Model Changes

### SpeakerProfile (additions)

```csharp
public sealed record SpeakerProfile
{
    // ... existing fields ...

    /// <summary>Audio clip IDs associated with this profile.</summary>
    public List<string> ClipIds { get; init; } = [];
}
```

### New: AudioClipInfo

**File**: `lucia.Wyoming/Diarization/AudioClipInfo.cs`

```csharp
public sealed record AudioClipInfo
{
    public required string Id { get; init; }
    public required string ProfileId { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
    public required TimeSpan Duration { get; init; }
    public required int SampleRate { get; init; }
    public string? Transcript { get; init; }
    public required long FileSizeBytes { get; init; }
}
```

### New: VoiceProfileOptions additions

```csharp
public bool AutoCreateProvisionalProfiles { get; set; } = true;
public string AudioClipBasePath { get; set; } = "./data/voice-clips";
public int MaxClipsPerProfile { get; set; } = 3;
public int MaxAutoProfiles { get; set; } = 10;
```

**Clip rotation**: When saving a new clip for a profile that already has `MaxClipsPerProfile` clips, delete the oldest clip before writing the new one (FIFO ring buffer behavior — always keep the most recent N clips).

**Auto-profile cap**: `UnknownSpeakerTracker` tracks how many auto-created provisional profiles exist. Once `MaxAutoProfiles` is reached, new unknown speakers are still labeled as `Unidentified Person N` and tracked for interaction counting, but:
- No audio clips are saved for profiles beyond the cap
- No new embeddings are stored (provisional profile is metadata-only)
- The counter `N` increments globally across all provisional profiles
- Profiles under the cap continue to receive clip updates normally

---

## File Inventory

### New Files

| File | Purpose |
|------|---------|
| `lucia.Wyoming/Audio/WavWriter.cs` | 16-bit PCM mono WAV file writer |
| `lucia.Wyoming/Diarization/AudioClipInfo.cs` | Audio clip metadata record |
| `lucia.Wyoming/Diarization/AudioClipService.cs` | Clip save/list/delete/move operations |
| `lucia.Wyoming/Diarization/ProfileMergeService.cs` | Profile merge + embedding recalculation |
| `lucia.Wyoming/Wyoming/SessionEventBus.cs` | Channel-based event fan-out for SSE |
| `lucia.Wyoming/Wyoming/SessionEvent.cs` | Base event record |
| `lucia.Wyoming/Wyoming/SessionConnectedEvent.cs` | Connection event |
| `lucia.Wyoming/Wyoming/SessionDisconnectedEvent.cs` | Disconnection event |
| `lucia.Wyoming/Wyoming/SessionStateChangedEvent.cs` | State transition event |
| `lucia.Wyoming/Wyoming/TranscriptEvent.cs` | Finalized transcript event |
| `lucia.Wyoming/Wyoming/SpeakerDetectedEvent.cs` | Speaker identification event |
| `lucia.Wyoming/Wyoming/AudioLevelEvent.cs` | Audio level + voice count event |
| `lucia.AgentHost/Apis/VoiceConfigApi.cs` | Configuration read/update endpoints |
| `lucia.AgentHost/Apis/VoiceClipApi.cs` | Audio clip retrieval/management endpoints |
| `lucia.AgentHost/Apis/WyomingSessionApi.cs` | Session list + SSE monitoring endpoints |

### Modified Files

| File | Changes |
|------|---------|
| `lucia.Wyoming/Diarization/VoiceProfileOptions.cs` | Add `AutoCreateProvisionalProfiles`, `AudioClipBasePath`, `MaxClipsPerProfile` |
| `lucia.Wyoming/Diarization/SpeakerProfile.cs` | Add `ClipIds` list |
| `lucia.Wyoming/Diarization/UnknownSpeakerTracker.cs` | Check `AutoCreateProvisionalProfiles` gate, call `AudioClipService.SaveClipAsync` |
| `lucia.Wyoming/Wyoming/WyomingSession.cs` | Inject `SessionEventBus` + `AudioClipService`, publish events, pass audio to clip service |
| `lucia.Wyoming/Wyoming/WyomingServer.cs` | Inject `SessionEventBus`, publish connect/disconnect events |
| `lucia.Wyoming/Extensions/ServiceCollectionExtensions.cs` | Register new services |
| `lucia.AgentHost/Program.cs` | Map new API endpoints |
| `lucia-dashboard/src/api.ts` | Add voice config, clip, session, and merge API functions |
| `lucia-dashboard/src/pages/VoicePlatformPage.tsx` | Add Settings panel, clip playback, merge UI, Monitor tab |

---

## Task Breakdown

### T1: Voice Configuration API & Settings UI
- [ ] Add `AutoCreateProvisionalProfiles` to `VoiceProfileOptions`
- [ ] Create `VoiceConfigApi.cs` with GET/PUT endpoints
- [ ] Update `UnknownSpeakerTracker` to check `AutoCreateProvisionalProfiles`
- [ ] Add config persistence to disk (`voiceconfig.json`)
- [ ] Create Settings panel in dashboard with all controls
- [ ] Frontend API client functions for voice config

### T2: Audio Clip Capture Infrastructure
- [ ] Create `WavWriter` utility (16-bit PCM mono)
- [ ] Create `AudioClipInfo` record
- [ ] Create `AudioClipService` (save, list, delete, move)
- [ ] Integrate clip capture into `WyomingSession.ProcessTranscriptAsync`
- [ ] Wire `AudioClipService` into `UnknownSpeakerTracker`
- [ ] Add clip options to `VoiceProfileOptions`
- [ ] Create `VoiceClipApi.cs` (list, stream WAV, delete, reassign)
- [ ] Register new services in DI

### T3: Profile Merge & Clip Reassignment
- [ ] Create `ProfileMergeService` (merge embeddings, move clips)
- [ ] Add merge endpoint to API
- [ ] Add reassign-clip endpoint to API
- [ ] Add `ClipIds` to `SpeakerProfile`
- [ ] Frontend: merge button on profile cards with target picker modal
- [ ] Frontend: per-clip reassign dropdown
- [ ] Frontend: audio playback (`<audio>` elements) per profile

### T4: Real-Time Session Monitoring
- [ ] Create `SessionEvent` record hierarchy (7 event types)
- [ ] Create `SessionEventBus` (Channel-based)
- [ ] Integrate event publishing into `WyomingSession` (transcript, speaker, state, audio level)
- [ ] Integrate event publishing into `WyomingServer` (connect, disconnect)
- [ ] Create `WyomingSessionApi.cs` (REST list + SSE stream)
- [ ] Frontend: Monitor tab with device cards
- [ ] Frontend: SSE `EventSource` connection
- [ ] Frontend: live transcript feed with speaker labels
- [ ] Frontend: audio level meters per device
- [ ] Frontend: voice count per device

### T5: Testing
- [ ] Unit tests: WavWriter (valid WAV header, sample conversion)
- [ ] Unit tests: AudioClipService (save, list, delete, move)
- [ ] Unit tests: ProfileMergeService (embedding combination, clip transfer)
- [ ] Unit tests: SessionEventBus (pub/sub, multiple subscribers)
- [ ] Unit tests: VoiceConfigApi (round-trip settings)
- [ ] Unit tests: UnknownSpeakerTracker auto-profile gate
- [ ] Integration test: SSE endpoint streams events
- [ ] Frontend: TypeScript type-check
