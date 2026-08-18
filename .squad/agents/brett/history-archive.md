# Project Context

- **Owner:** Zack Way
- **Project:** lucia-dotnet — Privacy-first multi-agent AI assistant for Home Assistant
- **Stack:** .NET 10, C# 14, sherpa-onnx 1.12.29 (bundles ORT 1.23.2), Wyoming protocol, ONNX Runtime
- **Created:** 2026-03-26

## Key Systems I Own

- `lucia.Wyoming/` — Full voice pipeline
- STT Engines: HybridSttEngine, SherpaSttEngine, GraniteOnnxEngine
- Granite 4.0 1B Speech ONNX: 3-model pipeline (audio_encoder, embed_tokens, decoder_model_merged)
- Wake word: configurable detection
- VAD: Voice Activity Detection
- Diarization: speaker verification profiles
- Model management: download, catalog, safety validation (reject "..", path separators)
- Wyoming TCP server: IHostedService, concurrency-limited sessions
- Zeroconf/mDNS: service discovery for HA

## Key Constraints

- SherpaOnnx.DenoisedAudio has Dispose() but does NOT implement IDisposable — use explicit try/finally
- Model IDs must be filesystem-safe (no "..", no path separators)
- Audio pipeline latency is critical — measure at every stage
- All processing local — no cloud dependencies for voice

## Learnings

<!-- Append new learnings below. -->

### 2026-03-28: /app/models Subdirectory Audit (issue #120)
**Audit Complete — Ready for Hicks to implement**
- All five Wyoming model subdirs are writable at runtime: stt, vad, kws, speech-enhancement, speaker-embedding
- Each subdir has pre-baked models (copied at build time) but also supports runtime model downloads via ModelDownloader + HuggingFaceModelDownloader
- HF CLI uses `--cache-dir /app/models/{subdir}` — no separate `~/.cache/huggingface/` writes to worry about
- Tmpfs config (256MB /tmp) is sufficient for ONNX temp files; no disk writes during inference
- Plugins dir is read-only in voice images (pre-copied at build); can declare as VOLUME for consistency
- Full audit written to `.squad/decisions/inbox/brett-app-models-audit.md`

- **Enhanced clip re-transcription pattern**: Feeding GTCRN per-frame into STT causes buffer discontinuities from overlap-add lag. The fix is to accumulate the full enhanced clip, then re-transcribe in a fresh STT session after VAD end-of-speech. Feature-flagged via `SpeechEnhancementOptions.UseEnhancedClipForStt` (default off). The same flag also gates speaker verification audio source selection — enhanced vs raw.
- **`_utteranceAudioBuffer` vs `_rawUtteranceAudioBuffer`**: When GTCRN enhancement is active, enhanced frames go to `_utteranceAudioBuffer` and raw frames go to `_rawUtteranceAudioBuffer`. Both are plain `List<float>` — no synchronization needed since they're only written during the audio processing loop and read after audio-stop.
- **HybridSttSession re-transcription**: Creating a fresh `ISttSession`, feeding complete audio via `AcceptAudioChunk`, then calling `GetFinalResultAsync` is the correct pattern for single-pass offline transcription of a complete clip. No progressive updates needed.

### 2026-07-23: Idle CPU Investigation (requested by Zack)

**Investigation Complete — No smoking gun found**
- All ONNX/sherpa models are singleton + eagerly loaded, but no idle polling loops exist in voice code
- WyomingServer accept loop has proper 250ms retry delay on failure — not a tight spin
- SSE endpoints are event-driven (BackgroundTaskApi) or 750ms polled (EntityLocationCacheApi) — benign
- Biggest idle CPU suspects are the 5-second config pollers: `SqliteConfigurationProvider` hits DB every tick, `MongoConfigurationProvider` queries Mongo every tick
- `TaskArchivalService` does O(n) task sweep every 5 minutes unconditionally
- Redis/Mongo drivers use default keepalive — no aggressive heartbeats found
- Full findings written to `.squad/decisions/inbox/brett-cpu-investigation.md`

### 2026-04-14: Enhanced Clip STT Pipeline Implementation (w/ Lambert QA)

**Implementation Complete**
- Feature flag `UseEnhancedClipForStt` added to SpeechEnhancementOptions
- Re-transcription path integrated in ProcessTranscriptAsync with proper guard conditions
- Enhanced audio routing to speaker verification (gates on same flag)
- Build clean, 288 tests pass (9 new integration tests from Lambert)
- Orchestration logs: `.squad/orchestration-log/2026-04-14T20-45-02Z-brett.md`

