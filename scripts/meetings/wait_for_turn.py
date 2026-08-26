"""Block until a meeting log.md gets a line matching a pattern, or time out.

Shared by all three meeting skills (Claude/Codex/Cursor) so the wait
mechanism is identical regardless of which tool is polling. Stateless: each
invocation takes its baseline from the log's current line count, so repeated
invocations never re-match an old line.

Exit codes:
  0 - a new line matched `--pattern`; that line is printed to stdout.
  2 - timed out without a match.
  1 - usage/IO error.
"""

import argparse
import re
import sys
import time
from pathlib import Path


def read_lines(log_path: Path) -> list[str]:
    if not log_path.exists():
        return []
    return log_path.read_text(encoding="utf-8").splitlines()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--log", required=True, help="Path to the meeting's log.md")
    parser.add_argument("--pattern", required=True, help="Regex a new line must match")
    parser.add_argument("--timeout", type=float, default=300.0, help="Seconds to wait before giving up")
    parser.add_argument("--poll", type=float, default=5.0, help="Seconds between checks")
    args = parser.parse_args()

    log_path = Path(args.log)
    try:
        regex = re.compile(args.pattern)
    except re.error as exc:
        print(f"invalid --pattern: {exc}", file=sys.stderr)
        return 1

    baseline = len(read_lines(log_path))
    deadline = time.monotonic() + args.timeout

    while True:
        for line in read_lines(log_path)[baseline:]:
            if regex.search(line):
                print(line)
                return 0

        remaining = deadline - time.monotonic()
        if remaining <= 0:
            print(f"TIMEOUT after {args.timeout}s waiting for pattern: {args.pattern}", file=sys.stderr)
            return 2

        time.sleep(min(args.poll, remaining))


if __name__ == "__main__":
    sys.exit(main())
