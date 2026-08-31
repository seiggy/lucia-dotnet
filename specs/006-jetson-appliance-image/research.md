# Research: Jetson appliance image

**Date:** 2026-08-29
**Status:** Feasible, proof of concept recommended

## Verdict

A flashable Lucia appliance is feasible on the **Jetson Orin Nano 8GB**.
The original Jetson Nano is not a viable target for the current voice build.

The shortest safe path is:

1. Keep the first appliance on the repository-tested JetPack 6 and CUDA 12.6
   family.
2. Build a customized NVIDIA Jetson Linux root file system.
3. Run AgentHost, Redis, and a narrow appliance-management helper under
   systemd.
4. Use Redis AOF for active task and session records.
5. Use SQLite for configuration, traces, schedules, and archives.
6. Install the OpenTelemetry Collector and Redis exporter, disabled by default.
7. Put the root file system, application releases, and persistent data on
   NVMe.
8. Give Lucia and the operating system separate signed update channels.
9. Expose telemetry, updates, diagnostics, and host restart controls in the
   dashboard only when appliance mode is enabled.
10. Advertise the appliance over mDNS as `lucia.local` by default, with a
    validated hostname override during installation.
11. Use a temporary Wi-Fi access point and the existing web setup as the
    primary headless onboarding path.

This removes Docker, PostgreSQL, MongoDB, and the PostgreSQL exporter. The
Collector and Redis exporter remain available without consuming runtime
resources until the owner enables telemetry. The image should reduce RAM use,
disk duplication, boot time, and operational complexity. It should not
materially improve CUDA inference speed because containers use the host kernel
and GPU rather than emulating them.

JetPack 7.2.1 is now the current NVIDIA stack for Orin and adds official Yocto
support. It is deferred for Lucia because the target hardware has shown
firmware and operating-system instability in the field. Moving to it would
also change the tested base from Ubuntu 22.04, kernel 5.15, and CUDA 12.6 to
Ubuntu 24.04, kernel 6.8, and CUDA 13.2. Reconsider it only after NVIDIA's Orin
releases are stable enough for appliance use and the custom ONNX Runtime and
sherpa-onnx libraries have been rebuilt and validated.

## Hardware scope

"Jetson Nano" and "Jetson Orin Nano" are different products.

The original Jetson Nano ended on JetPack 4.6.6 and Jetson Linux 32.7.6, with
Ubuntu 18.04, kernel 4.9, and CUDA 10.2. NVIDIA identifies that as the final
JetPack 4 and R32 release. Lucia targets .NET 10 and its current voice build
expects the Orin `sm_87` GPU, CUDA 12.6, cuDNN 9, and custom ARM64 CUDA native
libraries. Supporting the original Nano would require a separate legacy build
and an unsupported .NET operating-system combination.

Sources:

- [NVIDIA Jetson Linux 32.7.6 and JetPack 4 end-of-life notice](https://developer.nvidia.com/embedded/linux-tegra-r3276)
- [Microsoft .NET 10 supported Ubuntu versions](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu-decision)
- [Current Lucia Jetson voice build](../../infra/docker/Dockerfile.agenthost-jetson-voice)

The rest of this document refers to the Jetson Orin Nano Developer Kit with an
8GB P3767 module. A commercial appliance should eventually use a production
module and validated carrier board. NVIDIA states that developer kits are for
development and production modules are for deployed products.

Source: [NVIDIA Jetson Linux Developer Guide](https://docs.nvidia.com/jetson/archives/r39.2.1/DeveloperGuide/index.html)

## Baseline image choices

| Baseline | Fit | Cost | Decision |
|---|---|---:|---|
| Customized NVIDIA Jetson Linux rootfs | Best match for Lucia's current GPU build | Medium | Use for the first appliance |
| OE4T `meta-tegra` on JetPack 7.2.1 | Smallest and most reproducible final image | High | Defer until JetPack 7 is stable on the target |
| Ubuntu Core 22 for Jetson | Built-in confinement, OTA, rollback, and disk encryption | High | Viable later, not the first build |
| Canonical Ubuntu Server for Jetson | Supported conventional Ubuntu | Medium | Use only if Canonical support is a requirement |
| Generic ARM64 distro | Poor fit for board firmware and NVIDIA drivers | High risk | Reject |

### Customized NVIDIA Jetson Linux

NVIDIA's BSP already supplies the board firmware, UEFI, kernel, drivers,
flashing tools, and Ubuntu sample root file system. The supported build flow is
to populate `Linux_for_Tegra/rootfs`, run `apply_binaries.sh`, customize the
rootfs, and flash it with `flash.sh` or `l4t_initrd_flash.sh`.

This is the least risky option because Lucia's existing ARM64 ONNX Runtime and
sherpa-onnx build is based on JetPack 6 and CUDA 12.6. Use the current tested
R36.4.7 device package manifest for the first native proof of concept. For a
reproducible release, move to a publicly downloadable BSP such as Jetson Linux
36.5.2 and rebuild the native voice libraries against that exact package set.
The repository's R36.4.7 label should be treated as a tested device baseline,
not as proof that NVIDIA published a standalone R36.4.7 BSP.

NVIDIA JetPack 6.2.3 includes Jetson Linux 36.5.2, Ubuntu 22.04, kernel 5.15,
CUDA 12.6, cuDNN 9.3, and TensorRT 10.3. NVIDIA provides runtime package sets,
so the image does not need the desktop or development toolchain.

Sources:

- [JetPack 6.2.3](https://developer.nvidia.com/embedded/jetpack-sdk-623)
- [Jetson Linux 36.5.2](https://developer.nvidia.com/embedded/jetson-linux-r3652)
- [NVIDIA flashing support](https://docs.nvidia.com/jetson/archives/r36.5.2/DeveloperGuide/SD/FlashingSupport.html)

### Yocto and `meta-tegra`

JetPack 7.2 officially supports the Yocto Project. The current OE4T
`meta-tegra` layer tracks JetPack 7.2.1 and Jetson Linux 39.2.1, supports the
Orin Nano Developer Kit, and produces a machine-specific tegraflash bundle.
This is the cleanest route to a small, read-only, reproducible production
image.

It is not the shortest first step. Lucia would need recipes for the .NET
publish output, dashboard, models, service units, Redis configuration, and the
custom ONNX Runtime and sherpa-onnx CUDA build. JetPack 7 also requires a GPU
compatibility pass because the current Lucia binaries target the JetPack 6
compute stack.

Sources:

- [NVIDIA JetPack 7 overview and official Yocto support](https://developer.nvidia.com/embedded/jetpack)
- [Jetson Linux 39.2.1 support for Orin](https://docs.nvidia.com/jetson/archives/r39.2.1/DeveloperGuide/index.html)
- [OE4T `meta-tegra`](https://github.com/OE4T/meta-tegra)
- [OE4T flashing documentation](https://github.com/OE4T/meta-tegra/blob/master/docs/Flashing.md)
- [OE4T redundant rootfs support](https://github.com/OE4T/meta-tegra/blob/master/docs/Redundant-Rootfs-A-B-Partition-Support.md)

### Ubuntu Core

Canonical released Ubuntu Core 22 for Jetson in May 2026 and lists the Orin
Nano Developer Kit as tested and certified. It includes strict snap
confinement, OTA updates, secure boot support, and automatic full-disk
encryption where the hardware supports it.

Lucia would need a confined ARM64 snap, writable data under snap-owned paths,
GPU content interfaces, device access for audio and multicast discovery, and
validation that Roslyn script plugins work under confinement. Those are real
product changes. Ubuntu Core is viable, but not the lazy path to the first
image.

Source: [Ubuntu Core 22 for Jetson release notes](https://canonical-ubuntu-for-jetson.readthedocs-hosted.com/latest/core/release-note-jammy/)

## Lucia appliance layout

The minimum appliance has three enabled services:

1. `lucia-redis.service`
2. `lucia-agenthost.service`
3. `lucia-appliance-manager.service`

The image also installs two optional services:

1. `lucia-otelcol.service`
2. `lucia-redis-exporter.service`

Both telemetry services are disabled by the image's systemd preset. They start
only after the owner enables telemetry during onboarding or later from the
Appliance page.

AgentHost runs in standalone mode, embeds the current in-process agents, and
serves the compiled React dashboard. Aspire AppHost remains a development tool
and is not installed.

Use these settings:

```ini
DataProvider__Cache=Redis
DataProvider__Store=SQLite
DataProvider__SqlitePath=/var/lib/lucia/db/lucia.db
ConnectionStrings__redis=127.0.0.1:6379
Deployment__Mode=standalone
Appliance__Mode=Installed
PluginDirectory=/var/lib/lucia/plugins
```

The current code derives three SQLite files for configuration, traces, and
tasks. Redis stores active A2A task records and conversation sessions with a
24-hour TTL. The existing timeout worker reloads `InputRequired` tasks after a
restart. `Submitted` and `Working` records survive, but automatic continuation
of their interrupted execution remains separate backlog work.

Repository references:

- [AgentHost provider selection](../../lucia.AgentHost/Program.cs)
- [SQLite provider registration](../../lucia.Data/Extensions/ServiceCollectionExtensions.cs)
- [Redis task persistence](../../lucia.Agents/Integration/RedisTaskStore.cs)
- [Restart-aware input timeout worker](../../lucia.Agents/Services/InputRequiredTimeoutService.cs)

### Persistent paths

Use NVMe with replaceable A/B operating-system slots, a persistent application
partition mounted at `/opt/lucia`, and persistent data partitions.

```text
/opt/lucia/
  releases/
  current
/var/lib/lucia/
  db/
  models/
  plugins/
  voice-clips/
/var/lib/redis/
/var/lib/lucia/otelcol/
/etc/lucia/
  lucia.env
  telemetry.env
```

Keep application releases in versioned, read-only directories such as
`/opt/lucia/releases/<version>` with `/opt/lucia/current` as an atomic symlink.
Keep databases, models, plugins, credentials, and update state outside the
replaceable rootfs. Keeping `/opt/lucia` outside `APP` and `APP_b` prevents an
operating-system rollback from silently changing the installed Lucia version.

The current SQLite implementation uses WAL with `synchronous=NORMAL`. That is
a reasonable default for one process, but the latest committed writes can be
lost during abrupt power failure. Use NVMe, back up before updates, and switch
to `FULL` only if power-cut testing shows the added durability is worth the
write latency.

### Redis durability

Enable AOF on the persistent partition. Redis documents that the default
`appendfsync everysec` policy can lose up to one second of writes during an
abrupt failure while retaining good write performance. Graceful service and
device restarts flush normally. Use `appendfsync always` only if testing proves
that the smaller loss window justifies its latency.

Do not use the current `allkeys-lru` policy for durable task records. Use
`noeviction`, measure the dataset, and set a memory limit that leaves room for
AOF rewrite buffers. A failed write is visible; silent eviction of active task
state is not.

Sources:

- [Redis persistence](https://redis.io/docs/latest/operate/oss_and_stack/management/persistence/)
- [Redis eviction policies](https://redis.io/docs/latest/develop/reference/eviction/)

### Optional telemetry

The optional telemetry bundle should reuse the existing Jetson pipeline:

- AgentHost exports OTLP to a Collector receiver bound to localhost.
- The Collector gathers host metrics directly with `hostmetrics`.
- `redis-exporter` exposes Redis metrics on localhost for the Collector's
  Prometheus receiver.
- The Collector exports to the owner's OTLP endpoint.

There is no PostgreSQL service in the appliance, so there is no PostgreSQL
exporter. The native Collector does not need the container configuration's
`/hostfs` mount and should read the host directly.

The onboarding telemetry step is optional and defaults to off. Enabling it
should require:

1. signal level, either metrics only or traces and metrics;
2. OTLP transport and endpoint;
3. optional authorization headers;
4. TLS trust settings when a private certificate authority is used;
5. a successful configuration validation and connection test.

Until the owner confirms the form, AgentHost uses `Observability__Mode=Off`,
the two telemetry units remain disabled, and no telemetry leaves the device.
When enabled, the appliance manager writes a root-readable
`/etc/lucia/telemetry.env` atomically, validates the Collector configuration,
enables `redis-exporter`, enables the Collector, and restarts AgentHost with
its OTLP endpoint set to the local Collector. Disabling telemetry reverses
those steps and leaves the last configuration available for later use.

The dashboard must never return stored authorization headers to the browser.
It may report whether credentials are configured and allow replacement.

Repository references:

- [Current Jetson Collector pipeline](../../infra/docker/otel-collector-jetson.yaml)
- [Current Redis exporter configuration](../../infra/docker/docker-compose.jetson-voice.yml)
- [AgentHost telemetry modes](../../lucia.ServiceDefaults/Extensions.cs)

## .NET and native GPU deployment

Publish AgentHost for `linux-arm64` as self-contained and untrimmed:

```bash
dotnet publish lucia.AgentHost/lucia.AgentHost.csproj \
  -c Release \
  -r linux-arm64 \
  --self-contained true \
  -p:UseAppHost=true \
  -p:PublishTrimmed=false \
  -p:PublishReadyToRun=true \
  -p:CopilotSkipCliDownload=true
```

Self-contained publishing removes the target's dependency on an installed
.NET runtime. Lucia dynamically loads DLL plugins and executes Roslyn C#
scripts, so Native AOT and trimming are not safe first targets. Keep
ReadyToRun because the existing Jetson build uses it, then benchmark it.
Microsoft notes that ReadyToRun can improve startup while making assemblies
two to three times larger and sometimes increasing working set.

Sources:

- [.NET publishing modes](https://learn.microsoft.com/en-us/dotnet/core/deploying/)
- [.NET ReadyToRun tradeoffs](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run)
- [.NET Native AOT limitations](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [Lucia dynamic plugin loader](../../lucia.Agents/PluginFramework/PluginLoader.cs)
- [Lucia Roslyn script host](../../lucia.Agents/PluginFramework/PluginScriptHost.cs)

On a native JetPack rootfs, use the system CUDA and cuDNN packages rather than
copying another set into `/opt/lucia`. Keep Lucia's exact ARM64 ONNX Runtime
and sherpa-onnx libraries beside the application. Rebuild and test them
whenever Jetson Linux, CUDA, cuDNN, ONNX Runtime, or sherpa changes.

Release validation must prove both application health and
`CUDAExecutionProvider` selection. `/health` alone can pass while inference
has fallen back to CPU.

## Flashing and image shape

A Jetson appliance is not one universal raw ARM image. The boot firmware,
board configuration, partition layout, module SKU, and carrier board matter.
For the Orin Nano Developer Kit, ship a versioned flash bundle with a manifest
and checksums.

The JetPack 6 flow is:

1. Download and checksum the BSP and sample rootfs.
2. Populate `Linux_for_Tegra/rootfs`.
3. Run `apply_binaries.sh`.
4. Install the selected JetPack runtime packages, Redis, Lucia publish output,
   dashboard, models, service units, and defaults into the rootfs.
5. Create the unprivileged `lucia` user and persistent directory ownership.
6. Generate the flash artifacts.
7. Put the board in Force Recovery Mode.
8. Flash QSPI and NVMe with `l4t_initrd_flash.sh`.

NVIDIA documents `l4t_initrd_flash.sh` for internal and external media,
including NVMe. The host needs native Linux and physical USB access to the
board. Treat Windows and virtualized USB flashing as unsupported build paths,
even though Windows can cross-publish the .NET ARM64 application.

Source: [NVIDIA flashing support](https://docs.nvidia.com/jetson/archives/r36.5.2/DeveloperGuide/SD/FlashingSupport.html)

## Updates, rollback, and recovery

The dashboard presents two independent update channels.

### Lucia updates

A Lucia release updates AgentHost, the dashboard, plugins shipped with the
image, custom ARM64 GPU libraries, and the Redis service binary and
configuration. Redis lives in the Lucia application partition rather than
being updated by the operating-system package manager. Its data remains under
`/var/lib/redis`.

Each signed release manifest includes:

- Lucia and Redis versions;
- SHA-256 hashes and download sizes;
- compatible appliance OS versions;
- expected JetPack, CUDA, cuDNN, ONNX Runtime, and sherpa versions;
- data and configuration schema versions;
- release notes and whether a host reboot is required.

The update flow is:

1. Check the Lucia feed and show the available version without installing it.
2. Download and verify the signed manifest and payload.
3. Reject an update that is incompatible with the installed OS or GPU stack.
4. Extract into `/opt/lucia/releases/<version>`.
5. Back up SQLite and Redis together.
6. Stop AgentHost, then stop Redis if that release changes Redis.
7. Atomically switch `/opt/lucia/current`.
8. Start Redis, then AgentHost.
9. Check Redis persistence, `/health`, and CUDA provider selection.
10. Restore the prior release symlink and restart both services if validation
    fails.

Most Lucia updates require only service restarts. The dashboard should warn
that it will disconnect, then reconnect to the same operation after AgentHost
returns.

### Operating-system updates

The OS channel owns JetPack, boot firmware, kernel, NVIDIA drivers, base
rootfs, and security fixes. It does not change `/opt/lucia`,
`/var/lib/lucia`, or `/var/lib/redis`.

Jetson Linux supports image-based OTA and rootfs A/B on Orin. `APP` and
`APP_b` pair with bootloader slots, and the bootloader can fail over after
repeated boot failures. NVIDIA provides the payload tools but requires the
product to supply the OTA client, download service, signature policy, and
validation logic.

The dashboard can discover, download, and stage an OS update. Applying it
writes the inactive slot and requires an explicit reboot. The new slot is not
marked healthy until Redis, AgentHost, SQLite, and CUDA pass validation. Failed
validation leaves the previous slot available for rollback.

OS rollback does not roll back application data. SQLite migrations must remain
backward compatible or ship a tested restore path. Compatibility metadata must
also prevent an OS update when the installed Lucia release cannot run on its
JetPack and CUDA versions.

Sources:

- [NVIDIA rootfs A/B](https://docs.nvidia.com/jetson/archives/r36.5.2/DeveloperGuide/SD/RootFileSystem.html)
- [NVIDIA image-based OTA](https://docs.nvidia.com/jetson/archives/r36.5.2/DeveloperGuide/SD/SoftwarePackagesAndTheUpdateMechanism.html)

## Security and first boot

The first image should:

- create a locked, unprivileged `lucia` account;
- grant only the `render`, `video`, and `audio` groups needed by the workload;
- keep the rootfs read-only where practical;
- expose the setup UI only on the local network;
- generate a unique dashboard key per device;
- store secrets and data on an encrypted partition;
- disable root SSH and password login;
- restrict dashboard and Wyoming ports with the host firewall;
- restrict mDNS to trusted local interfaces;
- sign application and operating-system updates.

### Privileged appliance manager

AgentHost must remain unprivileged. Do not grant its user general `sudo`,
unrestricted systemd D-Bus access, or write access to the rootfs.

Use a small root-owned appliance manager with an allowlisted local API over a
Unix socket under `/run/lucia-appliance`. It accepts only fixed operations:

- read appliance, service, storage, temperature, and update status;
- validate and apply telemetry configuration;
- enable or disable the two telemetry services;
- check, stage, apply, and roll back signed Lucia updates;
- check and stage signed operating-system updates;
- restart AgentHost and Redis;
- reboot or shut down the host;
- produce a redacted diagnostics bundle.

The manager validates every argument, performs one operation at a time, and
records progress in persistent update state. AgentHost exposes only the
authenticated appliance endpoints needed by the dashboard and forwards
allowlisted requests to the Unix socket. Long operations return an operation
ID so a browser reconnect can resume status polling after service or host
restart.

This trust boundary is required. The existing `/api/system/restart` endpoint
only stops AgentHost through `IHostApplicationLifetime`; it does not restart
the host.

Do not enable `PrivateDevices=true` on the Lucia systemd unit because it hides
GPU and audio devices. The current `infra/systemd/lucia.service` also hardcodes
a developer home directory and does not make the SQLite, model, and plugin
paths writable. Reuse the idea, not the current unit.

The setup API can generate the first dashboard key for an unprovisioned
device. Broadly exposing that endpoint creates a race in which another LAN
client could claim the device first. Bind initial setup to a controlled local
network and require the per-flash bootstrap key.

Repository references:

- [Current systemd unit](../../infra/systemd/lucia.service)
- [Current AgentHost restart endpoint](../../lucia.AgentHost/Apis/SystemApi.cs)
- [Setup API](../../lucia.AgentHost/Apis/SetupApi.cs)

Secure boot and disk-encryption fuse work should wait until flashing, recovery,
and update procedures are proven. Fuse changes are not a proof-of-concept
task.

Sources:

- [NVIDIA secure boot](https://docs.nvidia.com/jetson/archives/r36.5.2/DeveloperGuide/SD/Security/SecureBoot.html)
- [NVIDIA disk encryption](https://docs.nvidia.com/jetson/archives/r36.5.2/DeveloperGuide/SD/Security/DiskEncryption.html)

## Dashboard in appliance mode

The backend exposes an appliance capability flag. The dashboard adds no
appliance controls unless `Appliance__Mode=Installed` and the authenticated user
has administrator access.

On first boot, add one optional **Telemetry** step before setup completes. The
step explains what leaves the device, starts disabled, supports endpoint and
credential testing, and can be skipped without a warning state.

After onboarding, add one **Appliance** navigation item with four sections:

1. **Overview** shows appliance OS, JetPack, Lucia, and Redis versions; uptime;
   storage; temperature; service health; and pending restart state.
2. **Updates** shows separate Lucia and OS cards with current version, available
   version, compatibility, release notes, download size, progress, rollback
   state, and reboot requirements.
3. **Telemetry** edits the same opt-in settings, tests the remote endpoint, and
   shows Collector and Redis exporter health.
4. **Diagnostics** restarts AgentHost or Redis, downloads a redacted support
   bundle, and exposes host restart and shutdown behind explicit confirmation.

Host restart confirmation should show active task count and update status.
Block restart while an update is writing a release or OS slot. A plain
confirmation is enough for normal restart; destructive factory reset and
partition management are not part of the first appliance dashboard.

Reuse the existing Lucia Observatory shell, status colors, confirmation dialog,
toast feedback, and responsive navigation. The page is an operational control
surface, not a separate visual identity.

## First-boot installation and network onboarding

The Jetson Orin Nano Developer Kit has a populated M.2 Key-E 2230 slot with an
included wireless module. NVIDIA also documents USB-C device mode with USB
Ethernet at `192.168.55.1`, serial, and mass storage. A production appliance
must list a qualified Wi-Fi module and antennas in its own bill of materials;
wireless is a carrier-board feature, not part of the Orin Nano module.

Do not build a custom Bluetooth application for the first release. NVIDIA's
public kit specification guarantees an included wireless module but does not
make Bluetooth capability a stable product contract. Bluetooth provisioning
would also require a BlueZ GATT service, pairing policy, and a compatible
mobile application. Wi-Fi host mode can use the existing Lucia web setup.

Sources:

- [NVIDIA Orin Nano Developer Kit hardware layout](https://docs.nvidia.com/jetson/orin-nano-devkit/user-guide/latest/hardware_layout.html)
- [NVIDIA supported hardware guidance](https://docs.nvidia.com/jetson/orin-nano-devkit/user-guide/latest/supported_hardware.html)

### User flow

The intended blank-device flow is:

1. Use the Lucia image writer to flash the installer image to microSD.
2. Insert the microSD and one or more target drives, then apply power.
3. Boot the installer from microSD, validate the JetPack 6 QSPI level, and
   create a temporary Wi-Fi access point named
   `Lucia-<short-id>`.
4. Connect a phone or computer to that network and open the captive setup page.
5. Claim the appliance, choose the home Wi-Fi network, set the hostname, and
   configure optional telemetry.
6. Review every eligible target drive by stable ID, model, serial, capacity,
   transport, mount state, and existing partitions.
7. Select one target and explicitly authorize erasing that exact device.
8. Install the A/B rootfs, Lucia partition, and data partition to the selected
   drive.
9. Copy the completed onboarding state into the persistent data partition.
10. Reboot from the installed drive, join the home network, and continue at
    `http://<hostname>.local` or the displayed IP address.

Captive-portal auto-opening is a convenience, not the only entry point. The
instructions must also provide a fixed setup address.

Never write a target drive before explicit confirmation, even when it appears
blank. Occupied drives require a stronger warning that names their existing
partitions and filesystems. Authorization must bind to the stable device ID,
serial, capacity, current partition summary, and image digest so a device
reorder cannot redirect an approved erase to another drive.

Enumerate storage through stable `/dev/disk/by-id` identities rather than
assuming `/dev/nvme0n1`. Exclude the boot microSD, mounted system disks,
read-only media, undersized devices, and the device that holds installer state.
This keeps the same flow usable when later carrier boards expose multiple NVMe,
USB, or other supported storage connections.

The SD installer can only provide the one-card experience when the developer
kit already has JetPack 6-compatible QSPI firmware. Kits with incompatible QSPI
firmware need a one-time Force Recovery flash from a Linux host. A manufactured
appliance should program QSPI before shipment.

### Secure first claim

Do not ship a shared Wi-Fi password, dashboard key, or Linux password.

A generic DIY image derives its WPA2 setup passphrase from the Jetson serial
printed on the device. Its setup network is client-isolated and blocked from
forwarding traffic. Setup binds only to the AP address, and the first browser
atomically claims the installer with an HttpOnly session cookie. A reboot
clears an abandoned claim.

Manufactured appliances should use a unique code or QR label. Their temporary
access point can use that secret as its WPA2 or WPA3 passphrase and require the
same secret before creating the permanent dashboard key.

Validate host/AP mode on the exact JetPack 6 wireless module and driver. The
flow does not require simultaneous AP and station mode. It may stop the AP
while testing the home network and restore it when the NetworkManager
checkpoint expires.

Source: [NetworkManager `nmcli` and checkpoint support](https://networkmanager.dev/docs/api/latest/nmcli.html)

### Recovery paths

Use these fallbacks in order:

1. USB-C device-mode Ethernet at `http://192.168.55.1/setup`.
2. A restricted first-boot console that launches NetworkManager's standard
   `nmtui` on DisplayPort with a keyboard.
3. Wired Ethernet followed by `http://lucia.local`.

Keep USB setup available until ownership and a working LAN connection are
confirmed. The console must not drop the user into a general root shell.

Source: [NetworkManager `nmtui`](https://networkmanager.dev/docs/api/latest/nmtui.html)

## mDNS and appliance hostname

Install and enable Avahi as the appliance-wide mDNS and DNS-SD responder. The
default static hostname is `lucia`, which produces the default local name
`lucia.local`.

During first-boot installation, show a hostname field before Home Assistant and
telemetry configuration:

- default value: `lucia`;
- user input is the host label only, without `.local`;
- allow lowercase ASCII letters, numbers, and interior hyphens;
- reject empty labels, labels longer than 63 characters, and leading or
  trailing hyphens;
- show the resulting address, such as `kitchen-lucia.local`, before applying
  it.

The appliance manager applies the validated value with `hostnamectl`, persists
it as the static hostname, restarts Avahi, and restarts AgentHost so telemetry
resource attributes and service names use the new value. The Appliance page
shows the effective hostname and local URL. Changing it after onboarding uses
the same validation and requires explicit confirmation because existing
bookmarks and Home Assistant connections may still use the old name.

Avahi should publish:

- the host address as `<hostname>.local`;
- the dashboard as `_http._tcp` with its configured port and `/` path;
- the Wyoming voice service as `_wyoming._tcp` on port `10400`.

AgentHost already advertises `_wyoming._tcp` through
`ZeroconfAdvertiser`. Add an appliance-mode switch that disables that
in-process advertisement when Avahi owns it. Running both responders for the
same service adds duplicate records and makes conflict handling harder.

Allow UDP port 5353 multicast on trusted local interfaces. mDNS normally does
not cross VLANs, guest-network isolation, or routed subnets, so onboarding must
also show the current IP address as a fallback.

`lucia.local` is a default, not a fleet-wide guarantee. If another device
already owns the name, mDNS conflict resolution will change one responder's
effective name. The installer should probe before applying the hostname, show
the conflict, and ask the owner for a unique label instead of silently claiming
that `lucia.local` is available.

Sources:

- [Avahi mDNS and DNS-SD overview](https://avahi.org/)
- [systemd `hostnamectl`](https://www.freedesktop.org/software/systemd/man/latest/hostnamectl.html)
- [RFC 6762: Multicast DNS](https://www.rfc-editor.org/rfc/rfc6762.html)
- [RFC 6763: DNS-Based Service Discovery](https://www.rfc-editor.org/rfc/rfc6763.html)
- [Current Wyoming mDNS advertiser](../../lucia.Wyoming/Discovery/ZeroconfAdvertiser.cs)

## Performance expectations

Local image measurements on 2026-08-29:

| Existing image | Size |
|---|---:|
| Jetson voice image | 2,219,752,557 bytes |
| Jetson no-speech image | 148,711,230 bytes |

The voice image includes the .NET runtime, custom ONNX Runtime and sherpa
libraries, CUDA and cuDNN runtime libraries, models, dashboard, and plugins.
A native image avoids duplicating CUDA and cuDNN because JetPack already owns
them. It still needs the operating system, firmware-facing userspace, .NET
runtime, models, and custom voice libraries.

Expected wins with telemetry disabled:

- less RAM by removing PostgreSQL, its exporter, and Docker;
- less disk duplication from container layers and bundled CUDA libraries;
- fewer enabled services and health dependencies;
- faster boot-to-ready time;
- simpler access to GPU and audio devices.

Enabling the Collector and Redis exporter adds their measured memory and CPU
cost back. That cost is explicit and user-controlled rather than required for
every appliance.

Do not promise a CUDA throughput gain. Docker uses Linux namespaces and the
host kernel, and NVIDIA's runtime exposes the real GPU. Database volumes
already bypass the writable container layer. Benchmark before and after:

- boot-to-health time;
- idle and peak RSS;
- installed and compressed image size;
- API latency;
- voice real-time factor;
- sustained temperature and throttling;
- SQLite write latency and lock rate;
- Redis restart and AOF recovery time.

Sources:

- [Docker architecture](https://docs.docker.com/get-started/docker-overview/)
- [Docker volumes](https://docs.docker.com/engine/storage/volumes/)
- [NVIDIA Container Toolkit architecture](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/latest/arch-overview.html)

## Delivery sequence

### Phase 1: native proof of concept

- Record the current Orin module, carrier, firmware, and installed package
  manifest.
- Measure the existing Compose deployment.
- Publish AgentHost and reuse the current ARM64 voice libraries.
- Run AgentHost, Redis AOF, and SQLite on stock JetPack under corrected systemd
  units.
- Install the Collector and Redis exporter as disabled native units.
- Install Avahi, set the default hostname to `lucia`, and publish dashboard and
  Wyoming service records.
- Add the microSD bootstrap installer, blank-NVMe provisioning, secure Wi-Fi
  SoftAP, captive web setup, USB Ethernet setup, and restricted `nmtui`
  recovery.
- Validate setup, dashboard, Home Assistant, Redis recovery, SQLite recovery,
  voice, CUDA selection, plugins, `lucia.local`, a custom hostname, SoftAP
  rollback, USB setup, reboot, and abrupt power loss.

### Phase 2: reproducible JetPack 6 flash bundle

- Move to a public, checksum-pinned Jetson Linux 36.5.2 BSP.
- Rebuild ONNX Runtime and sherpa against that exact compute stack.
- Build the customized rootfs without desktop and development packages.
- Flash QSPI and NVMe in one documented process.
- Add unique first-boot credentials and a separate persistent data partition.
- Document the one-time recovery-flash path for kits with incompatible QSPI
  firmware.

### Phase 3: appliance management and onboarding

- Add the narrow root-owned appliance manager and Unix-socket protocol.
- Add appliance capability discovery and authenticated management APIs.
- Add hostname and optional telemetry onboarding steps plus the Appliance
  dashboard page.
- Validate Collector configuration, remote OTLP connectivity, enable, disable,
  restart, and redaction behavior.
- Validate mDNS conflict detection, hostname changes, IPv4 and IPv6
  advertisement, and IP-address fallback.
- Add host and service status, diagnostics, and guarded reboot controls.

### Phase 4: split updates and rollback

- Add the signed Lucia feed, coordinated AgentHost and Redis bundle, and
  release-symlink rollback.
- Add coordinated Redis and SQLite backups.
- Add the separate OS feed, rootfs A/B, image-based OTA, and post-boot health
  validation.
- Add compatibility checks in both directions between Lucia and OS releases.

### Phase 5: hardening

- Exercise interrupted downloads, failed service restarts, failed slot boots,
  corrupted signatures, full disks, unavailable OTLP endpoints, and browser
  reconnects.
- Add disk encryption and secure boot after physical recovery testing.

### Phase 6: reconsider the long-term image builder

- Revisit JetPack 7 only after Orin firmware and operating-system stability is
  demonstrated on the target hardware.
- Rebuild the voice native dependencies for its CUDA stack before any trial.
- Compare a pruned NVIDIA rootfs with the officially supported Yocto route.
- Adopt Yocto only if measured image size, boot time, security policy, or fleet
  reproducibility justify owning the recipes and update integration.
- Evaluate Ubuntu Core only if managed snap OTA and confinement justify its GPU
  and plugin packaging work.

## Feasibility summary

| Capability | Feasibility | Main risk |
|---|---|---|
| Native AgentHost on Orin Nano | High | Correct native GPU library placement |
| Redis plus SQLite appliance storage | High | Power-loss policy and coordinated backup |
| Optional native telemetry | High | Secret handling and bounded queues |
| `lucia.local` mDNS discovery | High | Name conflicts and segmented networks |
| Wi-Fi captive onboarding | High after hardware validation | AP security and failed network rollback |
| Flash, insert, and power installation | Medium | QSPI compatibility and safe NVMe provisioning |
| Appliance dashboard and host controls | High | Narrow privilege boundary |
| Independent Lucia updates | High | Coordinated AgentHost and Redis rollback |
| Independent OS A/B updates | Medium | Compatibility, signing, and boot validation |
| Repeatable devkit flash bundle | High | Board and QSPI/NVMe version coupling |
| Smaller runtime footprint | High | Most size remains in OS, models, and GPU stack |
| Faster CUDA inference from removing Docker | Low | There is little container compute overhead to remove |
| App-level rollback | High | Data migration compatibility |
| Full A/B OTA and secure boot | Medium | Update client, signing, recovery, and key operations |
| Yocto production image | High technically | Recipe and GPU migration cost |
| Original Jetson Nano support | Low | EOL BSP and incompatible compute stack |

The appliance is worth building for deployment simplicity, owner-controlled
telemetry, and lower default background resource use. Do not justify it as a
GPU performance project. The first useful milestone is a native JetPack 6
installation that survives reboot with Redis and SQLite, not a custom
distribution.