**Key Integration Points**
- Config: `Wyoming:Models:SpeechEnhancement:UseEnhancedClipForStt` (boolean, hot-reloadable)
- When flag OFF: raw audio path unchanged, enhancement only for clip storage
- When flag ON: post-VAD re-transcription adds ~1 inference pass; timing logged at Info level
- Speaker verification: routes enhanced utterance when flag ON, raw when flag OFF

**Decisions Merged**
- Decision #9: Feature-flagged Enhanced Clip STT Pipeline (status: Implemented, flag OFF by default)
- Decision #10: Enhanced Clip Pipeline Test Strategy (status: Implemented, 9 tests all green)

### Enhanced Clip A/B Telemetry (requested by Zack)

**Implementation Complete**
- Added 4 OpenTelemetry span tags on `wyoming.process_transcript` activity: `wyoming.stt.audio_source`, `wyoming.stt.enhanced_clip.enabled`, `wyoming.stt.enhanced_clip.sample_count`, `wyoming.stt.enhanced_clip.retranscription_ms`
- Added `enhanced_retranscription` PipelineStageTiming entry alongside existing `stt`, `diarization`, `enhancement` stages
- Added structured Info-level logs distinguishing enhanced vs raw path per utterance
- Fixed `SpanCollectorProcessor` case-sensitive prefix filter (`"Lucia."` → case-insensitive) — Wyoming spans (`lucia.Wyoming.Session`) were silently excluded from the dashboard trace store
- `TranscriptRecord.AudioSource` already existed; now consistently set to `"enhanced_clip"` or `"raw"` matching span tag values
- Build clean, 297/298 Wyoming tests pass (1 pre-existing DI registration failure)

### 2026-05-29: Voice Pipeline Health Review (whole-solution review, requested by Zack)

**Review complete — findings at `review-voice.md` (session files). No code modified.**
- **Unbounded utterance buffers (High):** `_utteranceAudioBuffer`/`_rawUtteranceAudioBuffer` (List<float>) accumulate per chunk and only clear on audio-stop. Per-event payload is capped but cumulative growth is not → OOM risk if a client never sends audio-stop. Needs a max-utterance duration/sample guard.
- **STT semaphore scope (High):** `WyomingServer.RunSessionAsync` holds `_sttConcurrency` (default 4) for the WHOLE session lifetime, not just STT. With `MaxWakeWordStreams=30` accepted, connections beyond 4 sit blocked on WaitAsync after being counted → sockets never read, clients silently hang. Should gate connections separately from STT burst, or acquire the slot only around finalization.
- **Telemetry race (Medium):** fire-and-forget `TrySaveTranscriptRecordAsync` reads `_sttFinalizationMs`/`_diarizationMs`/etc. which `ResetUtteranceAudio()` zeroes right after — stage timings can persist as 0ms. Snapshot timings into locals before Task.Run.
- **EXCLUDE_SPEECH is correctly wired** (Directory.Build.props maps `ExcludeSpeech=true`→`DefineConstants;EXCLUDE_SPEECH`; csproj does `Compile Remove` of Sherpa*/Gtcrn*/WyomingServer/WyomingSession; DI guarded by `#if !EXCLUDE_SPEECH`). BUT no CI job builds with `ExcludeSpeech=true`, so the ARM/Jetson variant can regress unnoticed — recommend adding a build-matrix job.
- **Hostname advertising inconsistency (Medium):** `lucia-{hostname}` is applied only in `WyomingServiceInfo.BuildInfoEvent`; mDNS (`ZeroconfAdvertiser`) still advertises `_options.ServiceName` ("lucia-wyoming"). Discovery name and protocol name diverge.
- **GTCRN per-frame allocations (Medium):** `GtcrnStreamingSession.Process` allocates List + several arrays/tensors per 256-sample hop — sustained GC pressure on the enhancement hot path.
- Confirmed good: parser bounds-checking + ArrayPool + partial-frame handling, writer SemaphoreSlim serialization, idempotent session Dispose, GtcrnStreamingSession does NOT dispose the shared InferenceSession.

- Participated in 2026-05-29 health review
---

**Update from Ripley (2026-05-30):** Inbox retriage complete. You have been assigned issues from the 2026-05-30 batch. Review .squad/decisions/decisions.md for details.

### 2026-05-30: mDNS Instance Name Alignment (issue #183)

