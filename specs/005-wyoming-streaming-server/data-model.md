# Data Model: Wyoming Streaming Voice Server

**Spec**: 005-wyoming-streaming-server
**Date**: 2026-03-13

## Overview

This document defines the data models used across the Wyoming streaming voice server. Models are organized by domain: Wyoming protocol, audio processing, speech engines, diarization, command routing, and TTS.

---

## 1. Wyoming Protocol Models

### WyomingEvent (Base)
| Field | Type | Description |
|-------|------|-------------|
| Type | string | Event type identifier (e.g., "audio-start", "transcript") |
| Data | Dictionary<string, object>? | Event-specific metadata |
| Payload | byte[]? | Optional binary payload (typically PCM audio) |

### AudioStartEvent
| Field | Type | Description |
|-------|------|-------------|
| Rate | int | Sample rate in Hz (e.g., 16000) |
| Width | int | Bytes per sample (e.g., 2 for 16-bit) |
| Channels | int | Number of audio channels (1=mono, 2=stereo) |
| Timestamp | long? | Optional start timestamp |

### AudioChunkEvent
| Field | Type | Description |
|-------|------|-------------|
| Rate | int | Sample rate in Hz |
| Width | int | Bytes per sample |
| Channels | int | Number of channels |
| Payload | byte[] | Raw PCM audio data |

### AudioStopEvent
| Field | Type | Description |
|-------|------|-------------|
| Timestamp | long? | Optional end timestamp |

### TranscribeEvent
| Field | Type | Description |
|-------|------|-------------|
| Name | string? | Preferred STT model name |
| Language | string? | Language hint (ISO 639-1) |

### TranscriptEvent
| Field | Type | Description |
|-------|------|-------------|
| Text | string | Recognized text |
| Confidence | float | Recognition confidence (0.0 - 1.0) |

### DetectEvent
| Field | Type | Description |
|-------|------|-------------|
| Names | string[]? | Wake words to detect (null = all configured) |

### DetectionEvent
| Field | Type | Description |
|-------|------|-------------|
| Name | string | Detected wake word name |
| Timestamp | long? | Detection timestamp |

### SynthesizeEvent
| Field | Type | Description |
|-------|------|-------------|
| Text | string | Text to synthesize |
| Voice | string? | Voice preset name |
| Language | string? | Target language |

### DescribeEvent
(No additional fields)

### InfoEvent
| Field | Type | Description |
|-------|------|-------------|
| Asr | AsrInfo[] | Available STT models |
| Tts | TtsInfo[] | Available TTS engines/voices |
| Wake | WakeInfo[] | Available wake word models |
| Version | string | Server version |

### ErrorEvent
| Field | Type | Description |
|-------|------|-------------|
| Text | string | Error message |
| Code | string? | Error code |

---

## 2. Audio Processing Models

### AudioFormat
| Field | Type | Description |
|-------|------|-------------|
| SampleRate | int | Samples per second (Hz) |
| BitsPerSample | int | Bits per sample (8, 16, 24, 32) |
| Channels | int | Channel count |
| BytesPerSecond | int | Computed: SampleRate * Channels * (BitsPerSample / 8) |
| BytesPerFrame | int | Computed: Channels * (BitsPerSample / 8) |

### VadSegment
| Field | Type | Description |
|-------|------|-------------|
| Samples | float[] | Audio samples for this speech segment |
| StartTime | TimeSpan | Segment start offset from stream beginning |
| EndTime | TimeSpan | Segment end offset |
| SampleRate | int | Sample rate of the audio |

---

## 3. Speech-to-Text Models

### SttResult
| Field | Type | Description |
|-------|------|-------------|
| Text | string | Recognized text |
| Confidence | float | Overall confidence (0.0 - 1.0) |
| IsFinal | bool | True if this is the final result (not partial) |
| Duration | TimeSpan | Duration of processed audio |
| Tokens | SttToken[]? | Optional per-word timing |

