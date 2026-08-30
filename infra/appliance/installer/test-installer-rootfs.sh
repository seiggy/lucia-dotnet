#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
rootfs="$script_dir/rootfs"
network_unit="$rootfs/usr/lib/systemd/system/lucia-network-bootstrap.service"
host_unit="$rootfs/usr/lib/systemd/system/lucia-installer-host.service"
install_unit="$rootfs/usr/lib/systemd/system/lucia-firstboot-install.service"
environment="$rootfs/etc/lucia-installer/installer.env.example"
dnsmasq_config="$rootfs/etc/NetworkManager/dnsmasq-shared.d/lucia-captive.conf"

grep -q '^Before=lucia-installer-host.service$' "$network_unit"
grep -q '^ExecStart=/usr/libexec/lucia/lucia-network-bootstrap$' "$network_unit"
grep -q '^After=.*lucia-network-bootstrap.service' "$host_unit"
grep -q '^ExecStart=/opt/lucia-installer/app/lucia.InstallerHost$' "$host_unit"
grep -q '^EnvironmentFile=/etc/lucia-installer/installer.env$' "$host_unit"
grep -q '^Appliance__Mode=Installer$' "$environment"
grep -q '^address=/#/10.42.0.1$' "$dnsmasq_config"
grep -q '^After=.*lucia-installer-host.service' "$install_unit"

echo "PASS: installer rootfs starts the captive host before disk installation"