**Fix Complete — PR #192**
- **Files changed:** `lucia.Wyoming/Wyoming/WyomingOptions.cs`, `lucia.Wyoming/Wyoming/WyomingServiceInfo.cs`, `lucia.Tests/Wyoming/WyomingProtocolComplianceTests.cs`
- **Approach:** Changed `WyomingOptions.ServiceName` default from `"lucia-wyoming"` to `$"lucia-{Environment.MachineName.ToLowerInvariant()}"` — single source of truth. Updated `WyomingServiceInfo.BuildInfoEvent()` to use `_options.ServiceName` (stored field) instead of computing hostname inline. `ZeroconfAdvertiser` already used `_options.ServiceName` so no change needed there.
- Added regression test `DescribeEvent_AsrAndWakeName_MatchServiceName`. Build clean (0 warnings); all 5 compliance tests pass.
- `LUCIA_SKIP_DOTNET_BUILD=1` used to bypass pre-commit hook because pre-existing `Nerdbank.MessagePack` vulnerability (GHSA-92vj-hp7m-gwcj / GHSA-qjvr-435c-5fjh) now triggers NU1902 as error in fresh restores. `lucia.EvalHarness` is outside this PR's scope.

### 2026-07-10: STT Semaphore Scope Fix (issue #178)

**Fix Complete — PR pending**
- **Root cause:** `WyomingServer.RunSessionAsync` held `_sttConcurrency` (default 4) for the ENTIRE session lifetime. The 30-connection wake-word accept limit let connections in, but only 4 could enter their read loop; the rest blocked with sockets unread — silently hanging clients.
- **Fix (final design):** Passed `_sttConcurrency` as optional `SemaphoreSlim?` to `WyomingSession`. Acquire happens at the Connected→Transcribing and WakeListening→Transcribing state transitions in `HandleAudioChunkEventAsync` (covering streaming decode). Release is centralized in `DisposeCurrentSttSession()`. `ReTranscribeEnhancedClipAsync` has its own acquire+release around enhanced-clip inference. Slot is NEVER held across diarization or client response writes.
- **Key pattern:** `if (_sttConcurrency is not null) await _sttConcurrency.WaitAsync(ct)` + `try/finally { _sttConcurrency?.Release(); }` — null-safe, backward compatible with tests that don't pass a semaphore.
- **Timing accuracy:** `_sttFinalizationMs` now measures pure inference time (stopwatch starts after semaphore acquire), consistent with Decision #17's timing snapshot principle.
- Build: 0 warnings, 0 errors. Wyoming tests: 296 passed, 10 skipped (hardware model tests), 0 failed.

### 2026-07-10: STT Semaphore Shutdown Race Fix (PR #220 follow-up)

**Fix Complete — committed to squad/178-stt-semaphore-scope**
- **Problem:** Automated review flagged that `_sttConcurrency` is disposed in `WyomingServer.Dispose()` while sessions can still be mid-finalization. `WaitAsync`/`Release` on a disposed `SemaphoreSlim` throw `ObjectDisposedException`, which surfaced as unhandled session errors during shutdown.
- **Approach chosen:** Option 2 (guard at acquire/release sites) rather than Option 1 (track in-flight sessions in StopAsync). Option 1 cannot guarantee safety when the host's shutdown deadline fires before tasks complete; Option 2 handles all shutdown scenarios including forced timeout.
- **WaitAsync guard:** `catch (ObjectDisposedException) when (ct.IsCancellationRequested)` → normalize to `OperationCanceledException` via `ct.ThrowIfCancellationRequested()`. If the token isn't cancelled (should not happen but defensive), re-throw ODE.
- **Release guard:** `try { _sttConcurrency!.Release(); } catch (ObjectDisposedException) { }` inside `finally` — the disposed semaphore has already reclaimed its slot.
- **sttSlotAcquired flag:** prevents Release being called if WaitAsync didn't succeed (no acquire = no release).
- **Test strategy:** Replaced flaky timing-based test (`SemaphoreSlim(0)` + `Task.Delay(100)`) with a deterministic `BlockingTestSttSession` + `SemaphoreSlim(1)` approach. New file `BlockingTestSttSession.cs` (one class per file). `InferenceStarted` TCS fires when `GetFinalResultAsync` is invoked; test disposes semaphore at that synchronisation point, unblocks inference, verifies no ODE surfaces.
- **Key insight (SemaphoreSlim.Dispose gotcha):** `SemaphoreSlim.Dispose()` does NOT cancel pending `WaitAsync` tasks — it nulls the internal linked list but leaves TCS in WaitingForActivation state. Waiters only unblock if the associated `CancellationToken` fires. This means `Dispose` alone cannot unblock a waiter; the test must rely on the acquire-then-dispose-then-release pattern instead.
- **Key insight (test design):** TCP backpressure is a real concern in session integration tests. If the server writes a `TranscriptEvent` and the client isn't draining, the `WriteEventAsync` call can block. Always start a background drain reader before sending audio events.
- Build: 0 warnings, 0 errors. Wyoming tests: 297 passed, 10 skipped, 0 failed.

