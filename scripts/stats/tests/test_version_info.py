import tempfile
import unittest
from pathlib import Path

from scripts.stats.version_info import read_bundle_version


PROJECT_SETTINGS_FIXTURE = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!129 &1
PlayerSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 26
  bundleVersion: 0.1.234
  productGUID: abc123
"""


class VersionInfoTests(unittest.TestCase):
    def test_reads_bundle_version_from_project_settings(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "ProjectSettings.asset"
            path.write_text(PROJECT_SETTINGS_FIXTURE, encoding="utf-8")

            self.assertEqual("0.1.234", read_bundle_version(path))


if __name__ == "__main__":
    unittest.main()
