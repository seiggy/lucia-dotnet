# Tasks: Jetson appliance image

**Plan:** [plan.md](plan.md)
**Research:** [research.md](research.md)
**Last updated:** 2026-09-13

## Phase 0: decisions and tracking

- [x] T001 Research JetPack, storage, telemetry, updates, mDNS, and onboarding.
- [x] T002 Fix JetPack 6 as the supported appliance baseline.
- [x] T003 Confirm the first test seam as the native bundle CLI and systemd
  output contract.
- [x] T004 Create this implementation plan and progress log.

## Phase 1: native runtime bundle

- [x] T005 Write a red CLI check for missing required bundle inputs.
- [x] T006 Implement bundle CLI argument validation and usage errors.
- [x] T007 Write a red bundle-layout check using fixture AgentHost, dashboard,
  and Redis inputs.
- [x] T008 Assemble the versioned rootfs overlay.
- [x] T009 Add AgentHost and Redis systemd units.
- [x] T010 Add Redis AOF, SQLite, sysusers, tmpfiles, and environment defaults.
- [x] T011 Validate units with `systemd-analyze verify` on Linux.
- [x] T012 Publish a real `linux-arm64` AgentHost and build a bundle.
- [x] T013 Install the bundle on stock JetPack 6 and verify Redis, SQLite,
  dashboard, health, Wyoming, and CUDA on the Orin.

## Phase 2: microSD to NVMe installer

- [x] T014 Confirm the installer state-machine seam and tests.
- [ ] T015 Detect QSPI compatibility and report the recovery-flash path.
- [ ] T016 Report complete occupied-drive layout without destructive probing.
- [x] T017 Provision A/B OS, Lucia, and persistent data partitions.
- [ ] T018 Exercise interrupted image writing and recovery on physical storage.
- [x] T019 Boot the installed target and retain the SD card as recovery media.

## Phase 3: first-boot network and ownership

- [x] T020 Confirm the network-bootstrap seam and tests.
- [x] T021 Add an atomic first-browser claim for generic DIY images.
- [x] T022 Start the validated client-isolated, non-routed SoftAP and captive
  setup.
- [x] T023 Claim ownership, create the dashboard key, and set the recovery
  account password atomically.
- [x] T024 Join home Wi-Fi through a timed NetworkManager checkpoint.
- [x] T025 Restore setup mode after failed Wi-Fi activation.
- [ ] T026 Set the hostname and publish dashboard and Wyoming Avahi records.
- [ ] T027 Validate USB Ethernet setup and owner-admin SSH recovery on hardware.

## Phase 4: appliance manager

- [x] T028 Confirm the Unix-socket manager seam and tests.
- [x] T029 Add read-only appliance and service status.
- [ ] T030 Add validated hostname and telemetry configuration.
- [ ] T031 Add allowlisted AgentHost, Redis, reboot, and shutdown operations.
- [ ] T032 Add persisted long-operation state and redacted diagnostics.
- [x] T033 Add the authenticated AgentHost appliance adapter.

## Phase 5: optional telemetry and dashboard

- [x] T034 Install Collector and Redis exporter units disabled by preset.
- [ ] T035 Add telemetry validation, endpoint testing, enable, and disable.
- [x] T036 Add appliance capability discovery.
- [ ] T037 Add hostname and telemetry steps to onboarding.
- [ ] T038 Add Appliance overview, updates, telemetry, and diagnostics sections.
- [ ] T039 Add guarded host controls and reconnecting operation progress.

## Phase 6: split update channels

- [x] T040 Complete and attest the Lucia release manifest compatibility fields.
- [x] T041 Stage and atomically apply AgentHost and Redis releases.
- [x] T042 Back up and roll back Redis and SQLite with Lucia releases.
- [x] T043 Define the OS update feed and compatibility manifest.
- [x] T044 Stage Jetson image-based OTA to the inactive slot.
- [ ] T045 Validate the new OS slot and exercise automatic rollback.
- [x] T046 Expose independent discovery and update operations in the dashboard.

## Phase 7: reproducible image pipeline

