# Appliance release assets

`appliance-release.yml` always publishes Lucia. It builds OS and installer images
only when image inputs changed, no usable published image baseline exists, or a
maintainer selects `force-full` on a manual dispatch from the release tag.
The workflow summary records the mode, baseline tag, fingerprint, and reason.

`planner.py` compares against the most recently published full manifest, not the
previous application tag. Unpublished builds and releases without a final manifest
cannot advance that baseline. A GitHub API failure, invalid manifest, or missing
published payload stops planning instead of guessing.

Pre-verifier manifests such as v1.4.0 did not publish an attestation bundle.
The planner checks their declared payloads and checksums but does not require
that later artifact. They cannot provide an image fingerprint, so the first
release without a usable baseline still builds full. New manifests must have
their attestation bundle. Release pagination uses `gh api --paginate --jq` and
does not require the newer `--slurp` option.

Image inputs include rootfs and installer overlays, systemd units, installer and
image-building scripts, installer host source, SDK settings, pinned system packages,
partition layout, and the native voice asset build stages. Application and dashboard
source changes do not rebuild images. In `appliance.lock`, source compatibility
requirements and Lucia target metadata are excluded from the image fingerprint;
OS target metadata, actual system-package pins, and partition identifiers are not.
Changes to those excluded prerequisites still undergo updater compatibility checks.

App-only builds package the application, manager, Redis, verification tools, and
native voice libraries without preparing a BSP/rootfs or creating/compressing a
disk image. ARM64 Redis, manager startup validation, and native library dependency
checks run in the native build's digest-pinned JetPack container. Full builds also
validate Redis and curl inside the assembled Jetson rootfs.

Application and OS channel versions are independent. `os-version` on manual
dispatch sets the full image's OS version; otherwise it defaults to the release
version. An app-only manifest contains no OS or installer channel and never
relabels an old image. App updates do not replace `lucia.env` or OS helpers.

