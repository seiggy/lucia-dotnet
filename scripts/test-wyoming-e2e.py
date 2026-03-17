#!/usr/bin/env python3
"""
End-to-end Wyoming protocol test against the LIVE server.
Streams the actual WAV file at real-time pace, exactly like HA does.
Usage: python3 test-wyoming-e2e.py [host] [port]
"""
import socket, json, time, wave, sys

HOST = sys.argv[1] if len(sys.argv) > 1 else "localhost"
PORT = int(sys.argv[2]) if len(sys.argv) > 2 else 10400
WAV_PATH = "samples/unfiltered_sample.wav"
EXPECTED = "Turn on Zack's light in the bedroom, please."
TIMEOUT = 30

def send(sock, event_type, data=None, payload=None):
    data_bytes = json.dumps(data).encode() if data else b""
    header = {"type": event_type, "data_length": len(data_bytes), "payload_length": len(payload) if payload else 0}
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

print(f"═══ Wyoming E2E Test ═══")
print(f"Server: {HOST}:{PORT}")
print(f"WAV: {WAV_PATH}")
print(f"Expected: \"{EXPECTED}\"")
print()

# Load WAV
with wave.open(WAV_PATH, 'rb') as w:
    rate = w.getframerate()
    pcm = w.readframes(w.getnframes())
audio_ms = len(pcm) / 2 * 1000 / rate
print(f"Audio: {audio_ms:.0f}ms at {rate}Hz ({len(pcm)} bytes)")

# Connect
sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
sock.settimeout(TIMEOUT)
try:
    sock.connect((HOST, PORT))
except Exception as e:
    print(f"CONNECT FAILED: {e}")
    sys.exit(1)
print(f"Connected to {HOST}:{PORT}")

# Step 1: Describe
t_start = time.monotonic()
send(sock, "describe")
info = recv(sock, timeout=5)
if info is None or info["type"] != "info":
    print(f"FAIL: Expected info, got {info}")
    sys.exit(1)
print(f"✓ Describe → info ({(time.monotonic()-t_start)*1000:.0f}ms)")

# Step 2: Transcribe (set language)
send(sock, "transcribe", {"language": "en"})
print(f"✓ Sent transcribe")

# Step 3: Audio start
send(sock, "audio-start", {"rate": rate, "width": 2, "channels": 1})
print(f"✓ Sent audio-start")

# Step 4: Stream audio chunks at REAL-TIME pace
chunk_ms = 20  # 20ms chunks like HA
chunk_bytes = rate * 2 * chunk_ms // 1000
chunks = 0
t_stream_start = time.monotonic()

for i in range(0, len(pcm), chunk_bytes):
    chunk = pcm[i:i+chunk_bytes]
    send(sock, "audio-chunk", {"rate": rate, "width": 2, "channels": 1}, payload=chunk)
    chunks += 1
    time.sleep(chunk_ms / 1000.0)  # REAL-TIME throttle

t_stream_end = time.monotonic()
stream_ms = (t_stream_end - t_stream_start) * 1000
print(f"✓ Streamed {chunks} chunks in {stream_ms:.0f}ms (real-time)")

# Step 5: Audio stop
send(sock, "audio-stop")
t_stop_sent = time.monotonic()
print(f"✓ Sent audio-stop")

# Step 6: Wait for transcript
print(f"  Waiting for transcript...")
resp = recv(sock, timeout=TIMEOUT)
t_done = time.monotonic()
sock.close()

finalize_ms = (t_done - t_stop_sent) * 1000
total_ms = (t_done - t_start) * 1000

if resp is None:
    print(f"FAIL: No response after {TIMEOUT}s")
    sys.exit(1)

if resp["type"] == "error":
    print(f"FAIL: Error response: {resp['data']}")
    sys.exit(1)

if resp["type"] != "transcript":
    print(f"FAIL: Expected transcript, got {resp['type']}: {resp.get('data')}")
    sys.exit(1)

text = resp["data"].get("text", "")
confidence = resp["data"].get("confidence", 0)

print()
print(f"═══ Results ═══")
print(f"  Transcript:  \"{text}\"")
print(f"  Confidence:  {confidence}")
print(f"  Stream time: {stream_ms:.0f}ms")
print(f"  Finalize:    {finalize_ms:.0f}ms  ← (AudioStop → Transcript)")
print(f"  Total:       {total_ms:.0f}ms")
print(f"  Audio:       {audio_ms:.0f}ms")
print(f"  Overhead:    {total_ms - audio_ms:.0f}ms above real-time")

# Strip speaker tag for comparison
clean = text
tag_end = clean.find("/>")
if tag_end >= 0:
    clean = clean[tag_end+2:].strip()

# Simple WER
ref_words = EXPECTED.upper().replace("'S","S").replace(",","").replace(".","").split()
hyp_words = clean.upper().replace("'S","S").replace(",","").replace(".","").replace("ZACH","ZACK").split()
if len(ref_words) > 0:
    n, m = len(ref_words), len(hyp_words)
    d = [[0]*(m+1) for _ in range(n+1)]
    for i in range(n+1): d[i][0] = i
    for j in range(m+1): d[0][j] = j
    for i in range(1,n+1):
        for j in range(1,m+1):
            cost = 0 if ref_words[i-1]==hyp_words[j-1] else 1
            d[i][j] = min(d[i-1][j]+1, d[i][j-1]+1, d[i-1][j-1]+cost)
    wer = d[n][m] / n * 100
else:
    wer = 0

print(f"  WER:         {wer:.1f}%")
print()

if finalize_ms > 200:
    print(f"⚠ SLOW: Finalize {finalize_ms:.0f}ms exceeds 200ms target")
if wer > 10:
    print(f"⚠ INACCURATE: WER {wer:.1f}% exceeds 10% target")
if finalize_ms <= 200 and wer <= 10:
    print(f"✅ PASS: {finalize_ms:.0f}ms finalize, {wer:.1f}% WER")
