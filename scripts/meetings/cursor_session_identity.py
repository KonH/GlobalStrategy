"""Print this Cursor session's Provider, Model, and Effort from the live picker.

Reads Cursor's reactive-storage SQLite (the same store the model picker
writes). Never hardcodes Model or Effort — those change per chat.

Stdout (tab-separated, one line):
  Cursor<TAB>Grok 4.6<TAB>High

Exit codes:
  0 - printed a line.
  1 - the store could not be read or had no composer model.
"""

from __future__ import annotations

import json
import sqlite3
import sys
from pathlib import Path

REACTIVE_STORAGE_KEY = (
    "src.vs.platform.reactivestorage.browser.reactiveStorageServiceImpl"
    ".persistentStorage.applicationUser"
)

EFFORT_FALLBACK_LABELS = {
    "low": "Low",
    "medium": "Medium",
    "high": "High",
    "xhigh": "Extra High",
    "extra-high": "Extra High",
    "extra_high": "Extra High",
}


def state_db_candidates() -> list[Path]:
    home = Path.home()
    return [
        home / "AppData" / "Roaming" / "Cursor" / "User" / "globalStorage" / "state.vscdb",
        home / "Library" / "Application Support" / "Cursor" / "User" / "globalStorage" / "state.vscdb",
        home / ".config" / "Cursor" / "User" / "globalStorage" / "state.vscdb",
    ]


def find_state_db() -> Path | None:
    existing = [p for p in state_db_candidates() if p.is_file()]
    if not existing:
        return None
    return max(existing, key=lambda p: p.stat().st_mtime)


def load_reactive_storage(db_path: Path) -> dict:
    uri = db_path.resolve().as_uri() + "?mode=ro"
    con = sqlite3.connect(uri, uri=True)
    try:
        row = con.execute(
            "SELECT value FROM ItemTable WHERE key = ?",
            (REACTIVE_STORAGE_KEY,),
        ).fetchone()
    finally:
        con.close()
    if not row:
        raise FileNotFoundError(f"missing key {REACTIVE_STORAGE_KEY}")
    raw = row[0]
    text = raw.decode("utf-8") if isinstance(raw, bytes) else str(raw)
    data = json.loads(text)
    if not isinstance(data, dict):
        raise ValueError("reactive storage JSON is not an object")
    return data


def find_named_model(obj: object, model_id: str) -> dict | None:
    if isinstance(obj, dict):
        if obj.get("name") == model_id and "parameterDefinitions" in obj:
            return obj
        for value in obj.values():
            found = find_named_model(value, model_id)
            if found is not None:
                return found
    elif isinstance(obj, list):
        for value in obj:
            found = find_named_model(value, model_id)
            if found is not None:
                return found
    return None


def humanize_model_id(model_id: str) -> str:
    return " ".join(part.capitalize() for part in model_id.replace("_", "-").split("-") if part)


def model_label(model_id: str, catalog_entry: dict | None) -> str:
    display = ""
    if catalog_entry:
        display = str(catalog_entry.get("clientDisplayName") or "").strip()
    if display.lower().startswith("cursor "):
        display = display[7:].strip()
    return display or humanize_model_id(model_id)


def effort_label(effort_value: str, catalog_entry: dict | None) -> str:
    if catalog_entry:
        for param in catalog_entry.get("parameterDefinitions") or []:
            if param.get("id") != "effort":
                continue
            values = (
                (param.get("parameterType") or {})
                .get("enumParameter", {})
                .get("values")
                or []
            )
            for option in values:
                if option.get("value") == effort_value:
                    name = str(option.get("displayName") or "").strip()
                    if name:
                        return name
    return EFFORT_FALLBACK_LABELS.get(effort_value.lower(), effort_value)


def identity_from_storage(data: dict) -> tuple[str, str, str]:
    composer = (
        (data.get("aiSettings") or {})
        .get("modelConfig", {})
        .get("composer")
        or {}
    )
    selected = composer.get("selectedModels") or []
    selected0 = selected[0] if selected and isinstance(selected[0], dict) else {}
    model_id = str(selected0.get("modelId") or composer.get("modelName") or "").strip()
    if not model_id:
        raise ValueError("composer model picker has no modelId")

    effort_value = ""
    for param in selected0.get("parameters") or []:
        if isinstance(param, dict) and param.get("id") == "effort":
            effort_value = str(param.get("value") or "").strip()
            break

    catalog_entry = find_named_model(data.get("availableDefaultModels2"), model_id)
    if catalog_entry is None:
        catalog_entry = find_named_model(data, model_id)

    model = model_label(model_id, catalog_entry)
    effort = effort_label(effort_value, catalog_entry) if effort_value else "unknown"
    return "Cursor", model, effort


def main() -> int:
    db_path = find_state_db()
    if db_path is None:
        print("cursor state.vscdb not found", file=sys.stderr)
        return 1
    try:
        provider, model, effort = identity_from_storage(load_reactive_storage(db_path))
    except Exception as exc:
        print(f"failed to read Cursor model picker: {exc}", file=sys.stderr)
        return 1
    print(f"{provider}\t{model}\t{effort}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