- [x] T047 Pin and verify JetPack 6 BSP and package inputs.
- [ ] T048 Build the pruned rootfs and microSD installer.
- [ ] T049 Produce checksums, package manifests, SBOM, and recovery media.
- [x] T050 Add CI validation that does not require target hardware.
- [ ] T051 Run flash, cold-boot, power-cut, Wi-Fi, CUDA, update, and rollback
  checks on the supported Orin.
- [x] T052 Add a stable-tag GitHub Release workflow for chunked installer,
  Lucia, and OS channels.

## Current checkpoint

Phase 1 is complete. The Phase 2 physical happy path passed, but QSPI
detection, full occupied-drive reporting, and power-cut recovery remain open.
Lab validation pulled appliance management and telemetry work forward.

## Work log

### 2026-09-13

- Pre-PR review found that rejecting an invalid recovery hash could occur after
  changing the target hostname. Moved the validated password update first and
  added a regression proving invalid recovery data leaves identity files intact.
- Recovered a lost Dashboard key through the existing `DASHBOARD_API_KEY`
  startup override, with owner approval. Removed the temporary override,
  verified authentication after restart, and removed the private temporary
  key file after the owner confirmed it was saved and dashboard access worked.
- Moved the acknowledgment gate ahead of target provisioning, which tests home
  Wi-Fi, for both initial and resumed installations. The final poweroff gate
  remains. The Linux regression and all five installer browser checks pass.
- Updated the saved `lucia-appliance-1.4.2-local-fixed-installer.img` with this
  handoff fix. Its current SHA-256 is
  `dae2841570ae1af6895d59bb1a3300d729b866206a08dc1002ce8ec822a7a622`.
- Hardware installation now completes, manual Wi-Fi connects, and the owner
  can SSH as `lucia-recovery` and use password-protected sudo.
- Fixed three additional runtime blockers on the installed Jetson. GCC 14's
  Redis executable required `GLIBCXX_3.4.32`, curl was absent, and archive-mode
  overlay copying assigned checkout ownership to system directories. The last
  defect made systemd-tmpfiles reject the path to AgentHost's staging directory.
- Rebuilt the pinned Redis source with GCC 11, installed curl/libcurl, restored
  root ownership of system directories, and replayed the existing tmpfiles
  rules. Applied OS package and ownership fixes to both slots without changing
  Wi-Fi or repartitioning. Redis, manager, and AgentHost remain running;
  HTTPS health returns `Healthy`, and CUDA voice warm-up completes.
- Updated the image build to use a pinned compatible compiler, pin curl's
  Ubuntu packages, execute Redis and curl inside the target rootfs, and exclude
  checkout ownership when copying overlays. Native Linux release checks pass.
- A new `lucia-appliance-1.4.2-local-fixed-installer.img` incorporates these
  runtime fixes and unfiltered Wi-Fi discovery. The original downloaded release
  and earlier local recovery image remain separate.
- Reversed the review-era security filter in Wi-Fi discovery. WPA3, open,
  enterprise, WEP, and unreported security types remain visible; duplicate
  SSIDs still use their strongest signal. Removed WPA2-only UI wording.
  This source change was not in the SD image being installed at that point, and
  it does not change the WPA-PSK connection profile.
- Reproduced the empty-scan Wi-Fi dead end in the browser and installer control.
  Initial setup and network retry now accept a typed WPA2-Personal SSID, with
  scan results as suggestions. A blank initial SSID still selects Ethernet.
- Removed scan membership as a configuration prerequisite. SSID byte limits,
  passphrase validation, the provisioning connection check, and setup AP
  recovery remain enforced.
- Passed all five installer browser checks, the dashboard build and targeted
  lint, Linux control/provisioning/bootstrap checks, and desktop/mobile layout
  checks. The rootfs text check passes in Git Bash; WSL encounters the existing
  CRLF in the checked-out captive DNS configuration.
- Read the failed installation's SD card without writing to it. The NVMe image
  write had finished, but `provision.state` was absent and `progress.json`
  recorded a system failure; the chosen recovery hash was still pending.
