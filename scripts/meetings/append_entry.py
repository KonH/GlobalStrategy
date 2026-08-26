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

Usage: append_entry.py --log <path> --text "<entry text, no trailing blank line>"
"""

import argparse
import sys
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--log", required=True, help="Path to the meeting's log.md")
    parser.add_argument("--text", required=True, help="Entry text (one or more lines, no trailing blank line)")
    args = parser.parse_args()

    log_path = Path(args.log)
    entry = args.text.rstrip("\n") + "\n\n"

    with open(log_path, "a", encoding="utf-8", newline="\n") as f:
        f.write(entry)

    return 0


if __name__ == "__main__":
    sys.exit(main())
