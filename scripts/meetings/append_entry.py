"""Append one entry to a meeting's log.md as a true filesystem append.

Deliberately NOT a read-modify-write (i.e. never open log.md with an editor
tool that reads the whole file and writes it back) — with multiple
independent agent processes appending to the same log.md, a read-modify-write
race can silently drop another agent's concurrently-written entry entirely.
Opening in append mode ('a') means each write only ever adds bytes at the
current end of file; concurrent writers can still interleave in a different
order than their own timestamps (harmless — see the log.md protocol's "who
may write when" rules, which make true concurrent *message* writes
impossible anyway), but none of them can ever clobber another's entry.

Like `wait_for_turn.py`, this script never signals through exit codes: it
always exits 0 and reports what happened as one fixed, prefix-tagged line on
stdout, so callers branch on the prefix rather than on `$?`:

  APPENDED: <path>  the entry was written
  ERROR: <detail>   bad usage or IO problem

Usage: append_entry.py --log <path> --text "<entry text, no trailing blank line>"
"""

import argparse
import sys
from pathlib import Path


def append_entry(log_path: Path, text: str) -> None:
    """Append one entry plus the protocol's blank-line separator."""
    entry = text.rstrip("\n") + "\n\n"
    with open(log_path, "a", encoding="utf-8", newline="\n") as f:
        f.write(entry)


def run() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--log", required=True, help="Path to the meeting's log.md")
    parser.add_argument("--text", required=True, help="Entry text (one or more lines, no trailing blank line)")
    args = parser.parse_args()

    log_path = Path(args.log)
    # Append mode would happily create log.md anywhere, turning a typo'd path
    # into a silent no-op meeting. Require the meeting directory to exist.
    if not log_path.parent.is_dir():
        print(f"ERROR: meeting directory does not exist: {log_path.parent}")
        return

    append_entry(log_path, args.text)
    print(f"APPENDED: {log_path}")


def main() -> int:
    try:
        run()
    except Exception as exc:  # noqa: BLE001 - always report as fixed output
        print(f"ERROR: {type(exc).__name__}: {exc}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
