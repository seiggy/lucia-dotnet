import json
import re
import subprocess
import tempfile
import uuid
import zipfile
from pathlib import Path

DIRECTORY = Path(__file__).resolve().parent
VARIANTS = (
    "Dockerfile", "Dockerfile.voice", "Dockerfile.voice-cpu",
    "Dockerfile.voice-rocm", "Dockerfile.ha", "Dockerfile.agenthost-jetson",
    "Dockerfile.agenthost-jetson-voice",
)


def uv_instructions(path):
    content = path.read_text()
    assert "astral.sh/uv/install.sh" not in content, path
    lines = content.splitlines()
    start = next(index for index, line in enumerate(lines)
                 if line.startswith("COPY --from=ghcr.io/astral-sh/uv:"))
    assert re.search(r"uv:\d+\.\d+\.\d+@sha256:[0-9a-f]{64} /uv /uvx /usr/local/bin/$", lines[start])
    end = start + 1
    assert lines[end].startswith("ENV UV_CACHE_DIR="), path
    while lines[end].endswith("\\"):
        end += 1
    return "\n".join(lines[start:end + 1])


recipe = uv_instructions(DIRECTORY / VARIANTS[0])
for variant in VARIANTS[1:]:
    assert uv_instructions(DIRECTORY / variant) == recipe, variant

with tempfile.TemporaryDirectory() as temporary:
    root = Path(temporary)
    # The read-only fixture mount must be searchable by both container UIDs.
    root.chmod(0o755)
    wheel = root / "lucia_uv_fixture-1.0-py3-none-any.whl"
    with zipfile.ZipFile(wheel, "w") as archive:
        archive.writestr("lucia_uv_fixture.py", 'def main():\n    print(\'{"stdio": "ready"}\')\n')
        archive.writestr("lucia_uv_fixture-1.0.dist-info/METADATA",
                         "Metadata-Version: 2.1\nName: lucia-uv-fixture\nVersion: 1.0\n")
        archive.writestr("lucia_uv_fixture-1.0.dist-info/WHEEL",
                         "Wheel-Version: 1.0\nRoot-Is-Purelib: true\nTag: py3-none-any\n")
        archive.writestr("lucia_uv_fixture-1.0.dist-info/entry_points.txt",
                         "[console_scripts]\nlucia-uv-fixture = lucia_uv_fixture:main\n")
        archive.writestr("lucia_uv_fixture-1.0.dist-info/RECORD", "")
    wheel.chmod(0o644)
    dockerfile = root / "Dockerfile"
    dockerfile.write_text(
        "FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:8c0b6857eab7b2aa57884c839bf4678414606bd7d17370f18a842ac5cf414711\n"
        + recipe + "\n"
    )
    image = f"lucia-uv-smoke:{uuid.uuid4().hex}"
    subprocess.run(["docker", "build", "--quiet", "--tag", image, str(root)], check=True)
    try:
        for uid in ("1000", "1100"):
            result = subprocess.run(
                ["docker", "run", "--rm", "--read-only", "--tmpfs", "/tmp:rw,exec,mode=1777",
                 "--user", f"{uid}:{uid}", "--mount", f"type=bind,source={root},target=/fixtures,readonly",
                 image, "sh", "-ec",
                 ('uv --version >&2; uvx --version >&2; '
                 'test -r /fixtures/lucia_uv_fixture-1.0-py3-none-any.whl; '
                 'uv python install 3.12.11 >&2; '
                 'mkdir -p "$UV_TOOL_DIR" "$UV_TOOL_BIN_DIR"; '
                 'test -w "$UV_CACHE_DIR"; test -w "$UV_PYTHON_INSTALL_DIR"; '
                 'uvx --offline --python 3.12.11 --from /fixtures/lucia_uv_fixture-1.0-py3-none-any.whl lucia-uv-fixture')],
                check=True, text=True, stdout=subprocess.PIPE,
            )
            assert json.loads(result.stdout) == {"stdio": "ready"}, result.stdout
            print(f"PASS: pinned uvx runs a local tool as UID {uid} with a read-only root filesystem.")
    finally:
        subprocess.run(["docker", "image", "rm", image], check=True, stdout=subprocess.DEVNULL)
