# Implementation plan: Jetson appliance image

**Date:** 2026-08-29
**Target:** Jetson Orin Nano 8GB on JetPack 6
**Research:** [research.md](research.md)
**Progress log:** [tasks.md](tasks.md)

## Goal

Ship a reproducible microSD installer that provisions a blank NVMe drive and
boots Lucia as an appliance. The installed system runs AgentHost, Redis, and
SQLite natively, exposes secure captive onboarding, advertises itself over
mDNS, and separates Lucia updates from Jetson OS updates.

## Fixed decisions

- JetPack 6 remains the supported base.
- The user selects the installation target by stable device identity.
- Every target write requires explicit dashboard confirmation.
- The selected drive holds the A/B OS slots, Lucia releases, and persistent
  data.
- Redis AOF preserves active task and session records.
- SQLite stores configuration, traces, schedules, and archives.
- Telemetry is installed but disabled until the owner opts in.
- A client-isolated, non-routed Wi-Fi SoftAP and captive web setup are the
  primary DIY headless flow. The first browser atomically claims setup.
- Captive setup requires the owner to choose a recovery password before the
  installer can write storage.
- USB Ethernet and restricted `nmtui` are recovery paths.
- AgentHost stays unprivileged.
- A root-owned appliance manager performs allowlisted host operations.
- `Appliance:Mode` is `Off`, `Installer`, or `Installed`. Non-appliance hosts
  default to `Off` and do not map installer or appliance-management routes.
- Lucia and OS updates use separate signed feeds and rollback paths.

## Modules and seams

| Module | Interface and seam | Responsibility |
|---|---|---|
| Native bundle builder | `infra/appliance/build-native-bundle.sh` CLI | Assemble a versioned rootfs overlay from published AgentHost, dashboard, and Redis inputs |
| SD installer | First-browser-claim HTTP on the isolated setup network | Validate QSPI, collect setup inputs, authorize one NVMe identity, provision the target, and power off |
| Network bootstrap | Appliance manager network operations | Run the secure SoftAP, commit Wi-Fi through a timed checkpoint, and restore setup access on failure |
| Appliance manager | Root-owned Unix socket | Telemetry, updates, service control, diagnostics, hostname, and host power |
| Appliance HTTP module | Authenticated appliance routes | Translate dashboard requests into allowlisted manager operations |
| Appliance dashboard | Capability-gated React route | Onboarding, status, updates, telemetry, diagnostics, and host controls |
| Image builder | Linux build CLI and flash manifest | Produce pinned JetPack 6 installer and recovery artifacts |

The external seam for each module is its test surface. Internal helpers remain
private until a second adapter proves a seam is real.

## Delivery order

### Phase 1: native runtime bundle

Build and validate a rootfs overlay with:

- self-contained `linux-arm64` AgentHost;
- compiled dashboard;
- pinned ARM64 Redis binary;
- Redis AOF with `noeviction`;
- SQLite and appliance environment defaults;
- hardened AgentHost and Redis systemd units;
- sysusers and tmpfiles declarations.

**Gate:** the public bundle CLI produces the documented layout from fixture
inputs, rejects invalid input, and all units pass `systemd-analyze verify` on
Linux.

### Phase 2: microSD to NVMe installer

Add QSPI compatibility checks, safe blank-drive detection, NVMe partitioning,
A/B rootfs installation, persistent Lucia/data partitions, progress state, and
recovery.

**Gate:** the dashboard inventories eligible drives without fixed device names;
no media is written before explicit confirmation; occupied media shows its
existing layout; authorization cannot move to a different device; interrupted
installation resumes or returns to a known recovery state.

### Phase 3: first-boot network and ownership

Add a client-isolated, non-routed DIY SoftAP, captive setup, atomic first
browser claim, owner-selected recovery password, Wi-Fi checkpoint rollback,
hostname selection, Avahi records, USB Ethernet, and restricted `nmtui`.

**Gate:** a phone can claim a new appliance and join it to Wi-Fi without
Ethernet, display, or keyboard; failed Wi-Fi returns to setup mode.

### Phase 4: appliance manager

Add the root-owned Unix-socket manager and authenticated AgentHost adapter.
Start with read-only status, then telemetry, service restart, reboot,
diagnostics, and update operations.

**Gate:** AgentHost has no general root or sudo access; the manager rejects
unknown operations and invalid arguments.

### Phase 5: optional telemetry and dashboard

Install disabled Collector and Redis exporter units. Extend onboarding and add
the capability-gated Appliance route for status, telemetry, diagnostics,
hostname, and guarded host actions.

**Gate:** a fresh image sends no telemetry; enabling and disabling telemetry is
observable, reversible, and never returns stored credentials to the browser.

### Phase 6: split update channels

Add signed Lucia bundles for AgentHost and Redis, then Jetson image-based OTA
for the OS. Enforce compatibility in both directions and preserve application
and data partitions across OS slot changes.

**Gate:** failed Lucia validation restores the prior release; failed OS boot
returns to the prior slot; the dashboard reconnects to persisted operation
state.

### Phase 7: reproducible flash pipeline

Pin BSP inputs, packages, models, native GPU libraries, Redis, .NET, and
dashboard assets. Produce manifests, checksums, SBOMs, installer media, OTA
payloads, and recovery instructions.

**Gate:** two clean Linux builds use the same declared inputs and produce
equivalent manifests; physical-device tests pass on the supported module and
carrier.

## Test strategy

Work in vertical red-green slices through each confirmed public seam. The first
confirmed seam is the native bundle CLI and its output contract. Confirm later
seams before their first test.

Host-independent checks run locally and in CI. Linux-only checks use
`systemd-analyze`, shell integration tests, and loopback disk images. QSPI,
NVMe, Wi-Fi AP mode, CUDA, power interruption, and A/B rollback require the
physical Orin test device.

## Work log rule

Update [tasks.md](tasks.md) when a task starts, completes, or becomes blocked.
Record the command or physical check that satisfied each phase gate. Keep
hardware-dependent tasks open until they run on the target device.