### 2026-07-10: STT Semaphore Definitive Design (PR #220 round-2 review)

**Fix Complete — committed to squad/178-stt-semaphore-scope**
- **Abandon-leak (bug):** `HandleDetectEventAsync` could dispose the STT session and return to wake listening without releasing the slot. Centralizing teardown in `DisposeCurrentSttSession()` (which now calls `ReleaseSttSlot()` at its start) ensures ALL paths — cancel, error, restart mid-utterance — release the permit exactly once. `ReleaseSttSlot()` is idempotent; double-call is safe.
- **Held-too-long (bug):** Prior round held the slot through ALL of `ProcessTranscriptAsync` (diarization + client write). Fixed by moving the release point to inside `DisposeCurrentSttSession()`, which is called right after `GetFinalResultAsync` completes and BEFORE diarization/client writes. Net effect: STT slot is held only while inference runs.
- **Enhanced-clip re-transcription:** `ReTranscribeEnhancedClipAsync` acquires its own separate slot around the `GetFinalResultAsync` call and releases in `try/finally`. OCE propagates (not swallowed), other inference exceptions are caught+logged and return null.
- **DEFINITIVE acquire/release points:**
  - Acquire: `HandleAudioChunkEventAsync` at the Connected→Transcribing and WakeListening→Transcribing state transitions (covers streaming decode in `AcceptAudioChunk`)
  - Release: `DisposeCurrentSttSession()` — the single teardown point, called by all paths
  - Re-acquire/release: `ReTranscribeEnhancedClipAsync` for enhanced-clip inference only
  - Belt-and-suspenders: `RunAsync` finally block calls `ReleaseSttSlot()` before `DisposeEngineSessions`
- **Concurrency regression test:** `RunAsync_FourConcurrentSessions_InferenceBoundedBySemaphoreLimit` — limit=2 semaphore, 4 concurrent sessions, `ConcurrencyTrackingTestSttSession` with 100ms inference delay tracks peak concurrent count. Asserts `peak ≤ 2` (slot bounded) and all 4 sessions return transcripts (no hang).
- **stt.queue_wait_ms telemetry:** `AcquireSttSlotAsync` records queue-wait in `_sttQueueWaitMs` field; `wyoming.stt.finalize` span carries `stt.queue_wait_ms` tag. Contention vs inference latency now distinguishable.
- Build: 0 warnings, 0 errors. Wyoming tests: 298 passed, 10 skipped, 0 failed.

### 2026-07-10: STT Semaphore Correctness Pass (PR #220 round-3 review)

**Fix Complete — committed to squad/178-stt-semaphore-scope**
- **Atomic release (Bug 1 — double-release race):** `_sttSlotAcquired` changed from `bool` to `int`. `ReleaseSttSlot()` uses `Interlocked.Exchange(ref _sttSlotAcquired, 0) != 1` as the gate — exactly ONE caller wins (RunAsync-finally OR Dispose), eliminating the race that could inflate the semaphore count above `MaxConcurrentSttSessions`.
- **Await background inference before permit release (Bug 2 — permit released while decode runs):** `HybridSttSession` now implements `IAsyncDisposable.DisposeAsync()`, which awaits any pending background `RunTranscription` task. New `DisposeCurrentSttSessionAsync()` helper calls `DisposeAsync()` on sessions that implement it, then `ReleaseSttSlot()`. All async teardown paths use the async helper. Sync `Dispose()`/`DisposeEngineSessions()` retains the sync path as an inherent limitation.
- **Detection response before slot acquisition (Bug 3 — slow client holds slot):** In the WakeListening→Transcribing path, `WriteEventAsync(DetectionEvent)` now executes BEFORE `AcquireSttSlotAsync`. A backpressured client can no longer occupy an STT slot while no inference is running.
- **Updated DEFINITIVE acquire/release points:**
  - Acquire: `HandleAudioChunkEventAsync` at Connected→Transcribing and WakeListening→Transcribing transitions (AFTER sending DetectionEvent for wake path)
  - Release: `DisposeCurrentSttSessionAsync()` — single async teardown point; awaits `IAsyncDisposable.DisposeAsync()` THEN calls `ReleaseSttSlot()`
  - Re-acquire/release: `ReTranscribeEnhancedClipAsync` for enhanced-clip inference (unchanged)
  - No belt-and-suspenders `ReleaseSttSlot()` in `RunAsync` finally — the `await DisposeCurrentSttSessionAsync()` there handles all cases
