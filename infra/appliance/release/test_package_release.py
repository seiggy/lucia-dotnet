import hashlib
import json
import pathlib
import subprocess
import tempfile
import unittest


class PackageReleaseTests(unittest.TestCase):
    def test_manifest_describes_chunked_update_channels(self) -> None:
        script = pathlib.Path(__file__).with_name("package_release.py")

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = pathlib.Path(temporary_directory)
            installer = root / "installer.img.zst"
            lucia = root / "lucia.tar.zst"
            os_update = root / "os.tar.zst"
            output = root / "release"
            installer.write_bytes(b"installer-image-payload")
            lucia.write_bytes(b"lucia")
            os_update.write_bytes(b"os-update-payload")

            subprocess.run(
                [
                    "python3",
                    str(script),
                    "--repository",
                    "seiggy/lucia-dotnet",
                    "--tag",
                    "v1.2.3",
                    "--installer",
                    str(installer),
                    "--lucia",
                    str(lucia),
                    "--os",
                    str(os_update),
                    "--lucia-version",
                    "1.2.4",
                    "--os-version",
                    "2.0.0",
                    "--output-dir",
                    str(output),
                    "--chunk-bytes",
                    "10",
                ],
                check=True,
            )

            manifest = json.loads(
                (output / "lucia-appliance-manifest.json").read_text()
            )
            self.assertEqual(manifest["schemaVersion"], 1)
            self.assertEqual(manifest["version"], "1.2.3")
            self.assertEqual(
                manifest["attestationBundleUrl"],
                "https://github.com/seiggy/lucia-dotnet/releases/download/"
                "v1.2.3/lucia-appliance-attestations.jsonl",
            )
            self.assertEqual(manifest["channels"]["lucia"]["version"], "1.2.4")
            self.assertEqual(manifest["channels"]["os"]["version"], "2.0.0")
            self.assertEqual(manifest["compatibility"]["jetsonLinux"], "36.5.2")
            self.assertEqual(manifest["compatibility"]["layoutVersion"], 1)
            self.assertEqual(manifest["compatibility"]["cuda"], "12.6")
            self.assertEqual(manifest["compatibility"]["onnxRuntime"], "1.23.2")
            self.assertEqual(
                manifest["releaseNotesUrl"],
                "https://github.com/seiggy/lucia-dotnet/releases/tag/v1.2.3",
            )
            self.assertEqual(
                manifest["releaseApi"],
                "https://api.github.com/repos/seiggy/lucia-dotnet/releases/tags/v1.2.3",
            )
            self.assertEqual(
                manifest["channels"]["os"]["requires"]["minimumLuciaVersion"],
                "1.2.4",
            )
            self.assertFalse(manifest["channels"]["lucia"]["requires"]["reboot"])
            self.assertTrue(manifest["channels"]["os"]["requires"]["reboot"])
            self.assertEqual(
                manifest["channels"]["os"]["requires"]["cuda"],
                "12.6",
            )
            self.assertEqual(len(manifest["channels"]["installer"]["parts"]), 3)
            self.assertEqual(len(manifest["channels"]["lucia"]["parts"]), 1)
            self.assertEqual(len(manifest["channels"]["os"]["parts"]), 2)

            for channel_name, source in {
                "installer": installer,
                "lucia": lucia,
                "os": os_update,
            }.items():
                channel = manifest["channels"][channel_name]
                payload = b"".join(
                    (output / part["name"]).read_bytes()
                    for part in channel["parts"]
                )
                self.assertEqual(payload, source.read_bytes())
                self.assertEqual(
                    channel["sha256"],
                    hashlib.sha256(source.read_bytes()).hexdigest(),
                )
                for part in channel["parts"]:
                    self.assertEqual(
                        part["url"],
                        "https://github.com/seiggy/lucia-dotnet/releases/download/"
                        f"v1.2.3/{part['name']}",
                    )


if __name__ == "__main__":
    unittest.main()