### SttToken
| Field | Type | Description |
|-------|------|-------------|
| Text | string | Token text |
| StartTime | TimeSpan | Token start time |
| EndTime | TimeSpan | Token end time |
| Confidence | float | Per-token confidence |

---

## 4. Wake Word Models

### WakeWordResult
| Field | Type | Description |
|-------|------|-------------|
| Keyword | string | Detected keyword text |
| Confidence | float | Detection confidence |
| Timestamp | DateTimeOffset | When detection occurred |

---

## 5. Diarization Models

### SpeakerEmbedding
| Field | Type | Description |
|-------|------|-------------|
| Vector | float[] | Embedding vector (256-512 dimensions) |
| Duration | TimeSpan | Duration of source audio |
| ExtractedAt | DateTimeOffset | When embedding was extracted |

### SpeakerIdentification
| Field | Type | Description |
|-------|------|-------------|
| ProfileId | string | Matched speaker profile ID |
| Name | string | Speaker name |
| Similarity | float | Cosine similarity score (0.0 - 1.0) |
| IsAuthorized | bool | Whether speaker has command authorization |

### SpeakerProfile (Persisted — MongoDB)
| Field | Type | Description |
|-------|------|-------------|
| Id | string | Unique profile identifier |
| Name | string | Human-readable speaker name |
| Embeddings | float[][] | Stored embeddings (multiple for averaging) |
| AverageEmbedding | float[] | Computed average for comparison |
| IsAuthorized | bool | Whether this speaker can execute commands |
| AllowedSkills | string[]? | Optional skill allowlist (null = all) |
| EnrolledAt | DateTimeOffset | Profile creation timestamp |
| UpdatedAt | DateTimeOffset | Last profile update |

---

## 6. Command Routing Models

### CommandPattern
| Field | Type | Description |
|-------|------|-------------|
| Id | string | Pattern identifier (e.g., "light-on-off") |
| SkillId | string | Target skill type name |
| Action | string | Skill action to invoke |
| Templates | string[] | Template patterns with {placeholders} |
| MinConfidence | float | Minimum confidence for fast-path (0.0 - 1.0) |
| Priority | int | Match priority (higher = preferred) |

### CommandRouteResult
| Field | Type | Description |
|-------|------|-------------|
| IsMatch | bool | Whether a pattern matched above threshold |
| Confidence | float | Combined confidence score |
| MatchedPattern | CommandPattern? | The matched pattern (if any) |
| CapturedValues | Dictionary<string, string>? | Extracted template values |
| ResolvedEntityId | string? | HA entity ID from entity resolution |
| ResolvedAreaId | string? | HA area ID if area-based command |
| SpeakerId | string? | Identified speaker (from diarization) |
| MatchDuration | TimeSpan | Time taken for matching |
| AlternativeMatches | CommandRouteResult[]? | Other possible matches below threshold |

### SkillDispatchResult
| Field | Type | Description |
|-------|------|-------------|
| Success | bool | Whether skill execution succeeded |
| ResponseText | string | Response text for TTS |
| SkillId | string | Which skill was invoked |
| ExecutionDuration | TimeSpan | How long execution took |
| Error | string? | Error message if failed |

---

## 7. TTS Models

### TtsSynthesisRequest
| Field | Type | Description |
|-------|------|-------------|
| Text | string | Text to synthesize |
| VoiceName | string? | Voice preset name (engine-specific) |
| Language | string | Target language (default: "english") |
| Speed | float | Playback speed multiplier (default: 1.0) |
| ReferenceAudioPath | string? | Path to reference audio for voice cloning |
| StyleInstruction | string? | Optional style hint (e.g., "speak softly") |

### TtsAudioChunk
| Field | Type | Description |
|-------|------|-------------|
| PcmData | ReadOnlyMemory<byte> | Raw PCM audio data |
| SampleRate | int | Sample rate in Hz |
| BitsPerSample | int | Bits per sample |
| Channels | int | Number of channels |

