#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$script_dir/appliance.lock"

[[ "${#COMPUTE_PACKAGES[@]}" -eq 8 ]]
[[ "$JETSON_BSP_SHA256" =~ ^[0-9a-f]{64}$ ]]
[[ "$JETSON_ROOTFS_SHA256" =~ ^[0-9a-f]{64}$ ]]
! grep -q 'SHA1\|download_sha1\|sha1sum' \
    "$script_dir/build-release-assets.sh" \
    "$script_dir/appliance.lock"
for package in "${COMPUTE_PACKAGES[@]}"; do
    read -r sha256 url <<< "$package"
    [[ "$sha256" =~ ^[0-9a-f]{64}$ ]]
    [[ "$url" == https://repo.download.nvidia.com/jetson/common/pool/*.deb ]]
done

grep -q 'sudo chroot "$root" dpkg --install' \
    "$script_dir/build-release-assets.sh"
! grep -Eq 'apt-get (update|install)' \
    "$script_dir/build-release-assets.sh"
grep -q 'gpasswd --delete lucia-recovery sudo' \
    "$script_dir/build-release-assets.sh"
grep -q 'usermod --shell.*lucia-recovery' \
    "$script_dir/build-release-assets.sh"
grep -q '^FROM scratch AS appliance-voice-assets$' \
    "$script_dir/../../docker/Dockerfile.agenthost-jetson-voice"
grep -q 'docker pull --platform linux/arm64 "$voice_asset_ref"' \
    "$script_dir/build-release-assets.sh"
! grep -q 'docker buildx build' \
    "$script_dir/build-release-assets.sh"
grep -q 'cp -a "$repo_root/plugins/." "$voice_dir/plugins/"' \
    "$script_dir/build-release-assets.sh"
grep -q 'target: appliance-voice-assets' \
    "$script_dir/../../../.github/workflows/jetson-voice-assets.yml"
grep -q 'keep-state: true' \
    "$script_dir/../../../.github/workflows/jetson-voice-assets.yml"
grep -q 'timeout-minutes: 720' \
    "$script_dir/../../../.github/workflows/jetson-voice-assets.yml"
grep -q 'needs: voice-assets' \
    "$script_dir/../../../.github/workflows/appliance-release.yml"
grep -q 'ref:.*inputs.tag.*github.ref' \
    "$script_dir/../../../.github/workflows/appliance-release.yml"
grep -q 'VOICE_ASSET_REF:.*needs.voice-assets.outputs.image_ref' \
    "$script_dir/../../../.github/workflows/appliance-release.yml"

echo "PASS: release rootfs uses pinned packages and restricted recovery"
