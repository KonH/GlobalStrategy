"""Gitignored local state tracking the last-scanned timestamp per provider, so a
full/incremental --scan only re-reads session files newer than the watermark.
Deleting the file is always safe: it just triggers a full rescan, and csv_store's
(session_id, stage) dedup prevents duplicate rows regardless.
"""

import json
from datetime import datetime, timezone
from pathlib import Path

DEFAULT_WATERMARK_PATH = Path(".stats/watermark.json")


def load_watermark(path=DEFAULT_WATERMARK_PATH):
    path = Path(path)
    if not path.exists():
        return {}
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return {}


def get_last_scanned(provider, path=DEFAULT_WATERMARK_PATH):
    """Returns the last-scanned timestamp (float, epoch seconds) for a provider, or
    None if never scanned - callers should then treat every file as new."""
    watermark = load_watermark(path)
    key = f"{provider}_last_scanned"
    value = watermark.get(key)
    if value is None:
        return None
    return datetime.fromisoformat(value).timestamp()


def advance_watermark(provider, when=None, path=DEFAULT_WATERMARK_PATH):
    if when is None:
        when = datetime.now(timezone.utc)
    path = Path(path)
    watermark = load_watermark(path)
    watermark[f"{provider}_last_scanned"] = when.isoformat()
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(watermark), encoding="utf-8")
