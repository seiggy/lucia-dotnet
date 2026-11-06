#!/usr/bin/env python3
import json
import os
import stat
import subprocess
import tempfile
import unittest
from pathlib import Path

HELPERS = Path(__file__).resolve().parents[1] / "rootfs/usr/libexec/lucia"
VALIDATOR = HELPERS / "lucia-validate-os-update"
CHECK = HELPERS / "lucia-rootfs-ab-check"
BOOT_SLOT = HELPERS / "lucia-boot-slot"
MODE = "L4TDefaultBootMode-781e084c-a330-417c-b678-38e696380cb9"


class OsBootValidationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.state = self.root / "state"
        self.state.mkdir()
        self.log = self.root / "commands"
        self.log.touch()
        self.tool("systemctl", 'case "$*" in *reboot*) test ! -e "$TEST_ROOT/reboot-fails" ;; esac')
        self.tool("curl", 'test ! -e "$TEST_ROOT/unhealthy"')
        self.tool("nm-online", "exit 0")
        self.tool(
            "findmnt",
            'case "$*" in *--mountpoint*) cat "$TEST_ROOT/esp-source" ;; *) cat "$TEST_ROOT/root-source" ;; esac',
        )
        self.tool("lsblk", "printf 'test-disk\\n'")
        self.tool(
            "blkid",
            'test ! -e "$TEST_ROOT/missing-uuid" || exit 2\ncat "$TEST_ROOT/root-uuid"',
        )
        self.tool(
            "grub-editenv",
            """
file="$1"; command="$2"; shift 2
test ! -e "$TEST_ROOT/editenv-fails" || exit 1
case "$command" in
  create) : > "$file" ;;
  set) for pair in "$@"; do grep -v "^${pair%%=*}=" "$file" > "$file.new" || true; echo "$pair" >> "$file.new"; mv "$file.new" "$file"; done ;;
  *) exit 64 ;;
esac
""",
        )
        self.partitions = self.root / "partitions"
        self.partitions.mkdir()
        sysblock = self.root / "sysblock"
        for index, label in enumerate(("APP", "APP_b", "esp"), start=1):
            os.mknod(self.partitions / label, stat.S_IFBLK | 0o600, os.makedev(240, index))
            (sysblock / label).mkdir(parents=True)
            (sysblock / label / "partition").write_text(str(index if label != "esp" else 11))
        self.efivars = self.root / "efivars"
        self.efivars.mkdir()
        self.set_boot_mode(1)
        self.esp = self.root / "esp"
        (self.esp / "EFI/BOOT").mkdir(parents=True)
        (self.root / "esp-source").write_text(str(self.partitions / "esp"))
        self.grub = self.root / "grubaa64.efi"
        self.grub.write_bytes(b"grub")
        self.environment = {
            **os.environ,
            "PATH": f"{self.root}:{os.environ['PATH']}",
            "TEST_ROOT": str(self.root),
            "LUCIA_UPDATE_ROOT": str(self.root),
            "LUCIA_UPDATE_HEALTH_ATTEMPTS": "1",
            "LUCIA_UPDATE_HEALTH_DELAY_SECONDS": "0",
            "LUCIA_SYSTEMCTL_PATH": str(self.root / "systemctl"),
            "LUCIA_CURL_PATH": str(self.root / "curl"),
            "LUCIA_NM_ONLINE_PATH": str(self.root / "nm-online"),
            "LUCIA_PARTLABEL_DIR": str(self.partitions),
            "LUCIA_SYS_CLASS_BLOCK": str(sysblock),
            "LUCIA_ROOTFS_MOUNT": str(self.root),
            "LUCIA_EFIVARS_PATH": str(self.efivars),
            "LUCIA_CMDLINE_PATH": str(self.root / "cmdline"),
            "LUCIA_ESP_MOUNT": str(self.esp),
            "LUCIA_GRUB_IMAGE_PATH": str(self.grub),
            "LUCIA_GRUB_EDITENV_PATH": str(self.root / "grub-editenv"),
        }
        self.set_mounted_slot(0)
        self.run_tool(BOOT_SLOT, "install", str(self.grub), "0")

    def tearDown(self):
        self.temporary.cleanup()

    def tool(self, name, commands):
        path = self.root / name
        path.write_text(
            f'#!/bin/sh\nprintf "{name} %s\\n" "$*" >> "$TEST_ROOT/commands"\n{commands}\n'
        )
        path.chmod(0o755)

    def set_boot_mode(self, mode):
        (self.efivars / MODE).write_bytes(bytes([7, 0, 0, 0, mode, 0, 0, 0]))

    def set_mounted_slot(self, slot):
        uuid = f"00000000-0000-0000-0000-{slot:012d}"
        (self.root / "root-source").write_text(
            str(self.partitions / ("APP_b" if slot else "APP"))
        )
        (self.root / "root-uuid").write_text(uuid)
        (self.root / "cmdline").write_text(f"root=PARTUUID={uuid} rw\n")

    def write_state(self, status, credential=True):
        (self.state / "os.env").write_text(
            "previous_slot=0\ntarget_slot=1\nversion=1.4.4\ntag=v1.4.4\n"
            "operation_id=11111111-1111-1111-1111-111111111111\n"
            f"status={status}\n"
            "validation_token=22222222-2222-2222-2222-222222222222\n"
        )
        key = self.state / "validation.key"
        if credential:
            key.write_text("33333333-3333-3333-3333-333333333333")
        elif key.exists():
            key.unlink()

    def run_tool(self, path, *arguments, expect_success=True):
        result = subprocess.run(
            ["bash", str(path), *arguments],
            env=self.environment,
            capture_output=True,
            text=True,
            timeout=10,
            check=False,
        )
        if expect_success:
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        return result

    def grub_env(self):
        pairs = (self.esp / "EFI/BOOT/lucia.env").read_text().splitlines()
        return dict(pair.split("=", 1) for pair in pairs)

    def status(self):
        for line in (self.state / "os.env").read_text().splitlines():
            if line.startswith("status="):
                return line.split("=", 1)[1]
        return None

    def operation(self):
        return json.loads((self.state / "operation.json").read_text())

    def reboots(self):
        return self.log.read_text().count("systemctl --no-block reboot")

    def test_install_selects_grub_and_records_the_committed_slot(self):
        self.assertEqual((self.esp / "EFI/BOOT/grubaa64.efi").read_bytes(), b"grub")
        self.assertEqual(
            self.grub_env(),
            {"lucia_slot": "a", "lucia_trial": "", "lucia_a": "gpt1", "lucia_b": "gpt2"},
        )
        self.assertEqual((self.efivars / MODE).read_bytes()[4], 0)
        self.run_tool(BOOT_SLOT, "commit", "1")
        self.run_tool(BOOT_SLOT, "trial", "1")
        self.run_tool(BOOT_SLOT, "install", str(self.grub), "0")
        self.assertEqual(self.grub_env()["lucia_slot"], "a")
        self.assertEqual(self.grub_env()["lucia_trial"], "")

    def test_layout_reports_the_mounted_slot(self):
        self.assertEqual(self.run_tool(CHECK, "--layout").stdout.strip(), "0")
        self.set_mounted_slot(1)
        self.assertEqual(self.run_tool(CHECK, "--layout").stdout.strip(), "1")
        self.set_boot_mode(0)
        self.assertEqual(self.run_tool(CHECK).stdout.strip(), "0")

    def test_layout_rejects_inconsistent_or_unsupported_boot_setups(self):
        cases = {
            "boot-mode": "GRUB or extlinux boot mode",
            "not-a-slot": "not an APP or APP_b",
            "kernel-partuuid": "root PARTUUID does not match",
            "esp-unmounted": "EFI system partition",
            "no-grub-image": "predates GRUB slot selection",
            "missing-uuid": "no valid PARTUUID",
        }
        for mismatch, message in cases.items():
            with self.subTest(mismatch=mismatch):
                self.set_mounted_slot(0)
                self.set_boot_mode(1)
                (self.root / "esp-source").write_text(str(self.partitions / "esp"))
                self.grub.write_bytes(b"grub")
                (self.root / "missing-uuid").unlink(missing_ok=True)
                if mismatch == "boot-mode":
                    self.set_boot_mode(2)
                elif mismatch == "not-a-slot":
                    (self.root / "root-source").write_text(str(self.partitions / "esp"))
                elif mismatch == "kernel-partuuid":
                    (self.root / "cmdline").write_text(
                        "root=PARTUUID=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa rw\n"
                    )
                elif mismatch == "esp-unmounted":
                    (self.root / "esp-source").write_text("")
                elif mismatch == "no-grub-image":
                    self.grub.write_bytes(b"")
                else:
                    (self.root / "missing-uuid").touch()
                result = self.run_tool(CHECK, "--layout", expect_success=False)
                self.assertNotEqual(result.returncode, 0)
                self.assertIn(message, result.stderr)

    def test_healthy_trial_slot_is_committed_without_a_reboot(self):
        self.write_state("pending")
        self.run_tool(BOOT_SLOT, "trial", "1")
        self.set_mounted_slot(1)
        self.run_tool(VALIDATOR)
        self.assertEqual(self.status(), "validated")
        self.assertEqual(self.operation()["Status"], "succeeded")
        self.assertEqual(self.operation()["Phase"], "complete")
        self.assertEqual(self.grub_env()["lucia_slot"], "b")
        self.assertEqual(self.grub_env()["lucia_trial"], "")
        self.assertEqual(self.reboots(), 0)
        self.assertFalse((self.state / "validation.key").exists())
        self.assertNotIn("nvbootctrl", self.log.read_text())

    def test_unhealthy_or_unverifiable_trial_reboots_into_the_committed_slot(self):
        for setup in ("unhealthy", "missing-credential"):
            with self.subTest(setup=setup):
                self.log.write_text("")
                self.write_state("pending", credential=setup != "missing-credential")
                (self.root / "unhealthy").unlink(missing_ok=True)
                if setup == "unhealthy":
                    (self.root / "unhealthy").touch()
                self.run_tool(BOOT_SLOT, "trial", "1")
                self.set_mounted_slot(1)
                self.run_tool(VALIDATOR)
                self.assertEqual(self.status(), "rolled-back")
                self.assertEqual(self.operation()["Action"], "apply")
                self.assertEqual(self.operation()["Status"], "failed")
                self.assertEqual(self.grub_env()["lucia_slot"], "a")
                self.assertEqual(self.grub_env()["lucia_trial"], "")
                self.assertEqual(self.reboots(), 1)
                self.run_tool(VALIDATOR)
                self.assertEqual(self.reboots(), 1)

    def test_trial_that_never_started_reports_failure_without_rebooting(self):
        self.write_state("pending")
        # The launcher fell back to extlinux, so GRUB never consumed the trial.
        self.run_tool(BOOT_SLOT, "trial", "1")
        self.set_mounted_slot(0)
        self.run_tool(VALIDATOR)
        self.assertEqual(self.status(), "rolled-back")
        self.assertIn("did not finish starting", self.operation()["Message"])
        self.assertEqual(self.grub_env()["lucia_trial"], "")
        self.assertEqual(self.reboots(), 0)

    def test_failed_commit_keeps_the_trial_pending_for_a_retry(self):
        self.write_state("pending")
        self.run_tool(BOOT_SLOT, "trial", "1")
        self.set_mounted_slot(1)
        (self.root / "editenv-fails").touch()
        result = self.run_tool(VALIDATOR, expect_success=False)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.status(), "pending")
        self.assertTrue((self.state / "validation.key").exists())
        self.assertEqual(self.reboots(), 0)
        (self.root / "editenv-fails").unlink()
        self.run_tool(VALIDATOR)
        self.assertEqual(self.status(), "validated")
        self.assertEqual(self.grub_env()["lucia_slot"], "b")

    def test_failed_recovery_reboot_is_requested_again(self):
        self.write_state("pending")
        (self.root / "unhealthy").touch()
        self.run_tool(BOOT_SLOT, "trial", "1")
        self.set_mounted_slot(1)
        (self.root / "reboot-fails").touch()
        result = self.run_tool(VALIDATOR, expect_success=False)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.status(), "pending")
        self.assertEqual(self.grub_env()["lucia_trial"], "")
        (self.root / "reboot-fails").unlink()
        self.run_tool(VALIDATOR)
        self.assertEqual(self.status(), "rolled-back")
        self.assertEqual(self.reboots(), 2)

    def test_requested_rollback_commits_the_previous_slot_only_when_healthy(self):
        self.run_tool(BOOT_SLOT, "commit", "1")
        self.write_state("rollback-pending")
        self.run_tool(BOOT_SLOT, "trial", "0")
        self.set_mounted_slot(0)
        self.run_tool(VALIDATOR)
        self.assertEqual(self.status(), "rolled-back")
        self.assertEqual(self.operation()["Action"], "rollback")
        self.assertEqual(self.operation()["Status"], "succeeded")
        self.assertEqual(self.grub_env()["lucia_slot"], "a")

        self.run_tool(BOOT_SLOT, "commit", "1")
        self.write_state("rollback-pending")
        self.set_mounted_slot(1)
        self.run_tool(VALIDATOR)
        self.assertEqual(self.status(), "validated")
        self.assertEqual(self.operation()["Status"], "failed")
        self.assertEqual(self.grub_env()["lucia_slot"], "b")
        self.assertEqual(self.reboots(), 0)

    def test_inconsistent_layout_stops_without_rebooting(self):
        self.write_state("pending")
        self.set_mounted_slot(1)
        self.set_boot_mode(2)
        self.run_tool(VALIDATOR)
        self.assertEqual(self.status(), "failed")
        self.assertEqual(self.operation()["Status"], "failed")
        self.assertEqual(self.reboots(), 0)

    def test_unsupported_boot_mode_blocks_apply_and_rollback_without_writes(self):
        self.set_boot_mode(2)
        updater = HELPERS / "lucia-update"
        for action in ("apply", "rollback"):
            result = subprocess.run(
                ["bash", str(updater), action, "os", "v1.4.4"],
                env={**self.environment, "LUCIA_VALIDATION_GROUP": "root"},
                capture_output=True,
                text=True,
                timeout=10,
                check=False,
            )
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("preflight", result.stderr)
        self.assertEqual(self.reboots(), 0)
        self.assertEqual(self.grub_env()["lucia_trial"], "")

    def test_slot_image_boot_references_use_the_installed_partition_uuid(self):
        target = self.root / "image"
        (target / "boot/extlinux").mkdir(parents=True)
        (target / "etc").mkdir()
        (target / "boot/extlinux/extlinux.conf").write_text(
            "DEFAULT primary\nLABEL primary\n  LINUX /boot/Image\n"
            "  APPEND ${cbootargs} root=PARTUUID=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa rw\n"
        )
        (target / "etc/fstab").write_text(
            "UUID=old-filesystem / ext4 defaults 0 1\n"
            "PARTLABEL=LUCIA_DATA /var/lib/lucia ext4 defaults 0 2\n"
        )
        uuid = "2247cb2a-1ce0-43de-aca6-447bc24e2946"
        result = subprocess.run(
            ["python3", str(HELPERS / "lucia-rebind-rootfs.py"), str(target), uuid],
            capture_output=True,
            text=True,
            timeout=10,
            check=False,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn(
            f"root=PARTUUID={uuid}",
            (target / "boot/extlinux/extlinux.conf").read_text(),
        )
        self.assertIn(f"PARTUUID={uuid} / ext4", (target / "etc/fstab").read_text())
        self.assertIn("PARTLABEL=LUCIA_DATA", (target / "etc/fstab").read_text())

    def test_rebinding_rejects_a_symlinked_boot_configuration(self):
        target = self.root / "image"
        (target / "boot/extlinux").mkdir(parents=True)
        outside = self.root / "outside"
        outside.write_text("do not change")
        (target / "boot/extlinux/extlinux.conf").symlink_to(outside)
        result = subprocess.run(
            [
                "python3",
                str(HELPERS / "lucia-rebind-rootfs.py"),
                str(target),
                "2247cb2a-1ce0-43de-aca6-447bc24e2946",
            ],
            capture_output=True,
            text=True,
            timeout=10,
            check=False,
        )
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(outside.read_text(), "do not change")


if __name__ == "__main__":
    unittest.main()