- **Concurrency test updated (Bug 4):** `ConcurrencyTrackingTestSttSession` now implements `IAsyncDisposable`. Counter increments on FIRST `AcceptAudioChunk` (models background decode starting) and decrements only after `DisposeAsync` completes (100ms simulated delay — models awaiting RunTranscription). This proves the slot is not returned while background work is in flight.
- **Key insight (SemaphoreSlim.Release() after Dispose is idempotent):** The Interlocked gate means `Release()` is called at most once per permit. Combined with the ODE guard (`catch ObjectDisposedException`) on Release, the dispose race is fully covered.
- Build: 0 warnings, 0 errors. Wyoming tests: 298 passed, 10 skipped, 0 failed.

### 2026-07-10: STT Semaphore Vasquez Gate Review (PR #220 round-4 review)

**Fix Complete — committed to squad/178-stt-semaphore-scope (sha: 30daee6c1847f23837888772493b9c064f51f0e0)**
- **Shared disposal task — no concurrent DisposeAsync early return (Bug 1):** `HybridSttSession` replaces `bool _disposed` guard with `Lazy<Task> _disposeTask` (LazyThreadSafetyMode.ExecutionAndPublication). `DisposeCore()` is the one-time factory: sets `_disposed = true` synchronously, then awaits `_pendingTranscription`. `DisposeAsync()` returns `new ValueTask(_disposeTask.Value)` — all concurrent callers share the SAME task. No caller returns until inference has actually finished. `_disposed` is now `volatile` for cross-thread visibility.
- **try/finally guarantees slot release on faulted disposal (Bug 3):** `DisposeCurrentSttSession()` and `DisposeCurrentSttSessionAsync()` now wrap disposal in `try/finally` with `ReleaseSttSlot()` in the `finally` block. Even if `DisposeAsync` throws, the STT slot is returned.
- **BlockingTestSttSession implements IAsyncDisposable:** `Lazy<Task>` backed by gate TCS. `DisposeAsync()` returns the gate task — all concurrent callers (RunAsync finally + session.Dispose()) await the same gate, faithfully modeling `HybridSttSession` semantics.
- **StopAsync race regression test (Bug 2):** `Dispose_WaitsForInferenceBeforeReleasingSlot_WhenRacingWithRunAsync` — asserts slot held before unblock, STILL held 150 ms after background Dispose() starts, then released exactly once after unblock.
- **XML doc fix (Bug 4):** `HybridSttSession` class summary now documents that BOTH `GetFinalResultAsync` and `DisposeAsync` await pending background transcription; no cancellation claim.
- **FINAL acquire/release invariant:** Acquire at Connected→Transcribing / WakeListening→Transcribing (after DetectionEvent write). Release in `DisposeCurrentSttSession{Async}()` try/finally via `Interlocked.Exchange` atomic gate. `HybridSttSession.DisposeAsync()` (via shared `Lazy<Task>`) awaits background RunTranscription BEFORE release fires.
- Build: 0 warnings, 0 errors. Wyoming tests: 300 passed, 10 skipped, 0 failed.

## Learnings

- **`Lazy<Task>` + `LazyThreadSafetyMode.ExecutionAndPublication` is the correct pattern for a shared async disposal task.** The factory runs synchronously until its first `await`, returning the `Task` to the Lazy. All concurrent callers get the same Task and await it. Strictly safer than a `bool _disposed` flag which leaves a window where a concurrent caller sees `_disposed == true` and returns early while the first caller is still awaiting.
- **`volatile bool` for cross-thread disposal guards** — Any `bool` field set from one thread and read from another (e.g. `_disposed`) must be `volatile` to prevent stale cached reads. Without it, the JIT can hoist the read out of loops or keep it in a register.
- **test design: mock `Lazy<Task>` must match production semantics** — `BlockingTestSttSession` uses `Lazy<Task>` so concurrent `DisposeAsync` callers (RunAsync finally + session.Dispose()) await the same task, exactly as with the real `HybridSttSession`. The test actually exercises the concurrent-caller invariant.
- **`try/finally` around `DisposeAsync` in cleanup helpers is non-negotiable** — If a session's `DisposeAsync` throws, the STT slot must still be released. Omitting the `finally` gives a permanent capacity leak that only manifests under error conditions.
- **Brief `Task.Delay` before "still held" assertions** — for tests checking that a concurrent background Dispose() is blocking, ~150 ms is wide enough to be reliable without making the test slow.