- Reproduced that failure: data expansion appended `p18` to a stable
  `/dev/disk/by-id/...` link instead of its resolved device. Resolve the device
  first, and run the existing loop test through a symlink so it covers the
  production path.
- The owner approved Bash SSH/console access with password-protected sudo,
  replacing the network-only recovery shell. Direct root SSH remains disabled.
  Apply the recovery password to the installer SD before the NVMe write, and
  retain journals across reboots.
- Built both NVMe rootfs slots locally from the verified release payload.
  Actual ARM64 sshd accepted password login against each slot in disposable
  overlays; sudo policy requires a password. WSL's ARM64 interpreter lacks
  credential-preserving execution, so that local check did not prove sudo
  elevation. The later hardware check recorded above did.
- Built `lucia-appliance-1.4.2-local-recovery-installer.img` locally:
  16,920,870,912 bytes, SHA-256
  `95e7a4497ebf1254207a53f281cf3a32340c9e95c9382f54afce9c80ac0bd726`.
  Its actual ARM64 installer helper applied a test recovery hash in a disposable
  overlay and SSH accepted that password. Final filesystem, partition table,
  embedded payload checksum, captive setup, and root-ownership checks passed.
- The original release image is unchanged. The owner authorized flashing the
  local build to the identified 128 GB SD. The full SD read-back matches the
  image SHA-256 above. Physical boot, service health, Wi-Fi, and sudo elevation
  were checked afterward, with the runtime repairs recorded above.

### 2026-09-12

- Removed the release builder's stale `chmod` for the deleted installer sudoers
  rule. The installer still runs as root and invokes its control helper directly.
- Reproduced the missing-file error with the installer overlay, then passed
  `bash infra/appliance/release/test_build_release_assets.sh` and
  `bash infra/appliance/installer/test-installer-rootfs.sh` after the fix.
- T048 and T051 remain open. The full image build and Jetson checks were not run.

### 2026-09-03

- T040 and T043 passed
  `python3 infra/appliance/release/test_package_release.py` and the offline
  attestation checks in `infra/appliance/release/test_build_release_assets.sh`.
- T041 and T042 passed
  `sudo bash infra/appliance/manager/test-lucia-update.sh`, including
  transactional apply, continuity validation, failed-manager recovery, and
  data rollback.
- T044 passed the loop-device APP, kernel, DTB, slot-selection, and rollback
  checks in `sudo bash infra/appliance/manager/test-lucia-update.sh`.
- T046 passed `npm --prefix lucia-dashboard run build` and
  `npx playwright test --project=chromium e2e/00-appliance.spec.ts`.
- T045 and T051 remain open pending the physical Jetson checks listed above.

### 2026-08-29

- Confirmed the native bundle CLI and systemd output as the first public test
  seam.
- Added `infra/appliance/build-native-bundle.sh` and its shell integration
  check.
- Added AgentHost and Redis units, Redis AOF with `noeviction`, SQLite
  defaults, sysusers, and tmpfiles declarations.
- Passed four public CLI checks with
  `bash infra/appliance/test-build-native-bundle.sh`.
- Passed `systemd-analyze verify` for both units in Ubuntu 22.04.
- Published self-contained `linux-arm64` AgentHost output: 570 files,
  320,954,891 bytes.
- Built the current dashboard successfully.
- Compiled Redis 8.2.9 for ARM64 from official commit
  `0eb14894da51df6d1e4a748e3308b95deb85617d`.
- Built and verified `lucia-native-0.1.0.tar.gz` inside Linux. The archive is
  118,800,345 bytes and preserves the atomic `current` symlink.
- Caught and avoided Cygwin's non-POSIX directory-link behavior by making the
  real bundle on Linux, which is also the required Jetson image-build host.
- Confirmed that the installer will carry every required voice model and GPU
  native library so first setup works offline.
- Added bundle inputs for the validated ARM64 ONNX Runtime, CUDA provider,
  sherpa-onnx, voice models, and plugins. The builder removes CPU native
  duplicates before applying the GPU overlay.
