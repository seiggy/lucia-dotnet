#!/usr/bin/env python3
import argparse
import hashlib
import json
import re
import subprocess
from pathlib import Path

DEFAULT_FINGERPRINT_PATHS = [
    "infra/appliance/rootfs",
    "infra/appliance/installer/rootfs",
    "infra/appliance/installer/lucia-install",
    "infra/appliance/installer/build-loop-image.sh",
    "infra/appliance/installer/finalize-loop-image.sh",
    "infra/appliance/installer/build-sd-image.sh",
    "infra/appliance/installer/verify-built-image.sh",
    "infra/appliance/build-native-bundle.sh",
    "infra/appliance/release/build-release-assets.sh",
    "infra/appliance/release/appliance.lock",
    "infra/appliance/release/voice-asset-key.sh",
    "infra/appliance/release/trusted-root.jsonl",
    "infra/appliance/release/planner.py",
    "infra/docker/Dockerfile.agenthost-jetson-voice",
    "lucia.InstallerHost",
    "Directory.Build.props",
    "global.json",
]


def compute_image_input_fingerprint(repo_root: Path) -> str:
    digest = hashlib.sha256()
    files = []
    for relative in DEFAULT_FINGERPRINT_PATHS:
        path = repo_root / relative
        if not path.exists():
            raise ValueError(f"Required image input is missing: {relative}")
        files.extend(path.rglob("*") if path.is_dir() else [path])
    for path in sorted(set(files)):
        relative = path.relative_to(repo_root)
        if not path.is_file() or (
            relative.parts[0] == "lucia.InstallerHost"
            and any(part in {"bin", "obj", "__pycache__"} for part in relative.parts)
        ):
            continue
        content = path.read_bytes().replace(b"\r\n", b"\n")
        if path.name == "Dockerfile.agenthost-jetson-voice":
            marker = b"FROM scratch AS appliance-voice-assets\n"
            if marker not in content:
                raise ValueError("Native voice asset build stage is missing.")
            prefix, remainder = content.split(marker, 1)
            content = prefix + marker + re.split(br"(?m)^FROM ", remainder, maxsplit=1)[0]
        if path.name == "appliance.lock":
            # Source prerequisites and app target metadata do not alter OS contents.
            content = b"\n".join(
                line for line in content.splitlines()
                if not line.startswith((b"LUCIA_SOURCE_", b"LUCIA_TARGET_", b"OS_SOURCE_"))
                and line.strip() and not line.lstrip().startswith(b"#")
            )
        digest.update(relative.as_posix().encode() + b"\0" + content + b"\0")
    return digest.hexdigest()


def choose_release_mode(current_fingerprint, published_manifests, *, force_full=False, release_tag=None):
    if re.fullmatch(r"[0-9a-f]{64}", current_fingerprint) is None:
        raise ValueError("The current image fingerprint must be a SHA-256 digest.")
    published = [entry for entry in published_manifests if not entry.get("draft") and not entry.get("prerelease")]
    # The release API supplies newest publication first. Do not sort version strings.
    baseline = next((
        entry for entry in published
        if entry.get("releaseMode") == "full"
        and {"os", "installer"} <= entry.get("channels", {}).keys()
        and re.fullmatch(r"[0-9a-f]{64}", entry.get("imageInputFingerprint", "")) is not None
    ), None)
    existing = next((entry for entry in published if entry.get("tag") == release_tag), None)
    if existing is not None:
        if existing.get("imageInputFingerprint") != current_fingerprint:
            raise ValueError("Published release inputs differ; use a new release tag.")
        mode = existing.get("releaseMode")
        if mode not in {"full", "app-only"} or (force_full and mode != "full"):
            raise ValueError("A published release cannot change its channel set; use a new tag.")
        reason = "preserving the published release mode for an immutable rerun"
    elif force_full:
        mode, reason = "full", "manual force-full override"
    elif baseline is None:
        mode, reason = "full", "no usable published image baseline; a full build is required"
    elif baseline["imageInputFingerprint"] != current_fingerprint:
        mode, reason = "full", "image inputs changed since the last published full release"
    else:
        mode, reason = "app-only", "image inputs match the last published full release"
    return {
        "release_mode": mode,
        "current_fingerprint": current_fingerprint,
        "baseline": baseline["imageInputFingerprint"] if baseline else "",
        "baseline_tag": baseline["tag"] if baseline else "",
        "reason": reason,
    }


def fetch_published_manifests(repository: str) -> list[dict]:
    pages = json.loads(subprocess.check_output(
        ["gh", "api", "--paginate", "--slurp", f"repos/{repository}/releases?per_page=100"],
        text=True,
    ))
    releases = sorted(
        (release for page in pages for release in page
         if not release["draft"] and not release["prerelease"]),
        key=lambda release: release["published_at"], reverse=True,
    )
    manifests = []
    for release in releases:
        asset = next((asset for asset in release["assets"]
                      if asset["name"] == "lucia-appliance-manifest.json"), None)
        if asset is None:
            continue  # Manifest-last publication: interrupted image builds are not baselines.
        manifest = json.loads(subprocess.check_output(
            ["gh", "api", "-H", "Accept: application/octet-stream",
             f"repos/{repository}/releases/assets/{asset['id']}"],
            text=True,
        ))
        if manifest.get("repository") != repository or manifest.get("tag") != release["tag_name"]:
            raise ValueError(f"Published manifest identity mismatch: {release['tag_name']}")
        available = {asset["name"] for asset in release["assets"]}
        required = {"SHA256SUMS", "lucia-appliance-attestations.jsonl"}
        required.update(part["name"] for channel in manifest["channels"].values()
                        for part in channel["parts"])
        if not required <= available:
            raise ValueError(f"Published manifest has missing assets: {release['tag_name']}")
        manifests.append(manifest)
    return manifests


def _read_published_manifests(path: Path) -> list[dict]:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ValueError(f"Cannot read published manifests: {path}") from error
    if not isinstance(data, list) or any(not isinstance(entry, dict) for entry in data):
        raise ValueError("Published manifests must be a JSON array of objects.")
    return data


def main():
    parser = argparse.ArgumentParser(description="Plan releases against published image inputs.")
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[3])
    parser.add_argument("--published-manifests", type=Path)
    parser.add_argument("--repository")
    parser.add_argument("--tag")
    parser.add_argument("--force-full", action="store_true")
    parser.add_argument("--print-fingerprint", action="store_true")
    parser.add_argument("--github-output", type=Path)
    parser.add_argument("--summary", type=Path)
    args = parser.parse_args()
    fingerprint = compute_image_input_fingerprint(args.repo_root)
    if args.print_fingerprint:
        print(fingerprint)
        return
    entries = (_read_published_manifests(args.published_manifests) if args.published_manifests
               else fetch_published_manifests(args.repository) if args.repository else [])
    plan = choose_release_mode(fingerprint, entries, force_full=args.force_full, release_tag=args.tag)
    if args.github_output:
        with args.github_output.open("a", encoding="utf-8") as stream:
            for key, value in plan.items():
                stream.write(f"{key}={value}\n")
    if args.summary:
        with args.summary.open("a", encoding="utf-8") as stream:
            stream.write(
                f"### Appliance release plan\n\nMode: `{plan['release_mode']}`\n\n"
                f"Baseline: `{plan['baseline_tag'] or 'none'}`\n\n"
                f"Image fingerprint: `{fingerprint}`\n\n{plan['reason']}.\n"
            )
    print(json.dumps(plan))


if __name__ == "__main__":
    main()