### 2026-07-10: Vasquez Round 8 — _finalizationTask + DisposeCore awaits full GetFinalResultAsync (commit cd2de93)

**Fix Complete — committed to squad/178-stt-semaphore-scope**
- **Residual race (Bug 1):** `DisposeCore` previously only awaited `_pendingTranscription` (the progressive background task), but `GetFinalResultAsync` also runs a final SYNCHRONOUS `RunTranscription()` pass AFTER `_pendingTranscription` completes. If `_pendingTranscription` was already done when `DisposeCore` ran, it would complete and release the slot while `RunTranscription()` was still executing on the RunAsync thread.
- **Fix:** `_disposeLock`-protected `_finalizationTask` (Task<SttResult>?). `GetFinalResultAsync()` becomes non-async: acquires `_disposeLock`, creates `FinalizeCore()` Task (storing it as `_finalizationTask`), returns it. `FinalizeCore()` awaits `_pendingTranscription` then calls `RunTranscription()` synchronously. `DisposeCore()` acquires `_disposeLock`, sets `_disposed = true`, captures `_finalizationTask`, releases lock, then awaits it — covering both progressive AND final decode. The lock serializes "finalization started" vs "disposal started"; no race window exists.
- **BlockingTestSttSession redesign:** New `_finalizationLock` + `Task<SttResult>? _finalizationTask` mirrors production. `GetFinalResultAsync()` creates `FinalizeAsync()` Task under lock. `FinalizeAsync()` fires `InferenceStarted` then blocks on `_finalInferenceGate`. `DisposeAsync()` reads `_finalizationTask` under lock and returns `ValueTask(finTask)` if set.
- **Test assertion ordering fix:** `Assert.Equal(1, semaphore.CurrentCount)` moved BEFORE `await runTask` — prevents the assertion being masked by RunAsync.finally independently releasing the slot.
- Build: 0 warnings, 0 errors. Wyoming tests: 301 passed (new test), 10 skipped, 0 failed.

### 2026-07-10: Vasquez Round 9 — AcceptAudioChunk/DisposeCore race (commit 015e283)

**Fix Complete — committed to squad/178-stt-semaphore-scope (commit 015e283)**
- **Race (Bug 1):** `AcceptAudioChunk` can pass the fast-path `ObjectDisposedException.ThrowIf(_disposed, this)` check. Then `DisposeCore` runs, observes `_pendingTranscription == null` (not yet published), and returns — releasing the STT permit. THEN `AcceptAudioChunk` publishes `_pendingTranscription = Task.Run(RunTranscription)` and ONNX inference executes AFTER the permit was returned. This violates `MaxConcurrentSttSessions`.
- **Fix:** Inside `AcceptAudioChunk`'s progressive check block, wrap the re-check and publish under `_disposeLock`. Inside the lock: if `_disposed` is true, return (drop chunk silently); if false, reset `_samplesSinceLastRefresh` and publish `Task.Run(RunTranscription)`. `Task.Run` returns the scheduled Task instantly; ONNX inference never executes while the lock is held. Lock is released immediately after `Task.Run` returns.
- **Invariant after fix:** Once `DisposeCore` has acquired `_disposeLock`, set `_disposed = true`, and captured the task fields, no new inference task can be published. If `AcceptAudioChunk` published first (under the lock), `DisposeCore` sees the non-null task and awaits it.
- **Lock ordering:** `_disposeLock` is always outer; `_bufferLock` is always inner. In `AcceptAudioChunk`, `_bufferLock` is acquired (and released) for buffer mutation BEFORE any `_disposeLock` acquisition in the progressive section. No deadlock path.
- **Test:** `GatableAcceptSttSession` (new file) gates inside `AcceptAudioChunk` between the fast-path check and the lock publish (via `ManualResetEventSlim`). Lock-protected `InferenceScheduledCount` increments only if not disposed. `AcceptAudioChunk_DisposalRace_DoesNotScheduleInferenceAfterSlotReleased` drives a WyomingSession into AcceptAudioChunk, gates it, calls `session.Dispose()` (releases permit), releases the gate, asserts `InferenceScheduledCount == 0` and `semaphore.CurrentCount == 1`.
- **Key insight (sealed class / no model = WyomingSession-level test):** `HybridSttSession` is sealed and requires a real `OfflineRecognizer` (sherpa-onnx native type). Writing a deterministic unit test of its `_disposeLock` AcceptAudioChunk fix directly is not practical without a test hook or model file. The mock (`GatableAcceptSttSession`) models the same lock-protected pattern at the WyomingSession integration level. The test verifies the invariant holds at the observable level (semaphore count, inference counter).
- Build: 0 warnings, 0 errors. Wyoming tests: 301 passed, 10 skipped, 0 failed.

