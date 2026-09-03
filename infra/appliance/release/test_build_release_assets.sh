#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$script_dir/appliance.lock"
voice_dockerfile="$script_dir/../../docker/Dockerfile.agenthost-jetson-voice"
voice_key_script="$script_dir/voice-asset-key.sh"
workflow_dir="$script_dir/../../../.github/workflows"

[[ "${#COMPUTE_PACKAGES[@]}" -eq 8 ]]
[[ "$GH_CLI_SHA256" =~ ^[0-9a-f]{64}$ ]]
[[ "$GH_CLI_HOST_SHA256" =~ ^[0-9a-f]{64}$ ]]
[[ "$TRUSTED_ROOT_SHA256" =~ ^[0-9a-f]{64}$ ]]
[[ "$JETSON_BSP_SHA256" =~ ^[0-9a-f]{64}$ ]]
[[ "$JETSON_ROOTFS_SHA256" =~ ^[0-9a-f]{64}$ ]]
[[ "$LUCIA_SOURCE_JETSON_LINUX_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]
[[ -n "$LUCIA_SOURCE_REDIS_VERSION" ]]
[[ -n "$LUCIA_SOURCE_CUDA_VERSION" ]]
[[ -n "$LUCIA_SOURCE_CUDNN_VERSION" ]]
[[ -n "$LUCIA_SOURCE_ONNX_RUNTIME_VERSION" ]]
[[ -n "$LUCIA_SOURCE_SHERPA_ONNX_VERSION" ]]
[[ "$OS_SOURCE_JETSON_LINUX_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]
[[ -n "$OS_SOURCE_REDIS_VERSION" ]]
[[ -n "$OS_SOURCE_CUDA_VERSION" ]]
[[ -n "$OS_SOURCE_CUDNN_VERSION" ]]
[[ -n "$OS_SOURCE_ONNX_RUNTIME_VERSION" ]]
[[ -n "$OS_SOURCE_SHERPA_ONNX_VERSION" ]]
[[ -n "$LUCIA_TARGET_JETSON_LINUX_VERSION" ]]
[[ "$LUCIA_TARGET_REDIS_VERSION" == "$REDIS_VERSION" ]]
[[ -n "$LUCIA_TARGET_CUDA_VERSION" ]]
[[ -n "$LUCIA_TARGET_CUDNN_VERSION" ]]
[[ -n "$LUCIA_TARGET_ONNX_RUNTIME_VERSION" ]]
[[ -n "$LUCIA_TARGET_SHERPA_ONNX_VERSION" ]]
[[ "$OS_TARGET_JETSON_LINUX_VERSION" == "$JETSON_LINUX_VERSION" ]]
[[ "$OS_TARGET_REDIS_VERSION" == "$REDIS_VERSION" ]]
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
grep -Fq '  group: jetson-voice-assets' \
    "$workflow_dir/jetson-voice-assets.yml"
grep -Fq 'ref=$VOICE_ASSET_IMAGE:sha-$hash' \
    "$workflow_dir/jetson-voice-assets.yml"
grep -q 'cache-from: type=gha,scope=jetson-voice-assets' \
    "$workflow_dir/jetson-voice-assets.yml"
grep -q 'cache-to: type=gha,scope=jetson-voice-assets,mode=max' \
    "$workflow_dir/jetson-voice-assets.yml"
grep -q 'timeout-minutes: 360' \
    "$workflow_dir/jetson-voice-assets.yml"
grep -q '^  validate-release-tag:' \
    "$workflow_dir/appliance-release.yml"
grep -q 'needs: validate-release-tag' \
    "$workflow_dir/appliance-release.yml"
grep -q 'needs: \[validate-release-tag, voice-assets\]' \
    "$workflow_dir/appliance-release.yml"
grep -Fq 'runs-on: [self-hosted, Linux, X64, jetson-image-builder]' \
    "$workflow_dir/appliance-release.yml"
grep -q 'timeout-minutes: 720' \
    "$workflow_dir/appliance-release.yml"
grep -q 'ref:.*inputs.tag.*github.ref' \
    "$workflow_dir/appliance-release.yml"
grep -q 'VOICE_ASSET_REF:.*needs.voice-assets.outputs.image_ref' \
    "$workflow_dir/appliance-release.yml"
grep -Fq '  packages: read' "$workflow_dir/appliance-release.yml"
grep -q 'docker image rm "$VOICE_ASSET_REF"' \
    "$workflow_dir/appliance-release.yml"
grep -q 'bundle-path' "$workflow_dir/appliance-release.yml"
grep -q 'lucia-appliance-attestations.jsonl' \
    "$workflow_dir/appliance-release.yml" \
    "$script_dir/package_release.py"
grep -q 'Verify exported offline attestation bundle' \
    "$workflow_dir/appliance-release.yml"
grep -q '"$verifier" attestation verify "$subject"' \
    "$workflow_dir/appliance-release.yml"
grep -q -- '--custom-trusted-root "$trusted_root"' \
    "$workflow_dir/appliance-release.yml"
grep -q -- '--source-ref "refs/tags/$RELEASE_TAG"' \
    "$workflow_dir/appliance-release.yml"
grep -q 'Manual appliance releases must run from the selected release tag' \
    "$workflow_dir/appliance-release.yml"
grep -q -- '--source-ref "refs/tags/$selected_tag"' \
    "$script_dir/../rootfs/usr/libexec/lucia/lucia-update"
grep -q 'pinned attestation trusted root failed verification' \
    "$script_dir/build-release-assets.sh"
printf '%s  %s\n' "$TRUSTED_ROOT_SHA256" "$script_dir/trusted-root.jsonl" \
    | sha256sum --check --status
grep -q -- '--gh-cli' "$script_dir/build-release-assets.sh"
grep -q -- '--lucia-source-jetson-linux "$LUCIA_SOURCE_JETSON_LINUX_VERSION"' \
    "$workflow_dir/appliance-release.yml"
grep -q 'for channel in manifest\["channels"\].values()' \
    "$workflow_dir/appliance-release.yml"
grep -q -- '--os-source-sherpa-onnx "$OS_SOURCE_SHERPA_ONNX_VERSION"' \
    "$workflow_dir/appliance-release.yml"
grep -q -- '--lucia-target-jetson-linux "$LUCIA_TARGET_JETSON_LINUX_VERSION"' \
    "$workflow_dir/appliance-release.yml"
grep -q -- '--os-target-sherpa-onnx "$OS_TARGET_SHERPA_ONNX_VERSION"' \
    "$workflow_dir/appliance-release.yml"
grep -q 'appliance-runtime.json' \
    "$script_dir/build-release-assets.sh"
grep -q '"$LUCIA_TARGET_REDIS_VERSION"' \
    "$script_dir/build-release-assets.sh"
grep -q '"$OS_TARGET_REDIS_VERSION"' \
    "$script_dir/build-release-assets.sh"
grep -q 'lucia-os-update-validation.service' \
    "$script_dir/build-release-assets.sh"
grep -q 'extract_partition_image APP .*system.img' \
    "$script_dir/build-release-assets.sh"
grep -q 'extract_partition_image APP_b .*system.img_b' \
    "$script_dir/build-release-assets.sh"
grep -q 'sgdisk --print.*lucia-nvme' \
    "$script_dir/build-release-assets.sh"
grep -q -- '-C "$ota_dir"' "$script_dir/build-release-assets.sh"
grep -q 'test-lucia-update.sh' \
    "$workflow_dir/appliance-release.yml" \
    "$workflow_dir/appliance-pr.yml"

voice_fixture="$(mktemp)"
trap 'rm -f "$voice_fixture"' EXIT
cp "$voice_dockerfile" "$voice_fixture"
voice_key="$(bash "$voice_key_script" "$voice_fixture")"
sed -i 's/node:22-alpine/node:22-alpine3.21/' "$voice_fixture"
[[ "$(bash "$voice_key_script" "$voice_fixture")" == "$voice_key" ]]
sed -i 's/ARG ORT_VERSION=1.23.2/ARG ORT_VERSION=1.23.3/' "$voice_fixture"
[[ "$(bash "$voice_key_script" "$voice_fixture")" != "$voice_key" ]]

echo "PASS: release rootfs uses pinned packages and restricted recovery"
