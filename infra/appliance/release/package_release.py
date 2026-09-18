import argparse
import hashlib
import json
import pathlib
import re
import shutil
import urllib.parse

COPY_BUFFER_BYTES = 1024 * 1024


def hash_file(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def chunk_file(
    source: pathlib.Path,
    output_directory: pathlib.Path,
    chunk_bytes: int,
    download_base: str,
) -> list[dict[str, object]]:
    part_count = (source.stat().st_size + chunk_bytes - 1) // chunk_bytes
    parts: list[dict[str, object]] = []

    if part_count == 1:
        destination = output_directory / source.name
        shutil.copyfile(source, destination)
        part_paths = [destination]
    else:
        part_paths = []
        with source.open("rb") as input_stream:
            for index in range(part_count):
                destination = output_directory / f"{source.name}.part{index:02d}"
                remaining = chunk_bytes
                with destination.open("wb") as output_stream:
                    while remaining > 0:
                        block = input_stream.read(
                            min(COPY_BUFFER_BYTES, remaining)
                        )
                        if not block:
                            break
                        output_stream.write(block)
                        remaining -= len(block)
                part_paths.append(destination)

    for path in part_paths:
        parts.append(
            {
                "name": path.name,
                "bytes": path.stat().st_size,
                "sha256": hash_file(path),
                "url": download_base + urllib.parse.quote(path.name),
            }
        )
    return parts


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repository", required=True)
    parser.add_argument("--tag", required=True)
    parser.add_argument("--installer", type=pathlib.Path)
    parser.add_argument("--lucia", type=pathlib.Path, required=True)
    parser.add_argument("--os", type=pathlib.Path)
    parser.add_argument("--lucia-version")
    parser.add_argument("--os-version")
    parser.add_argument("--release-mode", choices=("full", "app-only"))
    parser.add_argument("--image-input-fingerprint")
    parser.add_argument("--lucia-source-jetson-linux", required=True)
    parser.add_argument("--lucia-source-redis", required=True)
    parser.add_argument("--lucia-source-cuda", required=True)
    parser.add_argument("--lucia-source-cudnn", required=True)
    parser.add_argument("--lucia-source-onnx-runtime", required=True)
    parser.add_argument("--lucia-source-sherpa-onnx", required=True)
    parser.add_argument("--os-source-jetson-linux")
    parser.add_argument("--os-source-redis")
    parser.add_argument("--os-source-cuda")
    parser.add_argument("--os-source-cudnn")
    parser.add_argument("--os-source-onnx-runtime")
    parser.add_argument("--os-source-sherpa-onnx")
    parser.add_argument("--lucia-target-jetson-linux", required=True)
    parser.add_argument("--lucia-target-redis", required=True)
    parser.add_argument("--lucia-target-cuda", required=True)
    parser.add_argument("--lucia-target-cudnn", required=True)
    parser.add_argument("--lucia-target-onnx-runtime", required=True)
    parser.add_argument("--lucia-target-sherpa-onnx", required=True)
    parser.add_argument("--os-target-jetson-linux")
    parser.add_argument("--os-target-redis")
    parser.add_argument("--os-target-cuda")
    parser.add_argument("--os-target-cudnn")
    parser.add_argument("--os-target-onnx-runtime")
    parser.add_argument("--os-target-sherpa-onnx")
    parser.add_argument("--output-dir", type=pathlib.Path, required=True)
    parser.add_argument("--chunk-bytes", type=int, default=1_900_000_000)
    return parser.parse_args()


def main() -> None:
    arguments = parse_arguments()
    if not re.fullmatch(r"v[0-9]+\.[0-9]+\.[0-9]+", arguments.tag):
        raise SystemExit("--tag must match vMAJOR.MINOR.PATCH")
    if arguments.chunk_bytes < 1:
        raise SystemExit("--chunk-bytes must be positive")

    release_mode = arguments.release_mode or (
        "full" if arguments.installer and arguments.os else "app-only"
    )
    if arguments.installer and not arguments.os:
        raise SystemExit("--installer requires --os when building a full release")
    if arguments.os and not arguments.installer:
        raise SystemExit("--os requires --installer when building a full release")
    if release_mode == "full" and (arguments.installer is None or arguments.os is None):
        raise SystemExit("full release mode requires --installer and --os payloads")
    if release_mode == "app-only" and (arguments.installer is not None or arguments.os is not None):
        raise SystemExit("app-only release mode rejects --installer and --os payloads")

    release_version = arguments.tag.removeprefix("v")
    lucia_version = arguments.lucia_version or release_version
    os_version = arguments.os_version or release_version
    version_pattern = r"[0-9]+\.[0-9]+\.[0-9]+"
    if re.fullmatch(version_pattern, lucia_version) is None:
        raise SystemExit("--lucia-version must match MAJOR.MINOR.PATCH")
    if release_mode == "full" and re.fullmatch(version_pattern, os_version) is None:
        raise SystemExit("--os-version must match MAJOR.MINOR.PATCH")

    lucia_source_runtime = {
        "jetsonLinux": arguments.lucia_source_jetson_linux,
        "redis": arguments.lucia_source_redis,
        "cuda": arguments.lucia_source_cuda,
        "cudnn": arguments.lucia_source_cudnn,
        "onnxRuntime": arguments.lucia_source_onnx_runtime,
        "sherpaOnnx": arguments.lucia_source_sherpa_onnx,
    }
    os_source_runtime = {
        "jetsonLinux": arguments.os_source_jetson_linux or "",
        "redis": arguments.os_source_redis or "",
        "cuda": arguments.os_source_cuda or "",
        "cudnn": arguments.os_source_cudnn or "",
        "onnxRuntime": arguments.os_source_onnx_runtime or "",
        "sherpaOnnx": arguments.os_source_sherpa_onnx or "",
    }
    if any(not value.strip() for value in lucia_source_runtime.values()):
        raise SystemExit("Lucia source runtime versions must not be empty")
    if release_mode == "full" and any(not value.strip() for value in os_source_runtime.values()):
        raise SystemExit("OS source runtime versions must not be empty")
    if release_mode == "app-only" and any(value.strip() for value in os_source_runtime.values()):
        raise SystemExit("app-only release mode rejects OS runtime source metadata")

    lucia_target_runtime = {
        "jetsonLinux": arguments.lucia_target_jetson_linux,
        "redis": arguments.lucia_target_redis,
        "cuda": arguments.lucia_target_cuda,
        "cudnn": arguments.lucia_target_cudnn,
        "onnxRuntime": arguments.lucia_target_onnx_runtime,
        "sherpaOnnx": arguments.lucia_target_sherpa_onnx,
    }
    os_target_runtime = {
        "jetsonLinux": arguments.os_target_jetson_linux or "",
        "redis": arguments.os_target_redis or "",
        "cuda": arguments.os_target_cuda or "",
        "cudnn": arguments.os_target_cudnn or "",
        "onnxRuntime": arguments.os_target_onnx_runtime or "",
        "sherpaOnnx": arguments.os_target_sherpa_onnx or "",
    }
    if any(not value.strip() for value in lucia_target_runtime.values()):
        raise SystemExit("Lucia target runtime versions must not be empty")
    if release_mode == "full" and any(not value.strip() for value in os_target_runtime.values()):
        raise SystemExit("OS target runtime versions must not be empty")
    if release_mode == "app-only" and any(value.strip() for value in os_target_runtime.values()):
        raise SystemExit("app-only release mode rejects OS runtime target metadata")
    if re.fullmatch(r"[0-9a-f]{64}", arguments.image_input_fingerprint or "") is None:
        raise SystemExit("--image-input-fingerprint must be a SHA-256 digest")

    inputs = {"lucia": arguments.lucia.resolve()}
    if arguments.installer is not None:
        inputs["installer"] = arguments.installer.resolve()
    if arguments.os is not None:
        inputs["os"] = arguments.os.resolve()
    for name, path in inputs.items():
        if not path.is_file():
            raise SystemExit(f"{name} payload does not exist: {path}")
        if path.stat().st_size == 0:
            raise SystemExit(f"{name} payload is empty: {path}")

    output_directory = arguments.output_dir.resolve()
    if output_directory.exists() and any(output_directory.iterdir()):
        raise SystemExit(f"output directory is not empty: {output_directory}")
    output_directory.mkdir(parents=True, exist_ok=True)

    download_base = (
        f"https://github.com/{arguments.repository}/releases/download/"
        f"{arguments.tag}/"
    )
    channel_metadata = {
        "installer": ("full-image", "raw-zstd", release_version),
        "lucia": ("lucia-update", "tar-zstd", lucia_version),
        "os": ("os-update", "tar-zstd", os_version),
    }
    channels: dict[str, dict[str, object]] = {}
    for name, source in inputs.items():
        if name not in channel_metadata:
            continue
        kind, file_format, channel_version = channel_metadata[name]
        channels[name] = {
            "kind": kind,
            "format": file_format,
            "version": channel_version,
            "bytes": source.stat().st_size,
            "sha256": hash_file(source),
            "parts": chunk_file(
                source,
                output_directory,
                arguments.chunk_bytes,
                download_base,
            ),
        }

    channels["lucia"]["requires"] = {
        "layoutVersion": 1,
        "dataSchemaVersion": 1,
        "source": lucia_source_runtime,
        "target": lucia_target_runtime,
        "reboot": False,
    }
    if "os" in channels:
        channels["os"]["requires"] = {
            "minimumLuciaVersion": lucia_version,
            "layoutVersion": 1,
            "source": os_source_runtime,
            "target": os_target_runtime,
            "reboot": True,
        }

    manifest = {
        "schemaVersion": 1,
        "repository": arguments.repository,
        "attestationBundleUrl": download_base + "lucia-appliance-attestations.jsonl",
        "releaseApi": (
            f"https://api.github.com/repos/{arguments.repository}/releases/tags/"
            f"{arguments.tag}"
        ),
        "tag": arguments.tag,
        "version": release_version,
        "releaseMode": release_mode,
        "compatibility": {
            "architecture": "arm64",
            "board": "jetson-orin-nano-super-p3767-0005",
            "minimumDiskBytes": 61_203_283_968,
            "layoutVersion": 1,
            "dataSchemaVersion": 1,
        },
        "releaseNotesUrl": (
            f"https://github.com/{arguments.repository}/releases/tag/"
            f"{arguments.tag}"
        ),
        "channels": channels,
    }
    manifest["imageInputFingerprint"] = arguments.image_input_fingerprint

    manifest_path = output_directory / "lucia-appliance-manifest.json"
    manifest_path.write_text(
        json.dumps(manifest, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )

    checksum_lines = [
        f"{hash_file(path)}  {path.name}"
        for path in sorted(output_directory.iterdir())
        if path.is_file()
    ]
    (output_directory / "SHA256SUMS").write_text(
        "\n".join(checksum_lines) + "\n",
        encoding="utf-8",
        newline="\n",
    )


if __name__ == "__main__":
    main()