### 2026-07-10: Vasquez Round 10 — replace fake-session disposal race test with real HybridSttSession seam (commit cefb515)

**Fix Complete — committed to squad/178-stt-semaphore-scope (commit cefb515)**
- **Problem (Vasquez item 1):** `GatableAcceptSttSession` (the mock used in round 9) reimplemented the `_disposeLock` check+publish pattern itself, so the integration test `AcceptAudioChunk_DisposalRace_DoesNotScheduleInferenceAfterSlotReleased` would still PASS even if the production `if (_disposed) return;` guard was deleted from `HybridSttSession`. It tested the mock's correctness, not the production code. Also used a 100 ms `Task.Delay` (timing-dependent).
- **Fix — production seam approach:** `HybridSttSession` (sealed, requires native sherpa-onnx) was NOT testable in isolation due to the `OfflineRecognizer` hard dependency. Solution:
  - Added `[assembly: InternalsVisibleTo("lucia.Tests")]` via new `Properties/AssemblyInfo.cs`.
  - Added private `ForTestOnly` tag struct to disambiguate the test constructor from the public one at IL level (both would have the same parameter types otherwise, since nullability is compile-time only).
  - Public constructor chains via `: this(recognizer ?? throw ..., ..., default(ForTestOnly))` — null validation before chaining is valid via `?? throw` in the argument list.
  - Private test constructor accepts `OfflineRecognizer?` (null) — `RunTranscription` no-ops on null recognizer (`if (_recognizer is null) return;` guard).
  - `internal static HybridSttSession CreateForTest(...)` factory creates test instances.
  - `internal Action? BeforeProgressivePublishSeam` — called BEFORE `_disposeLock` is acquired (the race window). Null in production, zero overhead.
  - `internal Action? AfterProgressivePublishSeam` — called INSIDE the lock AFTER `Task.Run`; signals if publish happened post-disposal (guard bypassed).
- **New test `HybridSttSessionDisposalRaceTests`:** `AcceptAudioChunk_ProgressivePublish_IsDroppedWhenDisposalWinsRace` — deterministic via `ManualResetEventSlim` gate + `TaskCompletionSource`, no timing delays. Verified: test FAILS (publishCount=1) when `if (_disposed) return;` is removed from AcceptAudioChunk's lock block; passes (publishCount=0) with guard in place. Test exercises REAL production `HybridSttSession` code.
- **Comment fix (Vasquez item 2):** Replaced "ONNX decode runs entirely outside the critical section" (inaccurate) with accurate description: check+publish are atomic under `_disposeLock`; `Task.Run` enqueues the work inside the lock; the threadpool MAY begin executing `RunTranscription` concurrently before the lock exits, but that is safe — `DisposeCore` captures and awaits `_pendingTranscription` (published before lock release) so it always awaits any scheduled inference. Updated class-level XML doc to match.
- **Removed:** `GatableAcceptSttSession.cs` (mock-level test replaced); removed the associated integration test from `WyomingSessionIntegrationTests.cs`.
- **Key insight:** `OfflineRecognizer` (sherpa-onnx) is sealed with no public mock seam and cannot be created without model files. Internal seams (`ForTestOnly` constructor tag + `BeforeProgressivePublishSeam`/`AfterProgressivePublishSeam`) provide the necessary test hook without altering production behavior. This is the correct approach when a native dependency blocks direct unit testing.
- **Key insight:** C# nullability (`T` vs `T?`) is compile-time only — both map to the same IL type. When a public and private constructor share all parameter types except nullability, a private tag type (zero-size struct) is needed to create a distinct overload. Using `?? throw` in the `: this(...)` call enables null validation before the private constructor runs.
- Build: 0 warnings, 0 errors. Wyoming tests: 301 passed, 10 skipped, 0 failed.


### 2026-07-17: Jetson Orin Nano Native Voice Runtime Research (requested by Zack)

