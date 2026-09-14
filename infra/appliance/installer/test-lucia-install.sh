#!/usr/bin/env bash

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INSTALLER="$SCRIPT_DIR/lucia-install"
WORK="$(mktemp -d)"
LOOP_DEVICE=""
MOUNT_POINT=""

cleanup() {
    if [[ -n "$MOUNT_POINT" ]]; then
        umount "$MOUNT_POINT" 2>/dev/null || true
    fi
    if [[ -n "$LOOP_DEVICE" ]]; then
        losetup --detach "$LOOP_DEVICE" 2>/dev/null || true
    fi
    rm -rf "$WORK"
}
trap cleanup EXIT

pass_count=0
fail_count=0

pass() {
    printf 'PASS: %s\n' "$1"
    pass_count=$((pass_count + 1))
}

fail() {
    printf 'FAIL: %s\n' "$1"
    fail_count=$((fail_count + 1))
}

write_authorization() {
    local device="$1"
    local image_sha256="$2"
    local authorization_file="$3"
    local plan

    plan="$("$INSTALLER" plan --device "$device")"
    {
        printf 'status=approved\n'
        printf 'device=%s\n' "$device"
        printf 'device_identity=%s\n' \
            "$(printf '%s\n' "$plan" | sed -n 's/^device_identity=//p')"
        printf 'device_size_bytes=%s\n' \
            "$(printf '%s\n' "$plan" | sed -n 's/^size_bytes=//p')"
        printf 'layout_sha256=%s\n' \
            "$(printf '%s\n' "$plan" | sed -n 's/^layout_sha256=//p')"
        printf 'image_sha256=%s\n' "$image_sha256"
    } > "$authorization_file"
}

test_blank_disk_is_installable() {
    local image="$WORK/blank.img"
    local identity
    local output
    local status

    truncate --size 70G "$image"
    LOOP_DEVICE="$(losetup --find --show "$image")"

    output="$("$INSTALLER" plan --device "$LOOP_DEVICE" 2>&1)"
    status=$?
    identity="$(printf '%s\n' "$output" | sed -n 's/^device_identity=//p')"

    if [[ "$status" -eq 0 \
        && "$output" == *"classification=blank"* \
        && "$output" == *"action=install"* \
        && "$identity" =~ ^[0-9a-f]{64}$ ]]; then
        pass "blank disk is installable"
    else
        fail "blank disk is installable (status=$status output=$output)"
    fi

    losetup --detach "$LOOP_DEVICE"
    LOOP_DEVICE=""
}

test_mounted_disk_is_protected() {
    local image="$WORK/mounted.img"
    local output
    local status

    truncate --size 70G "$image"
    LOOP_DEVICE="$(losetup --find --show "$image")"
    mkfs.ext4 -q "$LOOP_DEVICE"
    MOUNT_POINT="$WORK/mounted"
    mkdir "$MOUNT_POINT"
    mount "$LOOP_DEVICE" "$MOUNT_POINT"

    output="$("$INSTALLER" plan --device "$LOOP_DEVICE" 2>&1)"
    status=$?

    if [[ "$status" -eq 0 \
        && "$output" == *"classification=protected"* \
        && "$output" == *"action=reject"* ]]; then
        pass "mounted disk is protected"
    else
        fail "mounted disk is protected (status=$status output=$output)"
    fi

    umount "$MOUNT_POINT"
    MOUNT_POINT=""
    losetup --detach "$LOOP_DEVICE"
    LOOP_DEVICE=""
}

test_unmounted_disk_with_data_requires_confirmation() {
    local image="$WORK/occupied.img"
    local output
    local status

    truncate --size 70G "$image"
    LOOP_DEVICE="$(losetup --find --show "$image")"
    mkfs.ext4 -q "$LOOP_DEVICE"

    output="$("$INSTALLER" plan --device "$LOOP_DEVICE" 2>&1)"
    status=$?

    if [[ "$status" -eq 0 \
        && "$output" == *"classification=occupied"* \
        && "$output" == *"action=confirmation-required"* ]]; then
        pass "unmounted disk with data requires confirmation"
    else
        fail "unmounted disk with data requires confirmation (status=$status output=$output)"
    fi

    losetup --detach "$LOOP_DEVICE"
    LOOP_DEVICE=""
}

