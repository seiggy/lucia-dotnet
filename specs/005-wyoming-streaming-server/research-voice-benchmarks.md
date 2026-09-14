# Voice identity and diarization benchmark research

## Current Lucia pipeline

Lucia does speaker verification, not diarization. `SherpaDiarizationEngine` extracts one embedding from the complete utterance and compares it with enrolled profile centroids using cosine similarity. It does not detect speaker turns, handle overlapping speakers, or use `DiarizationOptions.SegmentationModelPath`.

VAD is disabled by default. When enabled, Wyoming feeds each raw audio chunk directly to STT and separately feeds it to Silero VAD. The queued VAD segments are never consumed because `DrainVadSegmentsToStt` has no call sites. VAD currently changes the activity flag sent to the dashboard, but it does not endpoint, trim, or gate STT or speaker verification audio.

Relevant code:

- `lucia.Wyoming/Wyoming/WyomingSession.cs`
- `lucia.Wyoming/Diarization/SherpaDiarizationEngine.cs`
- `lucia.Wyoming/Vad/SherpaVadSession.cs`
- `lucia.AgentHost/appsettings.json`

## VAD and segmentation have different jobs

Keep Silero VAD available for wake-word gating, endpointing, and low-latency utterance framing. Do not make it a prerequisite for offline diarization, and do not remove audio before overlap-aware segmentation during evaluation. VAD is not a multi-speaker separator.