### TtsResult
| Field | Type | Description |
|-------|------|-------------|
| Audio | byte[] | Complete PCM audio buffer |
| SampleRate | int | Output sample rate |
| BitsPerSample | int | Output bit depth |
| Channels | int | Output channel count |
| Duration | TimeSpan | Audio duration |
| EngineId | string | Which TTS engine was used |

### TtsVoice
| Field | Type | Description |
|-------|------|-------------|
| Name | string | Voice preset identifier |
| Language | string | Primary language |
| Description | string? | Human-readable description |
| EngineId | string | Which engine owns this voice |

### CachedTtsResponse
| Field | Type | Description |
|-------|------|-------------|
| PcmData | byte[] | Cached PCM audio |
| SampleRate | int | Audio sample rate |
| OriginalText | string | Text that was synthesized |
| VoiceName | string | Voice used for synthesis |
| CachedAt | DateTimeOffset | When cached |
| HitCount | int | Number of cache hits |

---

## 8. Pipeline Session Models

### PipelineSession
| Field | Type | Description |
|-------|------|-------------|
| WyomingConnectionId | string | Unique TCP connection identifier |
| LuciaContextId | string? | Lucia conversation context ID |
| LuciaSessionId | string? | Lucia session ID |
| SatelliteId | string? | Identified satellite name |
| SpeakerId | string? | Identified speaker (from diarization) |
| CurrentStage | PipelineStage | Current pipeline stage |
| ContinueConversation | bool | Whether follow-up listening is active |
| AudioFormat | AudioFormat? | Negotiated audio format |
| StartedAt | DateTimeOffset | Session start time |
| LastActivity | DateTimeOffset | Last event received |

### PipelineStage (Enum)
| Value | Description |
|-------|-------------|
| WakeListening | Continuously monitoring for wake word |
| Transcribing | Actively transcribing speech |
| Routing | Diarization + command matching in progress |
| Executing | Skill dispatch or LLM call in progress |
| Synthesizing | TTS synthesis in progress |
| WaitingForFollowUp | Waiting for follow-up speech (continue_conversation) |
| Idle | Connection open but no active pipeline |

---

## 9. Configuration Models

### WyomingServerOptions
| Field | Type | Default | Description |
|-------|------|---------|-------------|
| Host | string | "0.0.0.0" | TCP listen address |
| Port | int | 10400 | Wyoming TCP port |
| MaxConnections | int | 8 | Maximum concurrent satellite connections |
| ServiceName | string | "lucia-wyoming" | Zeroconf service name |
| FollowUpTimeout | TimeSpan | 10s | Timeout for continue_conversation listening |

### SttOptions
| Field | Type | Default | Description |
|-------|------|---------|-------------|
| Engine | string | "sherpa-zipformer" | STT engine identifier |
| ModelPath | string | (required) | Path to model files |
| NumThreads | int | 4 | ONNX inference threads |
| SampleRate | int | 16000 | Expected input sample rate |

### WakeWordOptions
| Field | Type | Default | Description |
|-------|------|---------|-------------|
| Engine | string | "sherpa-kws" | Wake word engine |
| ModelPath | string | (required) | Path to KWS model |
| Keywords | string[] | ["hey lucia"] | Active wake words |
| Sensitivity | float | 0.5 | Detection sensitivity (0.0-1.0) |

### TtsOptions
| Field | Type | Default | Description |
|-------|------|---------|-------------|
| PrimaryEngine | string | "qwen3-tts" | Default TTS engine |
| FallbackEngine | string? | "chatterbox-turbo" | Fallback TTS engine |
| DefaultVoice | string | "ryan" | Default voice preset |
| DefaultLanguage | string | "english" | Default language |
| CacheEnabled | bool | true | Enable TTS response caching |
| CacheMaxEntries | int | 500 | Maximum cached phrases |

### DiarizationOptions
| Field | Type | Default | Description |
|-------|------|---------|-------------|
| Enabled | bool | true | Enable speaker diarization |
| SegmentationModel | string | (required) | Path to segmentation model |
| EmbeddingModel | string | (required) | Path to embedding model |
| SpeakerThreshold | float | 0.7 | Cosine similarity threshold for identification |