test_small_disk_is_rejected() {
    local image="$WORK/small.img"
    local output
    local status

    truncate --size 32G "$image"
    LOOP_DEVICE="$(losetup --find --show "$image")"

    output="$("$INSTALLER" plan --device "$LOOP_DEVICE" 2>&1)"
    status=$?

    if [[ "$status" -eq 0 \
        && "$output" == *"classification=too-small"* \
        && "$output" == *"action=reject"* ]]; then
        pass "small disk is rejected"
    else
        fail "small disk is rejected (status=$status output=$output)"
    fi

    losetup --detach "$LOOP_DEVICE"
    LOOP_DEVICE=""
}

test_nvidia_minimum_disk_size_is_installable() {
    local image="$WORK/nvidia-minimum.img"
    local output
    local status

    truncate --size 61203283968 "$image"
    LOOP_DEVICE="$(losetup --find --show "$image")"

    output="$("$INSTALLER" plan --device "$LOOP_DEVICE" 2>&1)"
    status=$?

    if [[ "$status" -eq 0 \
        && "$output" == *"classification=blank"* \
        && "$output" == *"action=install"* ]]; then
        pass "NVIDIA minimum disk size is installable"
    else
        fail "NVIDIA minimum disk size is installable (status=$status output=$output)"
    fi

    losetup --detach "$LOOP_DEVICE"
    LOOP_DEVICE=""
}

test_verified_image_is_written_to_blank_disk() {
    local disk_image="$WORK/install-target.img"
    local authorization_file="$WORK/install-target.authorization"
    local payload="$WORK/nvme.img"
    local state_file="$WORK/install-target.state"
    local expected_hash
    local output
    local status

    truncate --size 70G "$disk_image"
    LOOP_DEVICE="$(losetup --find --show "$disk_image")"
    printf 'LUCIA-NVME-IMAGE-v1\n' > "$payload"
    truncate --size 1M "$payload"
    expected_hash="$(sha256sum "$payload" | awk '{print $1}')"
    write_authorization "$LOOP_DEVICE" "$expected_hash" "$authorization_file"

    output="$("$INSTALLER" install \
        --device "$LOOP_DEVICE" \
        --image "$payload" \
        --sha256 "$expected_hash" \
        --state-file "$state_file" \
        --authorization-file "$authorization_file" 2>&1)"
    status=$?

    if [[ "$status" -eq 0 \
        && "$output" == *"result=installed"* \
        && "$(dd if="$LOOP_DEVICE" bs=20 count=1 status=none)" == "LUCIA-NVME-IMAGE-v1" ]]; then
        pass "verified image is written to blank disk"
    else
        fail "verified image is written to blank disk (status=$status output=$output)"
    fi

    losetup --detach "$LOOP_DEVICE"
    LOOP_DEVICE=""
}

