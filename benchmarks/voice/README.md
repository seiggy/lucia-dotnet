# Voice benchmark slice

This directory contains the first minimal, reproducible benchmark slice for speaker embeddings in Lucia.

This slice is intentionally narrow. It measures speaker embedding extraction and closed-set identification on a labeled local WAV set. It does not implement ASR WER, diarization DER/JER, full VAD endpointing, GPU sampling, or dataset acquisition.

The next slice must benchmark a cheap segmentation gate for bounded device clips. That gate decides whether Lucia should run the full diarization path, not whether VAD has solved multi-speaker audio. Device-side micro-wake-word and VAD can still trigger a bounded clip, but they do not source-separate overlapping voices. Diarization labels speaker regions; it does not untangle simultaneous speech. When overlap persists, the safe behavior is a reject-and-retry flow or a later source-separation path. Full diarization stays in the regular pipeline when the clip warrants it.

## Scope

The benchmark compares speaker embedding models such as 3D-Speaker ERes2Net, nemo_en_speakerverification_speakernet, and nemo_en_titanet_small on a single local manifest. Each model is evaluated on the same labeled clips, with enrollment and test splits tracked per speaker.

## Manifest format

The manifest is a JSON object with a top-level `clips` array. Each clip object requires:

- `path`: a WAV file path relative to the manifest file
- `speaker_id`: the labeled speaker identifier
- `split`: `enroll` or `test`

The benchmark resolves relative paths from the manifest folder before loading audio.

Voice Docker deployments persist captured clips in the `lucia-voice-data` volume. Copy them from a local voice deployment with:

```bash
docker compose -f infra/docker/docker-compose.voice.yml cp lucia:/app/data/voice-clips benchmarks/voice/live-clips
```

For the Jetson voice deployment:

```bash
docker compose -p lucia-voice -f infra/docker/docker-compose.jetson-voice.yml cp lucia-agenthost-voice:/app/data/voice-clips benchmarks/voice/live-clips
```

Speaker profile embeddings stay in PostgreSQL or MongoDB and already use persistent database volumes. Accepted enrollment WAV files are stored under their final profile ID. The benchmark recomputes embeddings from recordings for each candidate model, so copied stored embeddings are not valid comparison inputs.

Example manifest:

{
  "clips": [
    { "path": "user-provided/speaker_a/enroll_01.wav", "speaker_id": "speaker_a", "split": "enroll" },
    { "path": "user-provided/speaker_a/test_01.wav", "speaker_id": "speaker_a", "split": "test" },
    { "path": "user-provided/speaker_b/enroll_01.wav", "speaker_id": "speaker_b", "split": "enroll" },
    { "path": "user-provided/speaker_b/test_01.wav", "speaker_id": "speaker_b", "split": "test" }
  ]
}

The dataset must contain at least two speakers so verification has impostor scores. Every speaker must have at least one enrollment clip and one test clip. The benchmark fails clearly when the manifest is incomplete.

## CLI usage

```bash
dotnet run --project lucia.VoiceBenchmarks -- speaker --manifest benchmarks/voice/sample-manifest.json --model path/to/a.onnx --model-source https://example.com/a.onnx --model-threshold 0.7 --model-threshold-manifest path/to/development-manifest.json --output benchmarks/results
```

Repeat `--model`, `--model-source`, `--model-threshold`, and `--model-threshold-manifest` as aligned groups to compare models. Tune each threshold on a separate development split, then freeze it before evaluating the test clips.

## Notes

- Input audio is expected to be mono or stereo WAV files readable by NAudio.
- The benchmark downmixes stereo clips and converts them to mono 16 kHz float samples before embedding extraction.
- Output is written as deterministic JSON and Markdown reports in the requested output directory.
- Managed allocation values exclude native ONNX allocations; working-set values are process-level and are not a clean model-peak measurement.
