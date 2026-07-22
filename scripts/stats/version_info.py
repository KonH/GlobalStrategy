"""Reads the project's bundleVersion fresh from ProjectSettings.asset on every call -
never cached, per the spec's "version reflects the working tree at collection time"
acceptance criterion.
"""

import re
from pathlib import Path

DEFAULT_PROJECT_SETTINGS_PATH = Path("ProjectSettings/ProjectSettings.asset")

BUNDLE_VERSION_RE = re.compile(r"^\s*bundleVersion:\s*(\S+)", re.MULTILINE)


def read_bundle_version(path=DEFAULT_PROJECT_SETTINGS_PATH):
    text = Path(path).read_text(encoding="utf-8")
    match = BUNDLE_VERSION_RE.search(text)
    if match is None:
        raise RuntimeError(f"No bundleVersion found in {path}")
    return match.group(1)