### CommandRoutingOptions
| Field | Type | Default | Description |
|-------|------|---------|-------------|
| Enabled | bool | true | Enable fast-path command routing |
| ConfidenceThreshold | float | 0.8 | Minimum confidence for fast-path |
| FallbackToLlm | bool | true | Route unmatched commands to LLM |

---

## 10. Model Catalog & Management Models

### AsrModelDefinition
| Field | Type | Description |
|-------|------|-------------|
| Id | string | Model identifier matching archive name (e.g., "sherpa-onnx-streaming-zipformer-en-2023-06-26") |
| Name | string | Human-readable display name |
| Architecture | ModelArchitecture | Model architecture type |
| IsStreaming | bool | Whether model supports real-time streaming inference |
| Languages | string[] | Supported language codes (ISO 639-1) |
| SizeBytes | long | Approximate download size in bytes |
| Description | string | Human-readable description with use-case guidance |
| DownloadUrl | string | Full URL to .tar.bz2 archive on GitHub releases |
| IsDefault | bool | Whether this is the out-of-box default model |
| MinMemoryMb | int | Minimum memory required to load this model |
| QuantizationVariants | string[] | Available quantized versions (e.g., "int8", "fp16") |
| HasMobileVariant | bool | Whether a smaller mobile variant exists |

### ModelArchitecture (Enum)
| Value | Description |
|-------|-------------|
| ZipformerTransducer | Zipformer with transducer decoder (streaming + offline) |
| ZipformerCtc | Zipformer with CTC decoder |
| Paraformer | Alibaba Paraformer architecture |
| Conformer | Conformer transducer |
| NemoFastConformer | NVIDIA NeMo FastConformer (CTC or transducer) |
| NemoParakeet | NVIDIA NeMo Parakeet TDT |
| NemoNemotron | NVIDIA Nemotron speech model |
| NemoCanary | NVIDIA NeMo Canary multilingual |
| Whisper | OpenAI Whisper (offline only) |
| SenseVoice | FunASR SenseVoice (offline only) |
| Lstm | LSTM transducer |
| Telespeech | TeleSpeech CTC |
| Unknown | Unrecognized architecture |

### ModelFilter
| Field | Type | Description |
|-------|------|-------------|
| StreamingOnly | bool? | Only return streaming-capable models |
| Language | string? | Filter by supported language code |
| Architecture | ModelArchitecture? | Filter by architecture type |
| MaxSizeMb | int? | Maximum model size in megabytes |
| InstalledOnly | bool? | Only return models present on disk |

### InstalledModel
| Field | Type | Description |
|-------|------|-------------|
| Definition | AsrModelDefinition | Model metadata |
| LocalPath | string | Path to model directory on disk |
| InstalledAt | DateTimeOffset | When model was downloaded/installed |
| DiskSizeBytes | long | Actual size on disk after extraction |
| IsActive | bool | Whether this is the currently loaded model |
| IsValid | bool | Whether model files pass validation |
| DetectedArchitecture | ModelArchitecture | Auto-detected architecture from files |

### ModelDownloadProgress
| Field | Type | Description |
|-------|------|-------------|
| ModelId | string | Model being downloaded |
| BytesDownloaded | long | Bytes downloaded so far |
| TotalBytes | long? | Total archive size (null if unknown) |
| PercentComplete | int | 0-100 progress percentage |
| Status | DownloadStatus | Current download state |
| Error | string? | Error message if failed |

### DownloadStatus (Enum)
| Value | Description |
|-------|-------------|
| Queued | Download requested, waiting to start |
| Downloading | Actively downloading archive |
| Extracting | Archive downloaded, extracting files |
| Validating | Files extracted, validating model integrity |
| Complete | Model ready to use |
| Failed | Download or extraction failed |
| Cancelled | User cancelled the download |

