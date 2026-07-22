"""Loads/dedup-merges/writes a spec's usage.csv, keyed by (session_id, stage)."""

import csv
from pathlib import Path

COLUMNS = [
    "spec_id", "version", "stage", "mode", "context", "start", "end",
    "provider", "model", "cost_usd", "input_tokens", "cached_input_tokens",
    "output_tokens", "spec_size_kb", "plan_size_kb", "diff_lines", "session_id",
]


def usage_csv_path(spec_dir):
    return Path(spec_dir) / "usage.csv"


def _load_rows(path):
    if not path.exists():
        return []
    with path.open("r", encoding="utf-8", newline="") as f:
        return list(csv.DictReader(f))


def upsert_row(spec_dir, row):
    """row must contain at least session_id and stage; missing columns are written
    as empty cells. Overwrites in place (preserving file order) if (session_id,
    stage) already exists, else appends. Rewrites the whole file each call.
    """
    path = usage_csv_path(spec_dir)
    rows = _load_rows(path)

    key = (row.get("session_id"), row.get("stage"))
    index = next(
        (i for i, existing in enumerate(rows)
         if (existing.get("session_id"), existing.get("stage")) == key),
        None,
    )

    full_row = {column: row.get(column, "") for column in COLUMNS}
    if index is not None:
        rows[index] = full_row
    else:
        rows.append(full_row)

    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=COLUMNS)
        writer.writeheader()
        writer.writerows(rows)