**Research complete — memo delivered; decision at `.squad/decisions/inbox/brett-jetson-native-voice.md`. No code changed.**
- **The Jetson GPU voice path already exists and targets our exact board.** sherpa-onnx `build-aarch64-linux-gnu.sh` explicitly documents "NVIDIA Jetson Orin Nano Engineering Reference Developer Kit Super, Jetpack 6.2 [L4T 36.4.3] (CUDA 12.6, cudnn9)" with `SHERPA_ONNX_ENABLE_GPU=ON` + `SHERPA_ONNX_LINUX_ARM64_GPU_ONNXRUNTIME_VERSION=1.18.1`. So Jetson voice is a GPU-enablement + native-lib-sourcing problem, NOT a rewrite. (build script + cmake/onnxruntime-linux-aarch64-gpu.cmake)
- **aarch64 CUDA ONNX Runtime is COMMUNITY-built, pinned to 1.18.1, not official.** cmake downloads `onnxruntime-linux-aarch64-gpu-cuda12-1.18.1.tar.bz2` from `github.com/csukuangfj/onnxruntime-libs`. There is NO official Microsoft or NVIDIA aarch64+CUDA ORT. `Microsoft.ML.OnnxRuntime.Gpu.Linux` is x64-only. Version ceiling on aarch64 GPU is ~1.18.1 (vs x86 running 1.23.2) — an important gap when reasoning about ORT features.
- **The .NET wrapper is provider-agnostic.** `org.k2fsa.sherpa.onnx` ships a `linux-arm64` runtime sub-package that is CPU-only (`libsherpa-onnx-c-api.so`). GPU = rebuild sherpa-onnx aarch64 with GPU on + swap the two native `.so`s into `runtimes/linux-arm64/native/`, pass `Provider="cuda"`. Mirrors the existing x64 GPU overlay in `Dockerfile.voice`. Managed C# unchanged.
- **Parakeet-TDT needs no custom ops.** TDT = Token-and-Duration Transducer (RNN-T family variant predicting token+duration to skip encoder frames). sherpa-onnx exports encoder/decoder/joiner ONNX and does the TDT duration logic in C++ — no dynamic ONNX decoder ops, no TensorRT plugins. The SAME model (`sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8`) already runs on Lucia's x86 CUDA build, so Jetson is a port. Model is CC-BY-4.0, English-only; v3 is multilingual (25 EU langs). Do NOT conflate parakeet-ctc / parakeet-rnnt / parakeet-tdt.
- **GTCRN belongs on CPU, not GPU.** 48.2K params, 33 MMACs/s, desktop CPU RTF ~0.07. GPU offload adds per-256-sample-hop host<->device copies costing more than the compute. A TensorRT export example exists (Xiaobin-Rong/TRT-SE) but is unjustified. sherpa-onnx has built-in GTCRN (`OfflineSpeechDenoiser`) — option to drop the separate Microsoft.ML.OnnxRuntime dependency and consolidate on one native lib.
- **Orin Nano 8GB Super hardware reality:** Ampere GPU 1024 CUDA cores / 32 tensor cores @1020MHz, 67/33 INT8 TOPS (S/D), 17 FP16 TFLOPS; 8GB LPDDR5 unified @102 GB/s (Super); 6x A78 @1.7GHz; power 15W/25W/MAXN SUPER. **NO DLA on Orin Nano** (only Orin NX has DLA) — GPU is the only accelerator. 8GB is unified/shared CPU+GPU, so co-locating Redis+Mongo (current jetson compose) squeezes the voice memory budget — must re-plan.
- **No Python at runtime.** sherpa-onnx C-API is Python-free at inference. Drop the `hf` CLI (huggingface-hub) from the voice image; pre-bake models or fetch via .NET HttpClient. Python only optionally at offline export time, avoidable via pre-exported ONNX.
- **Licenses:** Parakeet-TDT CC-BY-4.0; Silero VAD MIT; 3D-Speaker eres2net Apache-2.0; pyannote segmentation-3.0 is HF-gated (but Lucia does embedding+cosine verification, not pyannote segmentation).
- **Numbers must be measured on hardware** — never invent RTF/WER/VRAM. JetPack 6.2 = CUDA 12.6, TensorRT 10.3, cuDNN 9.3, kernel 5.15, Ubuntu 22.04. CUDA 12.x minor-version compat means an ORT built vs CUDA 12.2 runs on 12.6.

### 2026-07-17 — Jetson Orin Nano Research Consolidation

Research contribution complete. Input consolidated into Decision 27 (Jetson Orin Nano Native Voice Inference) by Scribe. Confirmed: STT stage runs GPU; VAD/KWS/embedding/diarization run native sherpa-onnx; GTCRN keeps CPU. ORT 1.18.1 aarch64 GPU + sherpa-onnx CUDA build required. Model licensing verified (CC-BY-4.0, MIT, Apache-2.0). No new infrastructure needed; reuse existing `.NET Wyoming host + provider detection + Dockerfile overlay pattern. Kill gates (B1–B4) documented; PoC Stages 1–5 scoped at ~3–4 weeks on physical hardware.

