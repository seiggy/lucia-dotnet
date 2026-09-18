import hashlib
import io
import os
import shutil
import subprocess
import tarfile
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]


class AppOnlyBuildTests(unittest.TestCase):
    def test_real_build_script_packages_app_without_invoking_image_tools(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            repo, work, output = root / "repo", root / "work", root / "output"
            release = repo / "infra/appliance/release"
            release.mkdir(parents=True)
            for name in ("build-release-assets.sh", "voice-asset-key.sh", "trusted-root.jsonl"):
                shutil.copyfile(REPO / "infra/appliance/release" / name, release / name)
            shutil.copyfile(
                REPO / "infra/appliance/build-native-bundle.sh",
                repo / "infra/appliance/build-native-bundle.sh",
            )
            (repo / "infra/appliance/build-native-bundle.sh").chmod(0o755)
            shutil.copytree(REPO / "infra/appliance/rootfs", repo / "infra/appliance/rootfs")
            (repo / "infra/docker").mkdir()
            shutil.copyfile(
                REPO / "infra/docker/Dockerfile.agenthost-jetson-voice",
                repo / "infra/docker/Dockerfile.agenthost-jetson-voice",
            )
            (repo / "plugins").mkdir()
            (repo / "plugins/example").write_text("plugin")
            downloads = work / "downloads"
            downloads.mkdir(parents=True)

            def archive(name, members):
                path = downloads / name
                with tarfile.open(path, "w:gz") as bundle:
                    for filename in members:
                        data = b"#!/bin/sh\nexit 0\n"
                        entry = tarfile.TarInfo(filename)
                        entry.size, entry.mode = len(data), 0o755
                        bundle.addfile(entry, io.BytesIO(data))
                return hashlib.sha256(path.read_bytes()).hexdigest()

            lock = [
                "JETSON_LINUX_VERSION=36.5.2", "REDIS_VERSION=8.2.9",
                "REDIS_COMMIT=fixture", "REDIS_BUILD_IMAGE=redis-builder",
                "OTELCOL_VERSION=fixture", "REDIS_EXPORTER_VERSION=fixture",
                "GH_CLI_VERSION=fixture",
                "COMPUTE_PACKAGES=()", "RUNTIME_PACKAGES=()",
            ]
            for prefix, name, files in (
                ("REDIS_SOURCE", "redis-fixture.tar.gz", ["source"]),
                ("OTELCOL", "otelcol-contrib_fixture_linux_arm64.tar.gz", ["otelcol-contrib"]),
                ("REDIS_EXPORTER", "redis_exporter-vfixture.linux-arm64.tar.gz", ["exporter/redis_exporter"]),
                ("GH_CLI", "gh_fixture_linux_arm64.tar.gz", ["gh_fixture_linux_arm64/bin/gh"]),
                ("GH_CLI_HOST", "gh_fixture_linux_amd64.tar.gz", ["gh_fixture_linux_amd64/bin/gh"]),
            ):
                lock += [f"{prefix}_URL=https://fixture/{name}",
                         f"{prefix}_SHA256={archive(name, files)}"]
            lock.append("TRUSTED_ROOT_SHA256=" + hashlib.sha256(
                (release / "trusted-root.jsonl").read_bytes()).hexdigest())
            for key in ("REDIS", "CUDA", "CUDNN", "ONNX_RUNTIME", "SHERPA_ONNX"):
                lock.append(f"LUCIA_TARGET_{key}_VERSION=fixture")
            (release / "appliance.lock").write_text("\n".join(lock) + "\n")
            tools = root / "tools"
            tools.mkdir()
            def tool(name, script):
                path = tools / name
                path.write_text("#!/usr/bin/env bash\nset -euo pipefail\n" + script)
                path.chmod(0o755)

            tool("sudo", '[[ "${1:-}" != -n ]] || shift\nexec "$@"\n')
            tool("df", "printf 'Avail\\n999999999\\n'\n")
            tool("curl", "echo 'Unexpected download or BSP access' >&2; exit 90\n")
            for name in ("sgdisk", "mount", "chroot", "e2fsck"):
                tool(name, f"echo 'Forbidden app-only image tool: {name}' >&2; exit 90\n")
            tool("dotnet", r'''
project="$2"
[[ "$project" != *InstallerHost* ]] || exit 90
while [[ "$1" != --output ]]; do shift; done
mkdir -p "$2"
name=lucia.AgentHost
[[ "$project" != *ApplianceManager* ]] || name=lucia.ApplianceManager
printf '#!/bin/sh\nexit 0\n' > "$2/$name"
chmod 755 "$2/$name"
''')
            tool("npm", r'''
mkdir -p "$2/dist"
printf 'dashboard' > "$2/dist/index.html"
''')
            tool("docker", r'''
case "$1" in
pull|rm) ;;
create) printf 'fixture-container\n' ;;
cp)
  mkdir -p "$3"
  if [[ "$2" == *:/native/. ]]; then
    for lib in libonnxruntime.so libonnxruntime_providers_cuda.so libonnxruntime_providers_shared.so libsherpa-onnx-c-api.so; do
      printf 'native' > "$3/$lib"
    done
  else
    printf 'model' > "$3/model.onnx"
  fi ;;
run)
  if [[ "$*" == *redis-builder* ]]; then
    for arg in "$@"; do
      if [[ "$arg" == *:/output ]]; then
        dest="${arg%:/output}"
        mkdir -p "$dest"
        printf '#!/bin/sh\nexit 0\n' > "$dest/redis-server"
        chmod 755 "$dest/redis-server"
      fi
    done
  else
    [[ "$*" == *"@sha256:"* && "$*" == *"ldd"* && "$*" == *"--validate"* ]]
    touch "$TEST_ROOT/runtime-validated"
  fi ;;
*) exit 90 ;;
esac
''')
            result = subprocess.run(
                ["bash", str(release / "build-release-assets.sh"), "--version", "1.5.0",
                 "--release-mode", "app-only", "--work-dir", str(work), "--output-dir", str(output)],
                env={**os.environ, "PATH": f"{tools}:{os.environ['PATH']}", "TEST_ROOT": str(root)},
                text=True, capture_output=True, timeout=60, check=False,
            )
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertTrue((root / "runtime-validated").exists())
            self.assertFalse((work / "bsp").exists())
            self.assertFalse((work / "sd-bsp").exists())
            self.assertEqual(
                [path.name for path in (output / "raw").iterdir()],
                ["lucia-appliance-1.5.0-lucia.tar.zst"],
            )
            listing = subprocess.check_output(
                ["tar", "-I", "zstd", "-tf", str(output / "raw/lucia-appliance-1.5.0-lucia.tar.zst")],
                text=True,
            )
            for entry in ("app/lucia.AgentHost", "manager/lucia.ApplianceManager",
                          "redis/bin/redis-server", "tools/gh", "tools/trusted-root.jsonl"):
                self.assertIn(f"./opt/lucia/releases/1.5.0/{entry}", listing)
            self.assertIn("libonnxruntime.so", listing)
            helper = work / "bundle/usr/libexec/lucia/lucia-update-progress.py"
            self.assertEqual(helper.stat().st_uid, 0)
            self.assertEqual(helper.stat().st_gid, 0)
            self.assertEqual(helper.stat().st_mode & 0o022, 0)


if __name__ == "__main__":
    unittest.main()