- Built and ARM64-verified `lucia-native-voice-0.1.0.tar.gz`: 971,422,406
  bytes, SHA-256
  `37018DCBEA6A82E2EA403159FC76241A48CA35B5FEAD1009A2318737E66F742A`.
- Connected to `zackw@orin-voice` and confirmed AArch64 Jetson Linux R36.4.7
  with NVMe root storage.
- The first concurrent native run reached CUDA and HTTP startup but the kernel
  killed it under memory pressure while the existing 5.6 GiB voice container
  remained active. The isolated native process had reached about 4.2 GiB.
- With an approved maintenance window, stopped only the existing AgentHost
  container and ran the native bundle on isolated ports.
- Native validation returned `Healthy`, selected `CUDAExecutionProvider`,
  opened Wyoming on `127.0.0.1:10401`, created 12 SQLite files and three Redis
  AOF files, and used 4,113,972 KiB RSS at the checkpoint.
- Cleaned up the native processes and restored the exact original AgentHost
  container to healthy state.
- Phase 1 gate passed. Phase 2 starts with T014.
- Confirmed the Phase 2 seam as the boot-time `lucia-install` CLI, with disk
  behavior tested against loopback images before the Orin NVMe.
- Added loopback checks for blank, mounted, occupied, and undersized media.
  Mounted media is always rejected; occupied media requires confirmation.
- Downloaded and verified the official Jetson Linux R36.5.2 BSP and sample
  rootfs against NVIDIA's published SHA-1 values.
- Installed the exact minimal CUDA 12.6 and cuDNN 9 runtime package versions
  used by the validated voice build.
- Generated NVIDIA's signed no-flash A/B package for P3767-0005, FAB 300,
  chip SKU D5.
- Added `build-loop-image.sh` and `finalize-loop-image.sh` to create and
  validate raw disk images without changing NVIDIA's production NVMe path.
- Produced a 61,203,283,968-byte NVMe image with APP, APP_b, a 6 GiB LUCIA
  partition, and an 18,839,453,184-byte LUCIA_DATA partition.
- Moved versioned application releases and mutable data out of both OS slots.
  Both root filesystems, both persistent filesystems, and the GPT pass offline
  checks.
- Added atomic installer state. A matching interrupted write restarts safely
  from byte zero; unrelated occupied disks remain blocked.
- Streamed the complete compressed image through the real `lucia-install`
  command onto a minimum-size loop disk and mounted all resulting partitions.
- NVMe payload: `lucia-nvme-0.1.1.img.zst`, 6,543,704,382 bytes, SHA-256
  `74eb6d5b959a1763ec78c2e26c07bda556d344ab5c6d12b3b8ea89581137c8c5`.
- Added the first-boot one-shot installer and a loop-aware wrapper around
  NVIDIA's unmodified SD image creator.
- Built and mounted the 16,670,261,248-byte Orin Nano Super SD image. Its
  payload, locked recovery account, executables, enabled unit, and ext4
  filesystem passed validation.
- SD artifact: `lucia-installer-sd-0.1.1.img.zst`, 8,464,827,738 bytes,
  SHA-256
  `e3ef40bd005fbee0100b315073030cd02c70ec106b646d61d25a6604248a12e1`.
- T019 requires separate test storage on the Orin; the active 2 TB lab NVMe
  must not be overwritten.
- The user authorized destructive use of the lab SD and NVMe. The exported lab
  image carries one-time preauthorization for that test only.
- Production installation now boots the SD captive portal before storage
  changes, inventories all eligible drives by stable identity, and binds
  explicit erase approval to the selected device and image digest.

### 2026-08-30

- User flashed the pinned lab SD image and authorized erasing the lab NVMe.
- SD installer wrote all 61,203,283,968 bytes and created APP, APP_b, LUCIA,
  and LUCIA_DATA on the 2 TB NVMe.
- R36.4.7 QSPI booted the R36.5.2 installer successfully. UEFI preferred the
  SD card on the first reboot, so removing the SD card was required.
- NVMe boot passed with `/` on p1, `/opt/lucia` on p17, and
  `/var/lib/lucia` on p18.