### ModelDownloadResult
| Field | Type | Description |
|-------|------|-------------|
| Success | bool | Whether download completed successfully |
| ModelId | string | Model identifier |
| LocalPath | string? | Path to extracted model (if successful) |
| AlreadyExisted | bool | True if model was already installed |
| Error | string? | Error message if failed |

### ModelValidationResult
| Field | Type | Description |
|-------|------|-------------|
| IsValid | bool | Whether model files are complete and loadable |
| ModelId | string | Model identifier |
| Architecture | ModelArchitecture | Detected architecture |
| MissingFiles | string[] | List of expected but missing files |
| Warnings | string[] | Non-fatal issues (e.g., "int8 variant available for smaller footprint") |

### SttModelOptions (Configuration — Updated)
| Field | Type | Default | Description |
|-------|------|---------|-------------|
| ActiveModel | string | "sherpa-onnx-streaming-zipformer-en-2023-06-26" | Currently active model ID |
| ModelBasePath | string | "/models/stt" | Base directory for model storage |
| NumThreads | int | 4 | ONNX inference thread count |
| SampleRate | int | 16000 | Expected input sample rate |
| ModelCatalogUrl | string | (GitHub releases URL) | URL for browsing available models |
| AllowCustomModels | bool | true | Whether user can register custom model paths |
| CustomModels | Dictionary<string, CustomModelConfig>? | null | User-registered custom models |
| AutoDownloadDefault | bool | true | Auto-download default model on first start |

### CustomModelConfig
| Field | Type | Description |
|-------|------|-------------|
| Path | string | Absolute path to model directory |
| Type | string | Architecture type hint (e.g., "streaming-transducer") |
| Languages | string[] | Supported languages |
| Description | string? | Optional description |

---

## 11. Voice Profile Management Models

### SpeakerProfile (Updated — MongoDB Persisted)
| Field | Type | Description |
|-------|------|-------------|
| Id | string | Unique profile identifier |
| Name | string | Human-readable speaker name |
| IsProvisional | bool | True if auto-discovered (not formally enrolled) |
| IsAuthorized | bool | Whether this speaker can execute commands |
| AllowedSkills | string[]? | Optional skill allowlist (null = all skills) |
| Embeddings | float[][] | Stored voice embeddings (multiple samples) |
| AverageEmbedding | float[] | Computed average embedding for comparison |
| InteractionCount | int | Number of voice interactions recorded |
| EnrolledAt | DateTimeOffset | Profile creation timestamp |
| UpdatedAt | DateTimeOffset | Last profile or embedding update |
| LastSeenAt | DateTimeOffset | Last voice interaction timestamp |
| ExpiresAt | DateTimeOffset? | Auto-expiry for provisional profiles (null = never) |

### OnboardingSession
| Field | Type | Description |
|-------|------|-------------|
| Id | string | Unique session identifier |
| SpeakerName | string | Name being enrolled |
| ProvisionalProfileId | string? | Existing provisional profile to promote (if any) |
| Prompts | string[] | Selected spoken prompts for this session |
| CollectedEmbeddings | List<float[]> | Embeddings extracted from each sample |
| CurrentPromptIndex | int | Index of next prompt to speak |
| Status | OnboardingStatus | Current session status |
| StartedAt | DateTimeOffset | When onboarding began |
| CompletedAt | DateTimeOffset? | When onboarding finished (if complete) |
| SatelliteId | string? | Which satellite is being used for onboarding |

### OnboardingStatus (Enum)
| Value | Description |
|-------|-------------|
| InProgress | Actively collecting voice samples |
| AwaitingSample | Waiting for user to speak the next prompt |
| Processing | Processing a submitted audio sample |
| Complete | All samples collected and profile created |
| Cancelled | User or system cancelled the session |
| Failed | Too many retries or quality issues |