test_verified_zstd_image_is_streamed_to_blank_disk() {
    local disk_image="$WORK/install-zstd-target.img"
    local authorization_file="$WORK/install-zstd-target.authorization"
    local payload="$WORK/nvme-zstd.img"
    local compressed_payload="$payload.zst"
    local expected_hash
    local output
    local status
    local state_file="$WORK/install-zstd-target.state"

    truncate --size 70G "$disk_image"
    LOOP_DEVICE="$(losetup --find --show "$disk_image")"
    printf 'LUCIA-NVME-ZSTD-v1\n' > "$payload"
    truncate --size 1M "$payload"
    zstd -q -o "$compressed_payload" "$payload"
    expected_hash="$(sha256sum "$compressed_payload" | awk '{print $1}')"
    write_authorization "$LOOP_DEVICE" "$expected_hash" "$authorization_file"

    output="$("$INSTALLER" install \
        --device "$LOOP_DEVICE" \
        --image "$compressed_payload" \
        --sha256 "$expected_hash" \
        --state-file "$state_file" \
        --authorization-file "$authorization_file" 2>&1)"
    status=$?

    if [[ "$status" -eq 0 \
        && "$output" == *"result=installed"* \
        && "$(dd if="$LOOP_DEVICE" bs=18 count=1 status=none)" == "LUCIA-NVME-ZSTD-v1" ]]; then
        pass "verified Zstandard image is streamed to blank disk"
    else
        fail "verified Zstandard image is streamed to blank disk (status=$status output=$output)"
    fi

    losetup --detach "$LOOP_DEVICE"
    LOOP_DEVICE=""
}

test_installation_state_is_persisted() {
    local disk_image="$WORK/state-target.img"
    local authorization_file="$WORK/state-target.authorization"
    local payload="$WORK/state.img"
    local state_file="$WORK/install.state"
    local expected_hash
    local output
    local status

    truncate --size 70G "$disk_image"
    LOOP_DEVICE="$(losetup --find --show "$disk_image")"
    printf 'LUCIA-STATE-v1\n' > "$payload"
    truncate --size 1M "$payload"
    expected_hash="$(sha256sum "$payload" | awk '{print $1}')"
    write_authorization "$LOOP_DEVICE" "$expected_hash" "$authorization_file"

    output="$("$INSTALLER" install \
        --device "$LOOP_DEVICE" \
        --image "$payload" \
        --sha256 "$expected_hash" \
        --state-file "$state_file" \
        --authorization-file "$authorization_file" 2>&1)"
    status=$?

    if [[ "$status" -eq 0 \
        && "$output" == *"result=installed"* \
        && -f "$state_file" \
        && "$(grep '^status=' "$state_file")" == "status=installed" \
        && "$(grep '^image_sha256=' "$state_file")" == "image_sha256=$expected_hash" \
        && "$(grep -o '\"stage\":\"[^\"]*\"' "$WORK/progress.json")" == '"stage":"image-written"' \
        && "$(grep -o '\"bytesWritten\":[0-9]*' "$WORK/progress.json")" == '"bytesWritten":1048576' \
        && "$(grep -o '\"totalBytes\":[0-9]*' "$WORK/progress.json")" == '"totalBytes":1048576' ]]; then
        pass "installation state is persisted"
    else
        fail "installation state is persisted (status=$status output=$output)"
    fi

    losetup --detach "$LOOP_DEVICE"
    LOOP_DEVICE=""
}

test_interrupted_installation_can_restart_safely() {
    local initial_classification="$1"
    local disk_image="$WORK/resume-${initial_classification}-target.img"
    local payload="$WORK/resume-${initial_classification}.img"
    local state_file="$WORK/resume-${initial_classification}.state"
    local device_identity
    local device_size
    local expected_hash
    local output
    local plan
    local status

    truncate --size 70G "$disk_image"
    LOOP_DEVICE="$(losetup --find --show "$disk_image")"
    if [[ "$initial_classification" == "occupied" ]]; then
        mkfs.ext4 -q "$LOOP_DEVICE"
    fi
    printf 'LUCIA-RESUME-v1\n' > "$payload"
    truncate --size 1M "$payload"
    expected_hash="$(sha256sum "$payload" | awk '{print $1}')"
    plan="$("$INSTALLER" plan --device "$LOOP_DEVICE")"
    device_identity="$(printf '%s\n' "$plan" | sed -n 's/^device_identity=//p')"
    device_size="$(printf '%s\n' "$plan" | sed -n 's/^size_bytes=//p')"
    {
        printf 'status=writing\n'
        printf 'device=%s\n' "$LOOP_DEVICE"
        printf 'device_identity=%s\n' "$device_identity"
        printf 'device_size_bytes=%s\n' "$device_size"
        printf 'image=%s\n' "$payload"
        printf 'image_sha256=%s\n' "$expected_hash"
    } > "$state_file"

    output="$("$INSTALLER" install \
        --device "$LOOP_DEVICE" \
        --image "$payload" \
        --sha256 "$expected_hash" \
        --state-file "$state_file" 2>&1)"
    status=$?

    if [[ "$status" -eq 0 \
        && "$output" == *"result=installed"* \
        && "$(grep '^status=' "$state_file")" == "status=installed" \
        && "$(dd if="$LOOP_DEVICE" bs=15 count=1 status=none)" == "LUCIA-RESUME-v1" ]]; then
        pass "$initial_classification interrupted installation can restart safely"
    else
        fail "$initial_classification interrupted installation can restart safely (status=$status output=$output)"
    fi

    losetup --detach "$LOOP_DEVICE"
    LOOP_DEVICE=""
}

