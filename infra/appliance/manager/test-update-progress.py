#!/usr/bin/env python3
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

HELPER = (
    Path(__file__).resolve().parents[1]
    / "rootfs/usr/libexec/lucia/lucia-update-progress.py"
)


class UpdateProgressTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / "state").mkdir()
        self.path = self.root / "state/operation.json"
        self.operation = {
            "OperationId": "11111111-1111-1111-1111-111111111111",
            "Channel": "os",
            "Status": "running",
        }
        self.path.write_text(json.dumps(self.operation))
        self.environment = {
            **os.environ,
            "LUCIA_UPDATE_ROOT": str(self.root),
            "LUCIA_UPDATE_OPERATION_ID": self.operation["OperationId"],
        }

    def run_helper(self, *arguments):
        return subprocess.run(
            [sys.executable, str(HELPER), *arguments],
            env=self.environment,
            capture_output=True,
            text=True,
            check=False,
            timeout=10,
        )

    def read_progress(self):
        return json.loads(self.path.read_text())

    def test_phase_progress_is_monotonic_and_bounded(self):
        for completed in ("30", "20", "200"):
            result = self.run_helper("phase", "writing", completed, "100")
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(
                self.read_progress()["CompletedBytes"],
                100 if completed == "200" else 30,
            )
        result = self.run_helper("phase", "validating")
        self.assertEqual(result.returncode, 0, result.stderr)
        progress = self.read_progress()
        self.assertEqual(progress["Phase"], "validating")
        self.assertIsNone(progress["CompletedBytes"])
        self.assertIsNone(progress["TotalBytes"])
        self.assertEqual(progress["Status"], "running")

    def test_another_operation_cannot_receive_progress(self):
        self.environment["LUCIA_UPDATE_OPERATION_ID"] = (
            "22222222-2222-2222-2222-222222222222"
        )
        result = self.run_helper("phase", "writing", "10", "100")
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.read_progress(), self.operation)

    def test_dd_progress_aggregates_uncompressed_partition_bytes(self):
        # dd uses carriage returns for intermediate reports and a newline at exit.
        command = (
            "import sys; "
            "sys.stderr.write('10 bytes copied\\r5 bytes copied\\r"
            "40 bytes (40 B) copied, 0.1 s, 400 B/s\\n')"
        )
        result = self.run_helper(
            "dd", "60", "100", "--", sys.executable, "-c", command
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        progress = self.read_progress()
        self.assertEqual(progress["Phase"], "writing")
        self.assertEqual(progress["CompletedBytes"], 100)
        self.assertEqual(progress["TotalBytes"], 100)
        self.assertEqual(progress["Status"], "running")

    def test_failed_write_keeps_last_measured_progress(self):
        command = (
            "import sys; "
            "sys.stderr.write('12 bytes copied\\rdevice write failed\\n'); "
            "sys.exit(7)"
        )
        result = self.run_helper(
            "dd", "20", "100", "--", sys.executable, "-c", command
        )
        self.assertEqual(result.returncode, 7)
        self.assertEqual(self.read_progress()["CompletedBytes"], 32)
        self.assertIn("device write failed", result.stderr)

    def test_real_dd_reports_written_bytes_without_completing_the_operation(self):
        source = self.root / "source.img"
        target = self.root / "target.img"
        source.write_bytes(b"rootfs" * 32768)
        size = source.stat().st_size
        result = self.run_helper(
            "dd", str(size), str(2 * size), "--",
            "dd", f"if={source}", f"of={target}", "conv=fsync", "status=progress",
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(target.read_bytes(), source.read_bytes())
        self.assertEqual(self.read_progress()["CompletedBytes"], 2 * size)
        self.assertEqual(self.read_progress()["TotalBytes"], 2 * size)
        self.assertEqual(self.read_progress()["Status"], "running")


if __name__ == "__main__":
    unittest.main()
