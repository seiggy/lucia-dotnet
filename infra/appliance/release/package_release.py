import argparse
import hashlib
import json
import pathlib
import re
import shutil
import urllib.parse


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
                destination.write_bytes(input_stream.read(chunk_bytes))
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
    parser.add_argument("--installer", type=pathlib.Path, required=True)
    parser.add_argument("--lucia", type=pathlib.Path, required=True)
    parser.add_argument("--os", type=pathlib.Path, required=True)
    parser.add_argument("--lucia-version")
    parser.add_argument("--os-version")
    parser.add_argument("--output-dir", type=pathlib.Path, required=True)
    parser.add_argument("--chunk-bytes", type=int, default=1_900_000_000)
    return parser.parse_args()


def main() -> None:
    arguments = parse_arguments()
    if not re.fullmatch(r"v[0-9]+\.[0-9]+\.[0-9]+", arguments.tag):
        raise SystemExit("--tag must match vMAJOR.MINOR.PATCH")
    if arguments.chunk_bytes < 1:
        raise SystemExit("--chunk-bytes must be positive")
    release_version = arguments.tag.removeprefix("v")
    lucia_version = arguments.lucia_version or release_version
    os_version = arguments.os_version or release_version
    version_pattern = r"[0-9]+\.[0-9]+\.[0-9]+"
    if re.fullmatch(version_pattern, lucia_version) is None:
        raise SystemExit("--lucia-version must match MAJOR.MINOR.PATCH")
    if re.fullmatch(version_pattern, os_version) is None:
        raise SystemExit("--os-version must match MAJOR.MINOR.PATCH")

    inputs = {
        "installer": arguments.installer.resolve(),
        "lucia": arguments.lucia.resolve(),
        "os": arguments.os.resolve(),
    }
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
    channels: dict[str, object] = {}
    for name, source in inputs.items():
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

    manifest = {
        "schemaVersion": 1,
        "repository": arguments.repository,
        "releaseApi": (
            f"https://api.github.com/repos/{arguments.repository}/releases/latest"
        ),
        "tag": arguments.tag,
        "version": release_version,
        "compatibility": {
            "architecture": "arm64",
            "board": "jetson-orin-nano-super-p3767-0005",
            "jetsonLinux": "36.5.2",
            "minimumDiskBytes": 61_203_283_968,
        },
        "channels": channels,
    }
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
