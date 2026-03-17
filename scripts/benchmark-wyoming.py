#!/usr/bin/env python3
"""
Wyoming STT Performance Benchmark
Runs all WAV files through the live Wyoming server and reports transcripts + timing.
Usage: python3 scripts/benchmark-wyoming.py [host] [port] [wav_dir]
"""
import socket, json, time, wave, sys, os, glob

HOST = sys.argv[1] if len(sys.argv) > 1 else "localhost"
PORT = int(sys.argv[2]) if len(sys.argv) > 2 else 10400
WAV_DIR = sys.argv[3] if len(sys.argv) > 3 else "lucia.AgentHost/data/voice-clips"
TIMEOUT = 30

def send(sock, event_type, data=None, payload=None):
    data_bytes = json.dumps(data).encode() if data else b""
    header = {"type": event_type, "data_length": len(data_bytes),
              "payload_length": len(payload) if payload else 0}
    sock.sendall((json.dumps(header) + "\n").encode() + data_bytes + (payload or b""))

def recv(sock, timeout=TIMEOUT):
    sock.settimeout(timeout)
    buf = b""
    while b"\n" not in buf:
        chunk = sock.recv(4096)
        if not chunk: return None
        buf += chunk
    hl, rem = buf.split(b"\n", 1)
    h = json.loads(hl)
    dl = h.get("data_length", 0)
    data = h.get("data")
    if dl > 0:
        while len(rem) < dl: rem += sock.recv(dl - len(rem))
        data = json.loads(rem[:dl])
    return {"type": h["type"], "data": data}

def transcribe_wav(host, port, wav_path):
    """Stream a WAV through Wyoming protocol and return (transcript, finalize_ms, audio_ms, error)."""
    try:
        with wave.open(wav_path, 'rb') as w:
            rate = w.getframerate()
            channels = w.getnchannels()
            width = w.getsampwidth()
            pcm = w.readframes(w.getnframes())
    except Exception as e:
        return None, 0, 0, f"WAV read error: {e}"

    audio_ms = len(pcm) / width / channels * 1000 / rate

    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(TIMEOUT)
        sock.connect((host, port))
    except Exception as e:
        return None, 0, audio_ms, f"Connect error: {e}"

    try:
        # Transcribe + AudioStart
        send(sock, "transcribe", {"language": "en"})
        send(sock, "audio-start", {"rate": rate, "width": width, "channels": channels})

        # Stream at real-time pace (20ms chunks)
        chunk_ms = 20
        chunk_bytes = rate * width * channels * chunk_ms // 1000
        for i in range(0, len(pcm), chunk_bytes):
            send(sock, "audio-chunk",
                 {"rate": rate, "width": width, "channels": channels},
                 payload=pcm[i:i+chunk_bytes])
            time.sleep(chunk_ms / 1000.0)

        # AudioStop and wait for transcript
        t0 = time.monotonic()
        send(sock, "audio-stop")
        resp = recv(sock, timeout=TIMEOUT)
        finalize_ms = (time.monotonic() - t0) * 1000

        sock.close()

        if resp is None:
            return None, finalize_ms, audio_ms, "No response"
        if resp["type"] == "error":
            return None, finalize_ms, audio_ms, f"Error: {resp.get('data', {}).get('text', '?')}"
        if resp["type"] != "transcript":
            return None, finalize_ms, audio_ms, f"Unexpected: {resp['type']}"

        text = resp["data"].get("text", "") if resp.get("data") else ""
        return text, finalize_ms, audio_ms, None

    except Exception as e:
        sock.close()
        return None, 0, audio_ms, f"Protocol error: {e}"


# Find all WAV files
wav_files = sorted(glob.glob(os.path.join(WAV_DIR, "**", "*.wav"), recursive=True))

# Also include samples/ directory
for extra_dir in ["samples"]:
    wav_files.extend(sorted(glob.glob(os.path.join(extra_dir, "**", "*.wav"), recursive=True)))

wav_files = sorted(set(wav_files))

if not wav_files:
    print(f"No WAV files found in {WAV_DIR}")
    sys.exit(1)

print(f"═══ Wyoming STT Benchmark ═══")
print(f"Server: {HOST}:{PORT}")
print(f"Files:  {len(wav_files)} WAV files")
print()
print(f"{'#':>3} {'Audio':>7} {'Final':>7} {'Status':>8}  {'File':<40} Transcript")
print("─" * 120)

results = []
for i, wav_path in enumerate(wav_files, 1):
    rel_path = os.path.relpath(wav_path)
    # Shorten the path for display
    short = rel_path
    if len(short) > 38:
        short = "..." + short[-35:]

    text, finalize_ms, audio_ms, error = transcribe_wav(HOST, PORT, wav_path)

    if error:
        status = "ERROR"
        display = error
    elif not text or text.strip() == "":
        status = "EMPTY"
        display = "(empty transcript)"
    else:
        status = "OK"
        # Strip speaker tag for display
        display = text
        tag_end = display.find("/>")
        if tag_end >= 0:
            display = display[tag_end+2:].strip()

    results.append({
        "file": rel_path,
        "audio_ms": audio_ms,
        "finalize_ms": finalize_ms,
        "status": status,
        "transcript": text or "",
        "error": error,
    })

    print(f"{i:>3} {audio_ms:>6.0f}ms {finalize_ms:>6.0f}ms {status:>8}  {short:<40} {display[:50]}")

# Summary
print()
print("─" * 120)
ok = [r for r in results if r["status"] == "OK"]
errors = [r for r in results if r["status"] == "ERROR"]
empty = [r for r in results if r["status"] == "EMPTY"]

total_audio = sum(r["audio_ms"] for r in results)
total_finalize = sum(r["finalize_ms"] for r in ok) if ok else 0
avg_finalize = total_finalize / len(ok) if ok else 0
max_finalize = max((r["finalize_ms"] for r in ok), default=0)
min_finalize = min((r["finalize_ms"] for r in ok), default=0)

print(f"═══ Summary ═══")
print(f"  Total files:     {len(results)}")
print(f"  Transcribed:     {len(ok)} OK, {len(empty)} empty, {len(errors)} errors")
print(f"  Total audio:     {total_audio/1000:.1f}s")
print(f"  Avg finalize:    {avg_finalize:.0f}ms")
print(f"  Min finalize:    {min_finalize:.0f}ms")
print(f"  Max finalize:    {max_finalize:.0f}ms")
print(f"  Under 200ms:     {sum(1 for r in ok if r['finalize_ms'] <= 200)}/{len(ok)}")
