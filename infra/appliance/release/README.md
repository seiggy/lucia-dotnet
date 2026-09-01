# Appliance release assets

`appliance-release.yml` builds three independent appliance channels for stable
GitHub releases:

| Channel | Purpose |
| --- | --- |
| Installer | Complete microSD image for Rufus and first installation |
| Lucia | AgentHost, dashboard, plugins, GPU native libraries, and Redis |
| OS | Jetson rootfs, kernel, and device tree for inactive-slot updates |

The captive setup network is an open, client-isolated, non-routed first-boot
network. The first browser to select **Begin setup** claims the session. Keep
the appliance physically controlled during setup.

The installed dashboard listens at `https://HOSTNAME.local:8099` with a
per-device certificate generated during setup. The first browser must accept
that local certificate. Plain HTTP is bound only to loopback on port 8098.

The `lucia-recovery` account has no sudo membership. Its owner-selected
password opens only the local NetworkManager text interface.

These are full component payloads, not binary deltas. Full payloads are larger,
but they are deterministic, recoverable, and do not require every user to have
the same prior version.

`appliance.lock` pins every CUDA and cuDNN runtime package by direct URL and
SHA-256. The rootfs build installs that local package set without reading live
Ubuntu or NVIDIA package indexes.

## Discovery

The updater requests:

```text
https://api.github.com/repos/seiggy/lucia-dotnet/releases/latest
```

It ignores a release until the release contains
`lucia-appliance-manifest.json`. The workflow uploads that manifest last, after
all payload parts, checksums, and GitHub artifact attestations exist.

Each manifest channel contains:

- complete compressed payload size and SHA-256;
- ordered part names, sizes, hashes, and release URLs;
- board, architecture, Jetson Linux, and minimum disk compatibility.

A future updater must download parts in manifest order, verify every part's
GitHub attestation and hash, join them, then verify the complete payload against
the digest in the attested manifest before writing it. The current dashboard
performs discovery only; apply controls remain locked.

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
200 GiB of scratch space. The GitHub-hosted voice job and self-hosted packaging
job each allow up to 12 hours.