test_matching_authorization_can_erase_occupied_disk() {
    local authorization_file="$WORK/erase.authorization"
    local device_identity
    local device_size
    local disk_image="$WORK/authorized-target.img"
    local expected_hash
    local layout_sha256
    local output
    local payload="$WORK/authorized.img"
    local plan
    local state_file="$WORK/authorized.state"
    local status

    truncate --size 70G "$disk_image"
    LOOP_DEVICE="$(losetup --find --show "$disk_image")"
    mkfs.ext4 -q "$LOOP_DEVICE"
    printf 'PRIOR-DATA' \
        | dd \
            of="$LOOP_DEVICE" \
            bs=1 \
            seek=$((69 * 1024 * 1024 * 1024)) \
            conv=notrunc \
            status=none
    printf 'LUCIA-AUTHORIZED-v1\n' > "$payload"
    truncate --size 1M "$payload"
    expected_hash="$(sha256sum "$payload" | awk '{print $1}')"
    udevadm settle
    plan="$("$INSTALLER" plan --device "$LOOP_DEVICE")"
    device_identity="$(printf '%s\n' "$plan" | sed -n 's/^device_identity=//p')"
    device_size="$(printf '%s\n' "$plan" | sed -n 's/^size_bytes=//p')"
    layout_sha256="$(printf '%s\n' "$plan" | sed -n 's/^layout_sha256=//p')"
    {
        printf 'status=approved\n'
        printf 'device=%s\n' "$LOOP_DEVICE"
        printf 'device_identity=%s\n' "$device_identity"
        printf 'device_size_bytes=%s\n' "$device_size"
        printf 'layout_sha256=%s\n' "$layout_sha256"
        printf 'image_sha256=%s\n' "$expected_hash"
    } > "$authorization_file"

    output="$("$INSTALLER" install \
        --device "$LOOP_DEVICE" \
        --image "$payload" \
        --sha256 "$expected_hash" \
        --state-file "$state_file" \
        --authorization-file "$authorization_file" 2>&1)"
    status=$?

    if [[ "$status" -eq 0 \
        && "$output" == *"result=installed"* \
        && ! -e "$authorization_file" \
        && "$(dd if="$LOOP_DEVICE" bs=10 skip=$((69 * 1024 * 1024 * 1024 / 10)) count=1 status=none | tr -d '\000')" == "" \
        && "$(dd if="$LOOP_DEVICE" bs=19 count=1 status=none)" == "LUCIA-AUTHORIZED-v1" ]]; then
        pass "matching authorization can erase occupied disk"
    else
        fail "matching authorization can erase occupied disk (status=$status output=$output)"
    fi

    losetup --detach "$LOOP_DEVICE"
    LOOP_DEVICE=""
}