Install a full release containing the new discovery code before relying on
app-only updates from an older updater that requires all channels. Respect the
manifest's source and target runtimes and the OS channel's minimum Lucia version.
For a fresh installation, scan the [release history](https://github.com/seiggy/lucia-dotnet/releases)
for the newest supported manifest with an `installer` channel, rather than assuming
GitHub's latest release contains an image.

Updates report measured progress for the current phase. Multipart downloads share
one total; OS writing uses the uncompressed sizes of the selected rootfs, kernel,
and device tree. Backup, validation, and restart phases are indeterminate.
Reloading recovers the same operation ID. A completed download or write is not a
successful update; health validation must pass. Failures retain their last phase.
Old OS helpers cannot report write progress until replaced through an approved OS
update or repair procedure, even if the application already has the new UI.

Payloads, checksums, and attestations upload before the manifest. A partial release
can be rerun before its manifest is published. Once published, differing assets
are rejected, and reruns retain the existing channel set. Use a new tag to change
published inputs or switch an app-only release to full. No published image is
overwritten by `force-full`.

The full release contains these channels:

| Channel | Purpose |
| --- | --- |
| Installer | Complete microSD image for Rufus and first installation |
| Lucia | AgentHost, dashboard, plugins, GPU native libraries, and Redis |
| OS | Jetson rootfs, kernel, and device tree for inactive-slot updates |

The captive setup network is an open, client-isolated, non-routed first-boot
network. The first browser to select **Begin setup** claims the session. Keep
the appliance physically controlled during setup.

The temporary installer host runs as root and invokes `lucia-installer-control`
directly. Its image does not include or require an installer sudoers rule.

The Wi-Fi scan lists detected network names without filtering their security
type. You can also enter a network name when the scan is empty. Leave the field
blank to use Ethernet only. Listing a network does not add support for its
authentication method; provisioning currently creates a WPA-PSK profile.
The installer checks the connection during provisioning and restores the setup
access point afterward.
Before testing home Wi-Fi, the installer waits for the owner to save and
acknowledge the Dashboard key. The same gate applies when provisioning resumes.

The installed dashboard listens at `https://HOSTNAME.local:8099` with a
per-device certificate generated during setup. The first browser must accept
that local certificate. Plain HTTP is bound only to loopback on port 8098.

The `lucia-recovery` account has a Bash login shell and password-protected sudo.
Use the recovery password chosen during setup with
`ssh -t lucia-recovery@HOSTNAME.local`. Direct root SSH login is disabled.
The installer enables this password on the SD card before writing the NVMe,
then applies it to both installed OS slots during provisioning. If installation
fails, keep the SD inserted and use this account to inspect
`journalctl -b -u lucia-firstboot-install`. Journals persist across reboots.

If the Dashboard key is lost, an OS administrator can use the existing
`DASHBOARD_API_KEY` startup override to replace it without resetting the
configuration database. This revokes prior keys named Dashboard. Remove the
temporary override after the replacement has been saved and verified.

These are full component payloads, not binary deltas. Full payloads are larger,
but they are deterministic, recoverable, and do not require every user to have
the same prior version.

`appliance.lock` pins CUDA, cuDNN, curl, and libcurl runtime packages by direct
URL and SHA-256. The rootfs build installs that local package set without
reading live Ubuntu or NVIDIA package indexes. Curl is required by the
appliance manager's Unix-socket health check and update helpers.

Redis builds with GCC 11 on Debian Bullseye so its libc and libstdc++
requirements stay compatible with JetPack's Ubuntu 22.04 runtime. The builder
runs the resulting ARM64 Redis executable and curl inside the actual Jetson
rootfs before packaging. A newer build host must not silently raise the target
runtime requirements.

Rootfs overlays preserve modes and links, but never checkout ownership.
System directories such as `/var` and `/var/lib` must remain root-owned;
otherwise systemd-tmpfiles refuses to create the service directories beneath
them. The assembled installer image verifies these owners.

## Discovery

The updater requests:

```text
https://api.github.com/repos/seiggy/lucia-dotnet/releases?per_page=100&page=1
```

It ignores a release until the release contains
`lucia-appliance-manifest.json`. The workflow uploads that manifest last, after
all payload parts, checksums, and GitHub artifact attestations exist.
After an administrator confirms an update, the dashboard submits the reviewed
tag and AgentHost fetches that exact release through the GitHub tags API.

The workflow also publishes `lucia-appliance-attestations.jsonl`. Installed
appliances verify the manifest and every downloaded part offline with the
official GitHub CLI, the workflow identity
`seiggy/lucia-dotnet/.github/workflows/appliance-release.yml`, and a Sigstore
trusted root embedded in the currently installed release. The manager refuses
to write a release until those checks and the manifest hashes all pass.

Each manifest channel contains:

- complete compressed payload size and SHA-256;
- ordered part names, sizes, hashes, and release URLs;
- board, architecture, layout, and minimum disk compatibility;
- source prerequisites and target Jetson Linux, Redis, CUDA, cuDNN, ONNX
  Runtime, and sherpa-onnx versions;
- the release notes URL and per-channel reboot requirement.

The updater downloads parts in manifest order, verifies every part's GitHub
attestation and hash, then verifies the complete compressed stream against the
digest in the attested manifest before writing it.

Update discovery pages through stable releases from newest to oldest and selects
the first one compatible with the appliance's active runtime. This keeps bridge
releases available after a long offline period.

Lucia updates stop AgentHost and Redis, back up configuration, SQLite, and Redis
data, install the new version under `/opt/lucia/releases`, and atomically switch
`/opt/lucia/current`. Rollback restores both the previous release link and its
data backup. Redis configuration lives under `/var/lib/lucia/redis`, so both OS
slots use the same versioned application configuration. A rollback keeps the
current data until the restored release passes the same persistence and CUDA
checks. If validation fails, the updater returns to the current release and
keeps the rollback backup. A Lucia update remains recoverable until the
restarted appliance manager binds its socket and finalizes the transaction.

Lucia updates and rollback restore `lucia-redis-exporter` after Redis restarts
only when its systemd unit is enabled. Disabled telemetry remains off.
The manager also restores an enabled exporter at startup, allowing the first
application upgrade from an older updater to recover it without requiring an
OS update or re-enabling telemetry. Failure recovery uses the same rule.
The exporter is optional telemetry: if it fails to start, the updater and
manager log a warning and do not fail or roll back the Lucia update.

Update-state writes flush both the file and its containing directory. The
managed updater uses libc directory APIs so this durability step works on
ARM64 as well as x86-64; Linux open-flag values are not portable between them.
The ARM64 updaters shipped in 1.4.2 and 1.4.3 used an x86-64 flag and can fail
with `Invalid argument` before staging begins. Those installations need a
one-time repair of both AgentHost and the manager before the dashboard can
install the corrected release. A replacement payload alone cannot repair the
updater that must install it.

Install the Lucia update before its matching OS update when the manifest's
`minimumLuciaVersion` requires it. The OS channel remains incompatible until
that application version is installed.

OS updates stream the selected raw images directly to the inactive `APP`,
kernel, and device-tree partitions. NVIDIA rootfs A/B selects the new slot for
the next boot. Before switching slots, the updater restores the device hostname,
recovery password hash, and provisioned Wi-Fi connection in the new root
filesystem. `lucia-os-update-validation.service` checks NetworkManager, Redis,
AgentHost, and the local health endpoint after boot. A failed check selects the
previous slot and reboots. Intentional rollback runs the same checks on the
previous slot and returns to the current slot if that validation fails.

### RootFS A/B prerequisites and recovery

An `APP_b` partition is not proof of working rootfs redundancy. Image generation
uses `ROOTFS_AB=1` with `--no-flash`; the SD installer writes the resulting NVMe
image, not the device's QSPI configuration. It reports unverified firmware without
blocking the hardware-tested offline installer.

The shared `lucia-rootfs-ab-check` reads NVIDIA's native capability result.
Exit code 0 from `is-rootfs-ab-enabled` means disabled; 1 and 2 mean enabled on
slots A and B. Other results and disagreement with `get-current-slot` block OS
updates. The full preflight also requires matching bootloader/rootfs slots,
the supported extlinux UEFI boot mode,
the current mounted root and its kernel PARTUUID to agree, and all six root,
kernel, and DTB partitions to belong to the same disk. It never enables firmware
features automatically.

The manager reports a blocking reason to the dashboard and rechecks before
accepting OS operations. The privileged updater checks again before writing.
Older managers or missing recovery helpers leave OS updates blocked while
Lucia-only updates remain available. Existing installations need the matching
root-owned updater, validator, `lucia-rootfs-ab-check`, and
`lucia-rebind-rootfs.py` helpers installed through an approved recovery procedure.
The running and fallback OS must both contain the corrected recovery logic.
An application update does not replace `/usr/libexec/lucia` by itself.

After writing a verified image, the updater changes its extlinux and root fstab
references to the actual inactive partition's PARTUUID. It does not change the
device's partition IDs. Directory symlinks and missing boot files are rejected
before activation. Partition-based kernel boot and GRUB are not accepted by this
extlinux-specific updater.

Post-boot validation repeats the full layout check before health checks or
recovery reboots. A healthy application alone cannot prove that Linux mounted
the requested OS slot. Boot validation uses the shipped `nvbootctrl verify`
command only after layout, Lucia, and network health pass. It runs before
NVIDIA's default boot-validation service.
Recovery persists a maximum of two additional reboot requests per operation.
If the requested slot never becomes active, readiness cannot be verified, or
the budget is exhausted, it records a terminal failure rather than rebooting
forever. The GUI retains the error; a manual power cycle is not the default fix.

For an existing device whose firmware has redundancy disabled:

1. Keep the running OS intact. Back up application data, configuration, recovery
   access, GPT metadata, and boot configuration before any firmware work.
2. Prepare the board-matched Jetson Linux BSP using NVIDIA's external-NVMe
   RootFS A/B flashing procedure. Inspect its flash plan before running it.
   This repository does not yet provide a hardware-verified firmware-only
   conversion that preserves an existing installation. Do not toggle EFI bytes
   or execute a generic full-disk flash command as a substitute.
3. Arrange USB Force Recovery and serial or HDMI console access. Execute only
   the reviewed provisioning plan with explicit owner approval.
4. Verify A-to-B and B-to-A boots, actual root PARTUUIDs, warm and cold boot
   behavior, health validation, and deliberately failed-update rollback.
   Preserve user data and confirm that recovery stops with an actionable error.

The software regressions cover disabled/unknown capability, bounded retries,
device-specific UUID rebinding, API admission, and GUI messages. They do not
prove firmware provisioning or real slot failover. That hardware acceptance
remains tracked in #275.
The mounted-image verifier runs the image's checker against deterministic
NVIDIA-tool responses for enabled slots A/B, disabled redundancy, and unsupported
results. It checks both the status and warning behavior without reading the
x86-64 build host's firmware. Missing root or target PARTUUID metadata returns a
specific diagnostic rather than an unexplained command failure.

Images older than this updater cannot bootstrap it from the dashboard. Upgrade
those devices once by reinstalling or manually deploying a release that
contains the verifier.

GitHub Release assets are limited to 2 GiB per file. The packager uses
1.9-billion-byte parts so installer and OS images stay within that limit.

## Runner

The complete appliance build requires a dedicated self-hosted runner with these
labels:

```text
self-hosted
Linux
X64
jetson-image-builder
```

The runner must have:

- native x86-64 Ubuntu;
- at least 200 GiB free under `RUNNER_TEMP`;
- passwordless `sudo`;
- Docker with Buildx;
- `qemu-user-static` with `/usr/bin/qemu-aarch64-static` for the Jetson rootfs;
- GitHub CLI;
- .NET 10, Node 22, and Python 3.12, installed by the workflow;
- direct internet access to NVIDIA, Ubuntu, NuGet, npm, GitHub, and GHCR.

The release downloads the pinned Jetson voice asset image from GHCR and
extracts its native libraries and models into the Lucia payload. Docker is
used only on the build runner; the installed appliance runs native services.
`.github/workflows/jetson-voice-assets.yml` rebuilds that image only when its
pinned Dockerfile inputs change. Appliance releases invoke that workflow first
and consume the exact returned image digest. The isolated voice build runs
natively on GitHub's `ubuntu-24.04-arm` runner and uses the Actions cache for
completed BuildKit layers. The first uncached native build completed in 46
minutes; the equivalent x86-64 QEMU build was still compiling after three
hours.

The complete image build remains unsuitable for a standard GitHub-hosted runner
because the two Jetson rootfs trees, signed flash package, raw images, and
compressed outputs exceed its disk allowance.

## Triggers

Stable `vMAJOR.MINOR.PATCH` tags trigger the workflow. The existing Squad
release workflow creates the GitHub Release while the appliance build runs.
The appliance workflow waits for that release before uploading.

Manual runs require an existing stable tag and GitHub Release.

## Local checks

```bash
python3 infra/appliance/release/test_package_release.py
bash infra/appliance/release/test_build_release_assets.sh
bash -n infra/appliance/release/build-release-assets.sh
act -l -W .github/workflows/jetson-voice-assets.yml
act -l -W .github/workflows/appliance-release.yml
```

The complete image build still requires the dedicated Linux runner and roughly
200 GiB of scratch space. The GitHub-hosted voice job allows up to six hours;
the self-hosted packaging job allows up to 12 hours.
