#!/usr/bin/env python3
import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path

VALIDATOR = (
    Path(__file__).resolve().parents[1]
    / "rootfs/usr/libexec/lucia/lucia-validate-os-update"
)


class OsBootValidationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.state = self.root / "state"
        self.state.mkdir()
        self.log = self.root / "commands"
        self.environment = {
            **os.environ,
            "LUCIA_UPDATE_ROOT": str(self.root),
            "LUCIA_UPDATE_HEALTH_ATTEMPTS": "1",
            "LUCIA_UPDATE_HEALTH_DELAY_SECONDS": "0",
            "TEST_ROOT": str(self.root),
        }
        self.tool(
            "nvbootctrl",
            """
case "$*" in
  '-t rootfs is-rootfs-ab-enabled') exit "$(cat "$TEST_ROOT/ab")" ;;
  '-t rootfs get-current-slot') cat "$TEST_ROOT/slot" ;;
  'verify') exit 0 ;;
  '-t rootfs set-active-boot-slot '*) exit 0 ;;
  *) exit 64 ;;
esac
""",
        )
        self.tool("systemctl", "exit 0")
        self.tool("curl", 'test ! -e "$TEST_ROOT/unhealthy"')
        self.tool("nm-online", "exit 0")
        self.environment.update(
            {
                "LUCIA_NVBOOTCTRL_PATH": str(self.root / "nvbootctrl"),
                "LUCIA_SYSTEMCTL_PATH": str(self.root / "systemctl"),
                "LUCIA_CURL_PATH": str(self.root / "curl"),
                "LUCIA_NM_ONLINE_PATH": str(self.root / "nm-online"),
            }
        )
        (self.root / "ab").write_text("1")
        (self.root / "slot").write_text("0")
        self.reset_state()

    def tearDown(self):
        self.temporary.cleanup()

    def tool(self, name, commands):
        path = self.root / name
        path.write_text(
            f'#!/bin/sh\nprintf "{name} %s\\n" "$*" >> "$TEST_ROOT/commands"\n{commands}\n'
        )
        path.chmod(0o755)

    def reset_state(self, status="rollback-recovery-pending"):
        (self.state / "os.env").write_text(
            "previous_slot=0\ntarget_slot=1\nversion=1.4.4\ntag=v1.4.4\n"
            "operation_id=11111111-1111-1111-1111-111111111111\n"
            f"status={status}\n"
            "validation_token=22222222-2222-2222-2222-222222222222\n"
        )
        (self.state / "validation.key").write_text(
            "33333333-3333-3333-3333-333333333333"
        )

    def run_validator(self):
        result = subprocess.run(
            ["bash", str(VALIDATOR)],
            env=self.environment,
            capture_output=True,
            text=True,
            timeout=10,
            check=False,
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def operation(self):
        return json.loads((self.state / "operation.json").read_text())

    def test_disabled_ab_stops_without_rebooting(self):
        (self.root / "ab").write_text("0")
        self.run_validator()
        self.assertEqual(self.operation()["Status"], "failed")
        self.assertIn("RootFS A/B", self.operation()["Message"])
        self.assertNotIn("reboot", self.log.read_text())
        self.assertNotIn("set-active-boot-slot", self.log.read_text())
        self.run_validator()
        self.assertNotIn("reboot", self.log.read_text())

    def test_unknown_ab_result_stops_without_rebooting(self):
        (self.root / "ab").write_text("64")
        self.run_validator()
        self.assertEqual(self.operation()["Status"], "failed")
        self.assertNotIn("reboot", self.log.read_text())

    def test_wrong_slot_reboots_are_bounded_across_invocations(self):
        for _ in range(5):
            self.run_validator()
        self.assertEqual(self.operation()["Status"], "failed")
        self.assertLessEqual(
            self.log.read_text().count("systemctl --no-block reboot"), 2
        )
        self.assertIn("status=failed", (self.state / "os.env").read_text())

    def test_success_uses_the_shipped_nvidia_verify_command(self):
        self.reset_state("pending")
        (self.root / "slot").write_text("1")
        (self.root / "ab").write_text("2")
        self.run_validator()
        self.assertEqual(self.operation()["Status"], "succeeded")
        self.assertIn("nvbootctrl verify\n", self.log.read_text())
        self.assertNotIn("mark-boot-successful", self.log.read_text())
        self.assertNotIn("reboot", self.log.read_text())

    def test_disabled_ab_apply_and_rollback_do_not_write_or_reboot(self):
        updater = VALIDATOR.with_name("lucia-update")
        (self.root / "ab").write_text("0")
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
            self.assertIn("RootFS A/B", result.stderr)
        self.assertNotIn("reboot", self.log.read_text())
        self.assertNotIn("set-active-boot-slot", self.log.read_text())

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
            [
                "python3",
                str(VALIDATOR.with_name("lucia-rebind-rootfs.py")),
                str(target),
                uuid,
            ],
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
        self.assertNotIn(
            "aaaaaaaa-", (target / "boot/extlinux/extlinux.conf").read_text()
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
                str(VALIDATOR.with_name("lucia-rebind-rootfs.py")),
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