[Pyannote segmentation 3.0](https://huggingface.co/pyannote/segmentation-3.0) accepts 10 seconds of 16 kHz mono audio and returns frame probabilities for non-speech, three local speakers, and two-speaker overlap combinations. It performs speech and overlap detection inside the diarization pipeline, but still needs speaker embeddings and clustering to assign speaker labels.

Sherpa-onnx 1.12.34, the version Lucia uses, already exposes `OfflineSpeakerDiarization` in .NET. Its configuration combines:

- a pyannote-compatible segmentation model;
- a speaker embedding extractor;
- fast clustering with a known cluster count or tuned threshold.

The official [.NET example](https://github.com/k2-fsa/sherpa-onnx/blob/master/dotnet-examples/offline-speaker-diarization/Program.cs) uses pyannote segmentation 3.0 with the same 3D-Speaker embedding model Lucia currently ships.

## Segmentation model candidates

| Model | Artifact size | License | Recommendation |
| --- | ---: | --- | --- |
| `sherpa-onnx-pyannote-segmentation-3-0` | 6.96 MB archive | MIT | Production candidate. Benchmark FP32 and INT8 on CPU first. |
| `sherpa-onnx-reverb-diarization-v1` | 10.92 MB archive | Non-commercial | Benchmark only unless Lucia obtains a commercial license. |
| `sherpa-onnx-reverb-diarization-v2` | 254.08 MB archive | Check model terms before use | Too large to adopt without measured gains on Lucia workloads. |

Official artifacts are in the [sherpa-onnx speaker segmentation release](https://github.com/k2-fsa/sherpa-onnx/releases/tag/speaker-segmentation-models). Reverb v1 support landed in sherpa-onnx 1.10.29 according to the [upstream changelog](https://github.com/k2-fsa/sherpa-onnx/blob/master/CHANGELOG.md).

Home Assistant currently sends a bounded clip after device-side micro-wake-word and VAD. Lucia should still check that clip for interruptions and multiple speakers. The next benchmark slice must measure a cheap segmentation gate that decides whether the full diarization path is worth running. That gate is a pre-check, not a replacement for diarization.

The fast path should run segmentation first, then skip embedding and clustering only when the segmentation result is consistent with one speaker and no sustained overlap. VAD does not solve multi-speaker clips; it only helps trim or trigger the clip. Diarization labels speaker regions, but it does not source-separate overlapping speech. Sustained overlap should trigger a reject-and-retry flow or wait for a later source-separation path.

Sherpa-onnx 1.12.34 does not expose segmentation-only inference through its public .NET API. It exposes the segmentation configuration only as part of `OfflineSpeakerDiarization`. A cheap production gate therefore needs either:

- a small direct ONNX Runtime wrapper for the pyannote powerset output;
- an upstream segmentation-only API;
- full diarization on every clip until the gate exists.

The direct wrapper is the likely choice, but its powerset decoding and thresholds need benchmark coverage before production use.

## Speaker embedding candidates

| Model | Artifact size | Embedding dimension | Training domain | License |
| --- | ---: | ---: | --- | --- |
| `3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k` | 39.59 MB | 192 | Mandarin-domain 3D-Speaker data | Apache-2.0 |
| `nemo_en_titanet_small` | 40.26 MB | 192 | English, telephonic and non-telephonic speech | Apache-2.0 |
| `nemo_en_speakerverification_speakernet` | 23.41 MB | 256 | English VoxCeleb, non-telephonic speech | Apache-2.0 |

TitaNet-small is the first model to test as an English replacement. SpeakerNet is the smaller memory candidate. Do not change the default from published model-card numbers alone. The models used different training data and evaluation protocols, and the TitaNet card does not clearly separate every reported result by checkpoint.

Primary sources:

- [NVIDIA TitaNet-small model card](https://catalog.ngc.nvidia.com/orgs/nvidia/nemo/models/titanet_small)
- [NVIDIA SpeakerNet model card](https://catalog.ngc.nvidia.com/orgs/nvidia/nemo/models/speakerverification_speakernet)
- [3D-Speaker repository](https://github.com/modelscope/3D-Speaker)
- [Sherpa-onnx speaker recognition artifacts](https://github.com/k2-fsa/sherpa-onnx/releases/tag/speaker-recongition-models)

Each embedding model needs its own threshold. Lucia's fixed `0.7` threshold cannot be compared fairly across models. Tune on a development split, freeze the threshold, then evaluate on separate clips.

## Dataset choices

### ASR

[LibriSpeech](https://www.openslr.org/12) is a useful CC BY 4.0 sanity benchmark. Its clean, segmented audiobook speech does not represent noisy far-field home commands. Pair it with:

- [Mozilla Common Voice](https://commonvoice.mozilla.org/en/datasets) for accent, speaker, and recording-device diversity;
- [AMI](https://groups.inf.ed.ac.uk/ami/corpus/) for far-field meeting speech;
- [Earnings-21 and Earnings-22](https://github.com/revdotcom/speech-datasets) for long-form conversational English.

### Diarization

Use [AMI](https://groups.inf.ed.ac.uk/ami/corpus/) as the freely downloadable English core set. It contains multi-speaker meetings, multiple microphone conditions, and overlap. Add [VoxConverse](https://github.com/joonson/voxconverse) for harder in-the-wild overlap.

[DIHARD III](https://dihardchallenge.github.io/dihard3/) and [CALLHOME](https://catalog.ldc.upenn.edu/LDC97S42) are useful stress tests but require LDC access. [LibriCSS](https://github.com/chenzhuo1011/libri_css) provides controlled overlap from 0 to 40 percent, but its main use is continuous speech separation and recognition rather than replacing AMI or VoxConverse DER evaluation.

### Speaker verification and identity

Use official VoxCeleb1 cleaned trials if its acquisition and use are approved. Add a consented Lucia corpus recorded on target microphones. Enrollment and test clips must come from different sessions. Do not redistribute VoxCeleb audio in this repository.

## Metrics

Keep task metrics separate.

| Stage | Primary metrics |
| --- | --- |
| ASR | Micro WER, CER where applicable, RTF, p50 and p95 finalization latency |
| VAD and endpointing | Miss rate, false alarm rate, onset delay, endpoint delay |
| Diarization | DER and JER with 0 ms collar and overlap included; miss, false alarm, and confusion components |
| Speaker verification | EER, normalized minDCF at `Ptarget=0.01`, FAR and FRR at a frozen threshold |
| Speaker identification | Closed-set top-1 accuracy and open-set rejection at a declared FAR |
| Runtime | Per-stage RTF, wall time, CPU core-equivalents, process RSS, managed allocations, throughput at concurrency 1 and production concurrency |

Report oracle-segmented and end-to-end VAD ASR separately. Do not compare WER with DER, EER with full-pipeline DER, or DER runs that use different collars and overlap rules.

On Jetson, sample [`tegrastats`](https://docs.nvidia.com/jetson/archives/r36.4.4/DeveloperGuide/AT/JetsonLinuxDevelopmentTools/TegrastatsUtility.html) every 100 ms. Record RAM, CPU, `GR3D_FREQ`, EMC, temperature, and power. Jetson uses unified memory, so pair device RAM with process RSS instead of reporting discrete GPU memory.

## Reproducibility contract

Every published run should record:

- dataset manifest and checksums;
- model URL and SHA-256;
- sherpa-onnx and ONNX Runtime versions;
- execution provider and thread count;
- operating system, CPU, GPU, Jetson power mode, and clocks;
- audio normalization and resampling rules;
- warm-up count, measured repetitions, and concurrency;
- development and evaluation split IDs;
- metric collar, overlap, and threshold settings.

Store machine-readable JSON beside the Markdown summary. Commit manifests and results, not third-party datasets or model binaries.

## Staged plan

1. Benchmark the shipped path with VAD off, raw audio to STT, and one embedding per utterance.
2. Compare ERes2Net, TitaNet-small, and SpeakerNet for EER, minDCF, top-1 accuracy, RTF, memory, and CPU cost.
3. Compare pyannote segmentation 3.0 FP32 and INT8 against Reverb v1 for research only, using overlap-inclusive DER and JER.
4. Keep pyannote as the production candidate because its license permits Lucia's use.
5. Benchmark the next slice: a segmentation-first gate on every bounded device clip. Skip clustering only for confidently single-speaker, non-overlapping clips.
6. Run full diarization for likely interruptions or speaker changes. Reject sustained overlap until source separation is available, and keep diarization in the regular pipeline rather than treating VAD as a substitute for it.
7. Design server-side VAD endpointing as a separate change. The current VAD path does not gate audio.
8. Run CPU and CUDA measurements on physical Orin hardware. Small models can lose to CPU once CUDA setup and unified-memory costs are included.
