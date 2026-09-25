import importlib.util
import json
import pathlib
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

SCRIPT = pathlib.Path(__file__).with_name("planner.py")
SPEC = importlib.util.spec_from_file_location("planner", SCRIPT)
planner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(planner)


def manifest(tag, fingerprint, mode="full", **extra):
    return {
        "tag": tag, "releaseMode": mode, "imageInputFingerprint": fingerprint,
        "channels": {"lucia": {}, **({"os": {}, "installer": {}} if mode == "full" else {})},
        **extra,
    }


class PlannerTests(unittest.TestCase):
    def test_full_app_app_full_uses_last_published_image(self):
        first, changed = "a" * 64, "b" * 64
        self.assertEqual(planner.choose_release_mode(first, [])["release_mode"], "full")
        releases = [manifest("v1.9.0", first)]
        for tag in ("v1.9.1", "v1.9.2"):
            result = planner.choose_release_mode(first, releases)
            self.assertEqual(result["release_mode"], "app-only")
            self.assertEqual(result["baseline_tag"], "v1.9.0")
            releases.insert(0, manifest(tag, first, "app-only"))
        releases.insert(0, manifest("v1.9.3", changed, "app-only"))
        self.assertEqual(planner.choose_release_mode(changed, releases)["release_mode"], "full")
        releases.insert(0, manifest("v1.10.0", changed))
        result = planner.choose_release_mode(changed, releases)
        self.assertEqual(result["release_mode"], "app-only")
        self.assertEqual(result["baseline_tag"], "v1.10.0")

    def test_draft_failed_and_missing_image_baselines_do_not_advance(self):
        old, new = "a" * 64, "b" * 64
        releases = [
            manifest("v1.2.0", new, draft=True),
            manifest("v1.1.0", new, channels={"lucia": {}}),
            manifest("v1.0.0", old),
        ]
        result = planner.choose_release_mode(new, releases)
        self.assertEqual(result["release_mode"], "full")
        self.assertEqual(result["baseline_tag"], "v1.0.0")
        self.assertEqual(
            planner.choose_release_mode(new, [manifest("v1.0.0", "")])["release_mode"],
            "full",
        )

    def test_force_full_and_immutable_rerun(self):
        fingerprint = "a" * 64
        releases = [manifest("v1.0.0", fingerprint)]
        self.assertEqual(
            planner.choose_release_mode(fingerprint, releases, force_full=True)["release_mode"],
            "full",
        )
        self.assertEqual(
            planner.choose_release_mode(fingerprint, releases, release_tag="v1.0.0")["release_mode"],
            "full",
        )
        with self.assertRaises(ValueError):
            planner.choose_release_mode("b" * 64, releases, release_tag="v1.0.0")

    def test_actual_input_classification_and_cli_are_deterministic(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            for relative in planner.DEFAULT_FINGERPRINT_PATHS:
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                if relative != "lucia.InstallerHost" and (
                    "." in path.name or path.name == "lucia-install"
                ):
                    path.write_text("input\n")
                else:
                    path.mkdir(exist_ok=True)
                    (path / "input").write_text("input\n")
            (root / "infra/docker/Dockerfile.agenthost-jetson-voice").write_text(
                "FROM builder\nFROM scratch AS appliance-voice-assets\nCOPY /native /native\n"
                "FROM app\nRUN app-build\n"
            )
            lock = root / "infra/appliance/release/appliance.lock"
            lock.write_text("JETSON_LINUX_VERSION=36.5.2\nLUCIA_SOURCE_REDIS_VERSION=8\n")
            fingerprint = planner.compute_image_input_fingerprint(root)
            lock.write_text("JETSON_LINUX_VERSION=36.5.2\nLUCIA_SOURCE_REDIS_VERSION=9\n")
            for relative in (
                "lucia.AgentHost/app.cs", "lucia-dashboard/src/page.tsx",
                "infra/appliance/release/__pycache__/planner.pyc",
            ):
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("application or generated change")
            self.assertEqual(planner.compute_image_input_fingerprint(root), fingerprint)
            for relative in (
                "infra/appliance/rootfs/usr/lib/systemd/system/new.service",
                "infra/appliance/installer/rootfs/etc/new-config",
                "lucia.InstallerHost/New.cs",
            ):
                with self.subTest(relative=relative):
                    path = root / relative
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_text("new image input")
                    self.assertNotEqual(planner.compute_image_input_fingerprint(root), fingerprint)
                    path.unlink()
                    self.assertEqual(planner.compute_image_input_fingerprint(root), fingerprint)
            lock.write_text("JETSON_LINUX_VERSION=36.6.0\nLUCIA_SOURCE_REDIS_VERSION=9\n")
            self.assertNotEqual(planner.compute_image_input_fingerprint(root), fingerprint)
            result = subprocess.run(
                [sys.executable, str(SCRIPT), "--repo-root", str(root), "--print-fingerprint"],
                check=True, capture_output=True, text=True,
            )
            self.assertEqual(result.stdout.strip(), planner.compute_image_input_fingerprint(root))

    def test_read_pretty_printed_manifest_array_and_reject_invalid_json(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = pathlib.Path(temporary) / "published.json"
            entries = [manifest("v1.0.1", "a" * 64, "app-only"), manifest("v1.0.0", "a" * 64)]
            path.write_text(json.dumps(entries, indent=2))
            self.assertEqual(planner._read_published_manifests(path), entries)
            path.write_text("{}{}")
            with self.assertRaises(ValueError):
                planner._read_published_manifests(path)

    def test_published_asset_lookup_uses_manifest_last_and_publication_order(self):
        old = manifest("v1.9.0", "a" * 64)
        new = manifest("v1.10.0", "b" * 64)
        for item in (old, new):
            item["repository"] = "owner/repo"
            for channel in item["channels"].values():
                channel["parts"] = [{"name": "payload"}]
        def release(tag, published_at, identifier, complete=True):
            names = ["payload", "SHA256SUMS", "lucia-appliance-attestations.jsonl"]
            if complete:
                names.append("lucia-appliance-manifest.json")
            return {
                "tag_name": tag, "published_at": published_at,
                "draft": False, "prerelease": False,
                "assets": [{"id": identifier, "name": name} for name in names],
            }
        pages = [[
            release("v1.9.0", "2026-09-01", 9),
            release("v1.10.0", "2026-09-02", 10),
            release("v1.11.0", "2026-09-03", 11, False),
        ]]
        def response(args, **kwargs):
            if "--paginate" in args:
                self.assertNotIn("--slurp", args)
                self.assertEqual(args[args.index("--jq") + 1], ".[] | @json")
                self.assertEqual(kwargs["encoding"], "utf-8")
                return "\n".join(json.dumps(item) for page in pages for item in page)
            return json.dumps(new if args[-1].endswith("/10") else old, indent=2)
        with patch.object(planner.subprocess, "check_output", side_effect=response) as command:
            self.assertEqual(planner.fetch_published_manifests("owner/repo"), [new, old])
            self.assertEqual(command.call_count, 3)
            self.assertIn("repos/owner/repo/releases/assets/10", command.call_args_list[1].args[0])

    def test_legacy_manifest_without_attestations_cannot_be_an_app_only_baseline(self):
        legacy = {
            "repository": "owner/repo", "tag": "v1.4.0", "schemaVersion": 1,
            "channels": {name: {"parts": [{"name": "payload"}]}
                         for name in ("lucia", "os", "installer")},
        }
        release = {
            "tag_name": "v1.4.0", "published_at": "2026-09-01",
            "draft": False, "prerelease": False,
            "assets": [{"id": 1, "name": name} for name in
                       ("lucia-appliance-manifest.json", "payload", "SHA256SUMS")],
        }

        def response(args, **kwargs):
            if "--paginate" in args:
                return json.dumps([[release]]) if "--slurp" in args else json.dumps(release)
            return json.dumps(legacy)

        with patch.object(planner.subprocess, "check_output", side_effect=response):
            manifests = planner.fetch_published_manifests("owner/repo")
            self.assertEqual(planner.choose_release_mode("a" * 64, manifests)["release_mode"], "full")
            with self.assertRaisesRegex(ValueError, "Published release inputs differ"):
                planner.choose_release_mode("a" * 64, manifests, release_tag="v1.4.0")

            for metadata in (
                {"attestationBundleUrl": "https://example.test/bundle"},
                {"releaseMode": "full", "imageInputFingerprint": "a" * 64},
            ):
                with self.subTest(metadata=metadata):
                    legacy.update(metadata)
                    with self.assertRaisesRegex(ValueError, "missing assets"):
                        planner.fetch_published_manifests("owner/repo")
                    for key in metadata:
                        del legacy[key]

            release["assets"] = [asset for asset in release["assets"] if asset["name"] != "payload"]
            with self.assertRaisesRegex(ValueError, "missing assets"):
                planner.fetch_published_manifests("owner/repo")

    def test_cli_emits_github_outputs_and_summary(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            published = root / "published.json"
            published.write_text("[]")
            output, summary = root / "outputs", root / "summary"
            subprocess.run(
                [sys.executable, str(SCRIPT), "--published-manifests", str(published),
                 "--github-output", str(output), "--summary", str(summary)],
                check=True, capture_output=True,
            )
            self.assertIn("release_mode=full\n", output.read_text())
            self.assertIn("current_fingerprint=", output.read_text())
            self.assertIn("no usable published image baseline", summary.read_text())


if __name__ == "__main__":
    unittest.main()