test_blank_disk_requires_authorization() {
    local disk_image="$WORK/unapproved-blank.img"
    local expected_hash
    local output
    local payload="$WORK/unapproved-blank-payload.img"
    local state_file="$WORK/unapproved-blank.state"
    local status

    truncate --size 70G "$disk_image"
    LOOP_DEVICE="$(losetup --find --show "$disk_image")"
    printf 'UNAPPROVED-WRITE\n' > "$payload"
    truncate --size 1M "$payload"
    expected_hash="$(sha256sum "$payload" | awk '{print $1}')"

    output="$("$INSTALLER" install \
        --device "$LOOP_DEVICE" \
        --image "$payload" \
        --sha256 "$expected_hash" \
        --state-file "$state_file" 2>&1)"
    status=$?

    if [[ "$status" -eq 1 \
        && "$output" == *"install refused"* \
        && "$(dd if="$LOOP_DEVICE" bs=16 count=1 status=none | tr -d '\000')" == "" ]]; then
        pass "blank disk requires authorization"
    else
        fail "blank disk requires authorization (status=$status output=$output)"
    fi

    losetup --detach "$LOOP_DEVICE"
    LOOP_DEVICE=""
}

test_stale_layout_authorization_is_rejected() {
    local authorization_file="$WORK/stale.authorization"
    local device_identity
    local device_size
    local disk_image="$WORK/stale-target.img"
    local expected_hash
    local layout_sha256
    local output
    local payload="$WORK/stale-payload.img"
    local plan
    local state_file="$WORK/stale.state"
    local status

    truncate --size 70G "$disk_image"
    LOOP_DEVICE="$(losetup --find --show "$disk_image")"
    mkfs.ext4 -q -L BEFORE "$LOOP_DEVICE"
    printf 'STALE-AUTHORIZATION\n' > "$payload"
    truncate --size 1M "$payload"
    expected_hash="$(sha256sum "$payload" | awk '{print $1}')"
    plan="$("$INSTALLER" plan --device "$LOOP_DEVICE")"
    device_identity="$(printf '%s\n' "$plan" | sed -n 's/^device_identity=//p')"
    device_size="$(printf '%s\n' "$plan" | sed -n 's/^size_bytes=//p')"
    layout_sha256="$(printf '%s\n' "$plan" | sed -n 's/^layout_sha256=//p')"
    {
        printf 'status=approved\n'
        printf 'device=%s\n' "$LOOP_DEVICE"
        printf 'device_identity=%s\n' "$device_identity"
        printf 'device_size_bytes=%s\n' "$device_size"
        printf 'layout_sha256=%s\n' "$layout_sha256"
        printf 'image_sha256=%s\n' "$expected_hash"
    } > "$authorization_file"
    e2label "$LOOP_DEVICE" AFTER

    output="$("$INSTALLER" install \
        --device "$LOOP_DEVICE" \
        --image "$payload" \
        --sha256 "$expected_hash" \
        --state-file "$state_file" \
        --authorization-file "$authorization_file" 2>&1)"
    status=$?

    if [[ "$status" -eq 1 \
        && "$output" == *"install refused"* \
        && "$(e2label "$LOOP_DEVICE")" == "AFTER" ]]; then
        pass "stale layout authorization is rejected"
    else
        fail "stale layout authorization is rejected (status=$status output=$output)"
    fi

    losetup --detach "$LOOP_DEVICE"
    LOOP_DEVICE=""
}

test_blank_disk_is_installable
test_mounted_disk_is_protected
test_unmounted_disk_with_data_requires_confirmation
test_small_disk_is_rejected
test_nvidia_minimum_disk_size_is_installable
test_verified_image_is_written_to_blank_disk
test_verified_zstd_image_is_streamed_to_blank_disk
test_installation_state_is_persisted
test_interrupted_installation_can_restart_safely blank
test_interrupted_installation_can_restart_safely occupied
test_matching_authorization_can_erase_occupied_disk
test_blank_disk_requires_authorization
test_stale_layout_authorization_is_rejected

printf '\n%d passed, %d failed\n' "$pass_count" "$fail_count"
[[ "$fail_count" -eq 0 ]]