### OnboardingStepResult
| Field | Type | Description |
|-------|------|-------------|
| Status | OnboardingStepStatus | What happened with this step |
| Message | string | TTS-friendly message for the user |
| NextPrompt | string? | Next prompt text (if more samples needed) |
| CompletedProfile | SpeakerProfile? | Created profile (if enrollment complete) |
| ProgressPercent | int | 0-100 progress indicator |

### OnboardingStepStatus (Enum)
| Value | Description |
|-------|-------------|
| NextPrompt | Sample accepted, more prompts remain |
| Retry | Sample rejected (too quiet, noisy, short) — retry same prompt |
| Complete | All samples collected, profile enrolled |
| Error | Unrecoverable error during processing |

### AudioQualityReport
| Field | Type | Description |
|-------|------|-------------|
| DurationMs | int | Length of speech segment after VAD (ms) |
| RmsEnergy | float | Root-mean-square energy level |
| EstimatedSnr | float | Estimated signal-to-noise ratio (dB) |
| IsTooQuiet | bool | Below minimum volume threshold |
| IsTooShort | bool | Below minimum duration (1.5s) |
| IsNoisy | bool | Below minimum SNR (10 dB) |
| EmbeddingNorm | float | L2 norm of extracted embedding (sanity check) |

### VoiceProfileOptions (Configuration)
| Field | Type | Default | Description |
|-------|------|---------|-------------|
| IgnoreUnknownVoices | bool | false | Drop commands from unrecognized speakers |
| SpeakerVerificationThreshold | float | 0.7 | Cosine similarity threshold for identification |
| AdaptiveProfiles | bool | true | Incrementally update embeddings from high-confidence matches |
| AdaptiveAlpha | float | 0.05 | Exponential moving average weight for adaptive updates |
| HighConfidenceThreshold | float | 0.85 | Minimum confidence for adaptive embedding updates |
| ProvisionalMatchThreshold | float | 0.65 | Threshold for matching against provisional profiles |
| ProvisionalRetentionDays | int | 30 | Days before inactive provisional profiles expire |
| SuggestEnrollmentAfter | int | 5 | Interactions before suggesting enrollment |
| OnboardingSampleCount | int | 5 | Number of voice samples during onboarding |
| MinSampleDurationMs | int | 1500 | Minimum speech duration per onboarding sample |
| MinSampleSnrDb | float | 10.0 | Minimum signal-to-noise ratio for sample acceptance |

### CustomWakeWord (MongoDB Persisted)
| Field | Type | Description |
|-------|------|-------------|
| Id | string | Unique wake word identifier |
| Phrase | string | Original text phrase (e.g., "Hey Jarvis") |
| Tokens | string | Tokenized form for keywords.txt (e.g., "▁HE Y ▁J AR V IS") |
| UserId | string? | Associated user profile ID (null = system-wide) |
| BoostScore | float | Beam search boost for this keyword (default: 1.5) |
| Threshold | float | Detection confidence threshold (default: 0.30) |
| IsDefault | bool | Whether this is the system default wake word |
| IsCalibrated | bool | Whether calibration has been performed |
| CalibratedAt | DateTimeOffset? | When last calibrated |
| CalibrationSamples | int | Number of samples used for calibration |
| DetectionRate | float? | Measured detection rate from calibration (0.0-1.0) |
| AverageConfidence | float? | Average detection confidence from calibration |
| CreatedAt | DateTimeOffset | When wake word was registered |
| UpdatedAt | DateTimeOffset | Last modification timestamp |

### CalibrationDetection
| Field | Type | Description |
|-------|------|-------------|
| Detected | bool | Whether the wake word was detected in this sample |
| Confidence | float | Detection confidence score (0.0-1.0) |
| AudioDurationMs | int | Duration of the audio sample in milliseconds |

### CalibrationResult
| Field | Type | Description |
|-------|------|-------------|
| DetectionRate | float | Fraction of samples where wake word was detected (0.0-1.0) |
| AverageConfidence | float | Average confidence across detected samples |
| BoostScore | float | Auto-tuned boost score |
| Threshold | float | Auto-tuned detection threshold |
| Recommendation | string | Human-readable recommendation (e.g., "High sensitivity — works great!") |