- Redis and AgentHost are active. `/health` is `Healthy`, the dashboard
  redirects to setup, CUDA loaded, and Wyoming listens on port 10400.
- Added `appliance-release.yml` for stable tags on the dedicated
  `jetson-image-builder` runner.
- Added pinned BSP and Redis inputs, release build orchestration, GitHub
  artifact attestations, chunked GitHub Release assets, SHA-256 checksums, and
  a manifest uploaded last for updater discovery.
- Replaced live rootfs package resolution with a complete CUDA and cuDNN
  package set pinned by URL and SHA-256 in `appliance.lock`.
- Added a 45-second NetworkManager checkpoint before provisioning either OS
  slot. Failed home Wi-Fi activation restores the setup AP and keeps the
  provisioning request for retry.
- Telemetry enable and disable now update AgentHost's local Collector endpoint
  and restart AgentHost inside the manager's rollback-safe operation.
- Installed AgentHost traffic now uses a per-device, hostname-bound HTTPS
  certificate on port 8099. Plain HTTP is loopback-only on port 8098.
- Real-size packaging produced five installer parts, one Lucia part, and four
  OS parts under GitHub's per-asset limit.
- Added a canonical disk-layout digest with partition starts, filesystem UUIDs,
  labels, types, and mount state. Every initial write now requires matching,
  single-use authorization; interrupted writes revalidate device identity and
  size before resuming.
- Explicitly unignored `infra/appliance/release/**` so tagged checkouts contain
  the workflow's build and packaging scripts.
- Pinned Python 3.12 for manifest packaging and expanded Lucia updates to carry
  every Lucia-owned rootfs file.
- WSL Ubuntu passes shell syntax, manifest tests, systemd verification, and
  `act` workflow discovery. Final focused review reported no blockers.
- Deployed the 0.1.2 onboarding fixes to the lab appliance. Setup now writes
  and reads the same configuration database, and all ten built-in agents seed
  and initialize.
- Built and deployed the 0.1.3 telemetry overlay with the ARM64 Collector and
  Redis exporter. Both remain disabled by default in production bundles.
- Configured the lab Collector for authenticated OTLP over TLS to
  `192.168.0.251:4317`. The Collector is ready, scrapes Redis, and exports
  without authentication or transport retries.
- Added a bundle contract requiring `Restart=always` for AgentHost. A clean
  stop through the existing restart API will now be restarted by systemd,
  which is the first no-power-cycle application update seam.
- Confirmed the appliance manager test seams: authenticated AgentHost HTTP
  endpoints backed by a root-owned Unix-socket manager, plus manager contract
  tests using a fake `systemctl` executable. Destructive integration remains a
  separate real-device check on the lab Jetson.
- Found that the lab image locks `lucia-recovery` after installing its SSH key,
  which also prevents password-based sudo. The captive setup must require the
  owner to set this local recovery password before setup completes.
- Started a separate one-shot repair image for the lab appliance. It installs
  the real manager and updated AgentHost unit on the existing NVMe without
  changing partitions, application data, or the pinned installer image.
- Replaced the repair-only direction with the intended installer flow. Its
  public test seam is the bootstrap-authenticated HTTP interface, backed by the
  existing loop-tested disk writer. Automated tests use controlled process and
  block-device fixtures; the final flow uses real NetworkManager, systemd, and
  NVMe operations on the lab Jetson.
- Fixed the deployment discriminator as `Appliance:Mode` with `Off`,
  `Installer`, and `Installed` values. Docker and other supported hosts remain
  `Off`; appliance-only HTTP routes and UI must not appear there.
- Chose a code-free DIY first claim because a generic Rufus image has no
  secure channel for delivering a unique secret to its owner. The first
  browser on the client-isolated setup AP receives an HttpOnly session cookie;
  other browsers are rejected. Reboot clears an abandoned pre-install claim.
  Manufactured appliances can add a printed per-device code later.
- Added the transient InstallerHost with atomic first-browser claim, captive
  probe redirects, disk and Wi-Fi inventory, short erase phrases, install
  authorization, and target provisioning for hostname, Wi-Fi, and the recovery
  password hash.
