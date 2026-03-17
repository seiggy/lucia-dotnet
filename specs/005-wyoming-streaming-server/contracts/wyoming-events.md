# Wyoming Protocol Event Contracts

**Spec**: 005-wyoming-streaming-server
**Date**: 2026-03-13

## Wire Format

Every Wyoming message consists of:
1. A UTF-8 JSON header terminated by `\n` (newline)
2. Optional extra data (`data_length` bytes of UTF-8 JSON)
3. Optional binary payload (`payload_length` bytes)

```
{"type":"<event-type>","data":{...},"data_length":0,"payload_length":0}\n
[extra_data_bytes]
[payload_bytes]
```

## Event Contracts

### describe
**Direction**: Client → Server
**Purpose**: Request server capabilities
```json
{"type":"describe"}\n
```

### info
**Direction**: Server → Client
**Purpose**: Respond with available services
```json
{
  "type": "info",
  "data": {
    "asr": [
      {
        "name": "sherpa-zipformer-en",
        "description": "Sherpa-ONNX Streaming Zipformer (English)",
        "version": "2023-06-26",
        "languages": ["en"],
        "attribution": {
          "name": "k2-fsa/sherpa-onnx",
          "url": "https://github.com/k2-fsa/sherpa-onnx"
        },
        "installed": true
      }
    ],
    "tts": [
      {
        "name": "qwen3-tts",
        "description": "Qwen3-TTS 0.6B via ONNX",
        "version": "0.6.1",
        "languages": ["en", "zh", "ja", "ko", "es"],
        "voices": [
          {"name": "ryan", "language": "en", "description": "Male English"},
          {"name": "emma", "language": "en", "description": "Female English"}
        ],
        "installed": true
      },
      {
        "name": "chatterbox-turbo",
        "description": "Chatterbox Turbo 350M via ONNX Runtime",
        "version": "1.0.0",
        "languages": ["en"],
        "voices": [
          {"name": "default", "language": "en", "description": "Default English"}
        ],
        "installed": true
      }
    ],
    "wake": [
      {
        "name": "hey_lucia",
        "description": "Hey Lucia wake word",
        "version": "1.0.0",
        "languages": ["en"],
        "installed": true
      }
    ]
  }
}\n
```

### audio-start
**Direction**: Bidirectional
**Purpose**: Begin an audio stream
```json
{"type":"audio-start","data":{"rate":24000,"width":2,"channels":1}}\n
```

**Note**: Wyoming TTS responses SHOULD stream native 24kHz/16-bit/mono PCM when the satellite supports it. Satellites that cannot consume 24kHz receive a 16kHz fallback stream instead.

### audio-chunk
**Direction**: Bidirectional
**Purpose**: Stream PCM audio data
```json
{"type":"audio-chunk","data":{"rate":16000,"width":2,"channels":1},"payload_length":3200}\n
[3200 bytes of raw PCM audio]
```

**Payload**: Raw PCM audio. For 16kHz/16-bit/mono, each 20ms frame = 640 bytes. For 24kHz/16-bit/mono, each 20ms frame = 960 bytes.

### audio-stop
**Direction**: Bidirectional
**Purpose**: End an audio stream
```json
{"type":"audio-stop"}\n
```

### transcribe
**Direction**: Client → Server
**Purpose**: Request transcription of preceding audio stream
```json
{"type":"transcribe","data":{"name":"sherpa-zipformer-en","language":"en"}}\n
```

### transcript
**Direction**: Server → Client
**Purpose**: Return recognized text
```json
{"type":"transcript","data":{"text":"turn on the kitchen lights","confidence":0.94}}\n
```

### partial-transcript
**Direction**: Server → Client
**Purpose**: Return an incremental non-final recognition result
```json
{"type":"partial-transcript","data":{"text":"turn on the kitchen","confidence":0.81,"is_final":false}}\n
```

### detect
**Direction**: Client → Server
**Purpose**: Begin wake word detection
```json
{"type":"detect","data":{"names":["hey_lucia"]}}\n
```

### detection
**Direction**: Server → Client
**Purpose**: Wake word detected
```json
{"type":"detection","data":{"name":"hey_lucia","timestamp":1710291919}}\n
```

### not-detected
**Direction**: Server → Client
**Purpose**: Audio stream ended without detection
```json
{"type":"not-detected"}\n
```

### synthesize
**Direction**: Client → Server
**Purpose**: Request text-to-speech synthesis
```json
{"type":"synthesize","data":{"text":"The kitchen lights have been turned on","voice":"ryan","language":"en"}}\n
```

**Response**: Server sends `audio-start`, multiple `audio-chunk`, then `audio-stop`.

### voice-started
**Direction**: Server → Client
**Purpose**: Voice activity detected
```json
{"type":"voice-started","data":{"timestamp":1710291919}}\n
```

### voice-stopped
**Direction**: Server → Client
**Purpose**: Voice activity ended
```json
{"type":"voice-stopped","data":{"timestamp":1710291922}}\n
```

### error
**Direction**: Server → Client
**Purpose**: Report an error
```json
{"type":"error","data":{"text":"Model not loaded","code":"model_not_found"}}\n
```

---

## Typical Session Flows

### Wake Word → STT → Response
```
Client: {"type":"detect","data":{"names":["hey_lucia"]}}\n
Client: {"type":"audio-start","data":{"rate":16000,"width":2,"channels":1}}\n
Client: {"type":"audio-chunk","payload_length":640}\n[640 bytes]
Client: {"type":"audio-chunk","payload_length":640}\n[640 bytes]
...
Server: {"type":"detection","data":{"name":"hey_lucia"}}\n

Client: {"type":"audio-start","data":{"rate":16000,"width":2,"channels":1}}\n
Client: {"type":"audio-chunk","payload_length":640}\n[640 bytes]
...
Client: {"type":"audio-stop"}\n
Client: {"type":"transcribe","data":{"language":"en"}}\n
Server: {"type":"partial-transcript","data":{"text":"turn on the kitchen","confidence":0.81,"is_final":false}}\n
Server: {"type":"transcript","data":{"text":"turn on the kitchen lights","confidence":0.94}}\n
```

### TTS Synthesis
```
Client: {"type":"synthesize","data":{"text":"Done. Kitchen lights are now on.","voice":"ryan"}}\n
Server: {"type":"audio-start","data":{"rate":24000,"width":2,"channels":1}}\n
Server: {"type":"audio-chunk","payload_length":4800}\n[4800 bytes]
Server: {"type":"audio-chunk","payload_length":4800}\n[4800 bytes]
...
Server: {"type":"audio-stop"}\n
```

**Fallback**: If a satellite does not support 24kHz TTS playback, the server resamples the response to 16kHz and emits `audio-start`/`audio-chunk` with `rate: 16000` instead.

### Service Discovery
```
Client: {"type":"describe"}\n
Server: {"type":"info","data":{...capabilities...}}\n
```
