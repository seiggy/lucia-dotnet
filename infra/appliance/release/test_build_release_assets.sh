#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$script_dir/appliance.lock"
voice_dockerfile="$script_dir/../../docker/Dockerfile.agenthost-jetson-voice"
voice_key_script="$script_dir/voice-asset-key.sh"
workflow_dir="$script_dir/../../../.github/workflows"

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
    "$voice_dockerfile"
[[ "$(grep -c 'Acquire::Retries "5"' "$voice_dockerfile")" -eq 2 ]]
[[ "$(grep -c 'APT::Update::Error-Mode "any"' "$voice_dockerfile")" -eq 2 ]]
grep -q 'docker pull --platform linux/arm64 "$voice_asset_ref"' \
    "$script_dir/build-release-assets.sh"
! grep -q 'docker buildx build' \
    "$script_dir/build-release-assets.sh"
grep -q 'voice-asset-key.sh' \
    "$script_dir/build-release-assets.sh" \
    "$workflow_dir/jetson-voice-assets.yml"
grep -q 'cp -a "$repo_root/plugins/." "$voice_dir/plugins/"' \
    "$script_dir/build-release-assets.sh"
grep -q 'target: appliance-voice-assets' \
    "$workflow_dir/jetson-voice-assets.yml"
grep -q 'docker/build-push-action@53b7df96c91f9c12dcc8a07bcb9ccacbed38856a # v7.3.0' \
    "$workflow_dir/jetson-voice-assets.yml"
grep -q 'runs-on: ubuntu-24.04-arm' \
    "$workflow_dir/jetson-voice-assets.yml"
if grep -q 'docker/setup-qemu-action' \
    "$workflow_dir/jetson-voice-assets.yml"; then
    echo "Native ARM64 workflow still enables QEMU" >&2
    exit 1
fi
if grep -q 'benchmark' "$workflow_dir/jetson-voice-assets.yml"; then
    echo "Production voice workflow still contains benchmark-only behavior" >&2
    exit 1
fi
grep -q '^  group: jetson-voice-assets$' \
    "$workflow_dir/jetson-voice-assets.yml"
grep -q 'ref=$VOICE_ASSET_IMAGE:sha-$hash' \
    "$workflow_dir/jetson-voice-assets.yml"
grep -q 'cache-from: type=gha,scope=jetson-voice-assets' \
    "$workflow_dir/jetson-voice-assets.yml"
grep -q 'cache-to: type=gha,scope=jetson-voice-assets,mode=max' \
    "$workflow_dir/jetson-voice-assets.yml"
grep -q 'timeout-minutes: 720' \
    "$workflow_dir/jetson-voice-assets.yml"
grep -q '^  validate-release-tag:' \
    "$workflow_dir/appliance-release.yml"
grep -q 'needs: validate-release-tag' \
    "$workflow_dir/appliance-release.yml"
grep -q 'needs: \[validate-release-tag, voice-assets\]' \
    "$workflow_dir/appliance-release.yml"
grep -Fq 'runs-on: [self-hosted, Linux, X64, jetson-image-builder]' \
    "$workflow_dir/appliance-release.yml"
grep -q 'ref:.*inputs.tag.*github.ref' \
    "$workflow_dir/appliance-release.yml"
grep -q 'VOICE_ASSET_REF:.*needs.voice-assets.outputs.image_ref' \
    "$workflow_dir/appliance-release.yml"
sed -n '15,20p' "$workflow_dir/appliance-release.yml" \
    | grep -q '^  packages: read$'
grep -q 'docker image rm "$VOICE_ASSET_REF"' \
    "$workflow_dir/appliance-release.yml"

voice_fixture="$(mktemp)"
trap 'rm -f "$voice_fixture"' EXIT
cp "$voice_dockerfile" "$voice_fixture"
voice_key="$(bash "$voice_key_script" "$voice_fixture")"
sed -i 's/node:22-alpine/node:22-alpine3.21/' "$voice_fixture"
[[ "$(bash "$voice_key_script" "$voice_fixture")" == "$voice_key" ]]
sed -i 's/ARG ORT_VERSION=1.23.2/ARG ORT_VERSION=1.23.3/' "$voice_fixture"
[[ "$(bash "$voice_key_script" "$voice_fixture")" != "$voice_key" ]]

echo "PASS: release rootfs uses pinned packages and restricted recovery"