- Added the responsive captive portal. Its boot sequence now waits for the
  Lucia logo to load before starting, and the form uses a compact mobile
  progress bar, in-flow theme controls, 44-pixel touch targets, password
  visibility, and a short drive-specific erase phrase.
- Removed runtime Google Fonts requests and bundled the licensed Outfit and DM
  Sans web fonts so the non-routed setup network has deterministic first paint.
- Fixed malformed nested CSS keyframes that caused Edge to discard the boot
  animations and leave the exiting overlay on screen.
- The user completed the full non-destructive preview. Installer claim returned
  200, install authorization returned 202, and the fake control reached
  `installed`.
- Built the separate captive lab image without changing the pinned 0.1.1
  installer. It advertises `Lucia-LAB200`, contains no preauthorized disk, and
  requires the browser to select and confirm storage before writing.
- Updated the embedded NVMe payload to `0.2.0-lab`. Both OS slots carry the
  installed appliance mode and manager units; the shared Lucia release carries
  AgentHost 0.1.3, the CUDA overlay, and the root-owned manager.
- Captive test image:
  `lucia-installer-sd-0.2.0-captive-lab.img`, 16,670,261,248 bytes, SHA-256
  `fae77a912573ae1e119c19360abb2e996a3ddecfce1720b3c8fce006281d071a`.
- Successful installation powers the appliance off instead of rebooting back
  into the SD card. The owner removes the SD card, then powers on the installed
  NVMe appliance.
- Physical AP testing found JetPack uses iptables legacy and lacks the nftables
  base-chain support assumed by the first image. Replaced the nft rules with
  one idempotent forwarding drop and bound InstallerHost to `10.42.0.1`.
- Physical startup also found trimmed self-contained ASP.NET hosts fail in
  Kestrel with an X509 `TypeLoadException`. InstallerHost and ApplianceManager
  now publish untrimmed. The lab SD applies the untrimmed manager after writing
  the NVMe payload.
- Live validation passed: `Lucia-LAB200` remained activated, dnsmasq served
  `10.42.0.10-254`, InstallerHost stayed at zero restarts, and the iPhone opened
  the captive portal.
- The captive flow authorized and wrote the occupied lab NVMe, provisioned both
  OS slots, powered off for SD removal, and booted the installed appliance at
  `192.168.1.239`.
- Installed validation passed: APP, LUCIA, and LUCIA_DATA mounted from NVMe;
  `lucia-home` activated; Redis, AgentHost, and the untrimmed manager were
  active; the manager Unix socket and dashboard ports listened; `/health`
  returned `Healthy`; optional telemetry remained inactive.
- Added persisted install stages for image validation, exact byte-level NVMe
  writing, OS-slot provisioning, storage sync, and poweroff. The portal shows
  GB written and percentage while `dd` copies the 61,203,283,968-byte payload.
- Progress-enabled captive image SHA-256:
  `cd0539bdfea19817942ad884ea0a3e2558bf0ea7d32b17154cae8e43525d4369`.
- Added the appliance-only authenticated dashboard route and Unix-socket
  adapter. Docker and other non-appliance modes do not map or display it.
- Added live appliance identity, Wi-Fi SSID and signal, service state and
  restart controls, guarded Jetson reboot, redacted OTLP configuration, and
  separate Lucia/OS GitHub update discovery.
- Impeccable reviewed the page at 390x844 and 1440x1000. The final review found
  no material usability, responsive, accessibility, or finish blockers.
- Physical management validation passed on `192.168.1.239`: all four services
  reported active, the Collector restart completed through the page, telemetry
  arrived in Grafana, and GitHub discovery correctly ignored the latest release
  because it has no appliance manifest.
- Fixed two live integration failures found during validation: the manager
  systemd sandbox now grants only `/etc/lucia`, `/opt/lucia`, and
  `/var/lib/lucia` as writable paths; AgentHost now preserves manager failures
  instead of re-executing unsafe methods against `/Error` and returning 405.