### BrowserOnboardingSession
| Field | Type | Description |
|-------|------|-------------|
| Id | string | Unique session identifier |
| SpeakerName | string | Name being enrolled |
| WakeWordPhrase | string? | Custom wake word being configured (null = default only) |
| WakeWordId | string? | Registered CustomWakeWord ID |
| VoiceProfileSessionId | string? | Linked OnboardingSession for voice profile |
| CurrentStep | OnboardingStep | Current step in the guided flow |
| CalibrationSamples | List<ReadOnlyMemory<float>> | Collected wake word calibration audio |
| CalibrationEnabled | bool | Whether user opted into calibration |
| StartedAt | DateTimeOffset | When browser session began |
| CompletedAt | DateTimeOffset? | When session finished |
| BrowserUserAgent | string? | Client browser identification |

### OnboardingStep (Enum)
| Value | Description |
|-------|-------------|
| Welcome | Name entry step |
| WakeWordSelection | Choose or enter custom wake word |
| VoiceTraining | Recording voice profile samples |
| WakeWordCalibration | Recording wake word calibration samples |
| Complete | All steps finished |

### OnboardingStartRequest
| Field | Type | Description |
|-------|------|-------------|
| SpeakerName | string | User's display name |
| WakeWordPhrase | string? | Custom wake word (null = use default) |
| CalibrationEnabled | bool | Whether to include wake word calibration step |

### OnboardingSummary
| Field | Type | Description |
|-------|------|-------------|
| SpeakerName | string | Enrolled name |
| ProfileId | string | Created speaker profile ID |
| WakeWord | string | Active wake word phrase |
| WakeWordCalibrated | bool | Whether calibration was completed |
| DetectionSensitivity | string | "Low", "Medium", "High" based on calibration |
| TotalDurationSeconds | int | How long the onboarding took |

### WakeWordOptions (Configuration — Updated)
| Field | Type | Default | Description |
|-------|------|---------|-------------|
| Engine | string | "sherpa-kws" | Wake word engine identifier |
| ModelPath | string | (required) | Path to KWS model files |
| DefaultKeywords | string[] | ["hey lucia"] | Default system wake words |
| Sensitivity | float | 0.5 | Global detection sensitivity (0.0-1.0) |
| MinPhraseSyllables | int | 2 | Minimum syllable count for custom wake words |
| MaxConcurrentKeywords | int | 10 | Maximum simultaneous active wake words |
| CalibrationSamples | int | 3 | Number of samples for calibration |
| AllowCustomWakeWords | bool | true | Whether users can define custom wake words |

---

## Entity Relationships

```
WyomingSession 1──1 PipelineSession
PipelineSession *──1 SpeakerProfile (optional)
PipelineSession *──1 SatelliteConfiguration
CommandPattern *──1 Skill (via SkillId)
CommandRouteResult 1──1 CommandPattern (optional)
TtsResult 1──1 ITtsEngine (via EngineId)
CachedTtsResponse *──1 TtsVoice
SpeakerProfile *──* SpeakerEmbedding (stored embeddings)
SpeakerProfile 1──* OnboardingSession (promoted from provisional)
OnboardingSession 1──* AudioQualityReport (one per sample attempt)
AsrModelDefinition 1──1 InstalledModel (when downloaded)
InstalledModel *──1 SttModelOptions (active model selection)
ModelDownloadProgress *──1 AsrModelDefinition
VoiceProfileOptions ──configures── SpeakerVerificationFilter
VoiceProfileOptions ──configures── UnknownSpeakerTracker
VoiceProfileOptions ──configures── AdaptiveProfileUpdater
CustomWakeWord *──1 SpeakerProfile (optional, via UserId)
BrowserOnboardingSession 1──1 OnboardingSession (voice profile)
BrowserOnboardingSession 1──1 CustomWakeWord (wake word config)
CalibrationResult *──1 CustomWakeWord
```
