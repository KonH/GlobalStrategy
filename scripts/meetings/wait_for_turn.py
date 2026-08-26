"""Wait on a meeting's log.md until something actionable happens.

Shared by all three meeting skills (Claude/Codex/Cursor) so the wait
mechanism is identical regardless of which tool is polling.

Output contract
---------------
This script **never signals through exit codes** — it always exits 0 (except
on an unhandled crash) and reports what happened as one fixed, prefix-tagged
line on stdout. Callers branch on the prefix, never on `$?`/`$LASTEXITCODE`:

  TURN: <line>      your turn grant is outstanding (--await-turn)
  ACK: <line>       an ack line (auto-written by --await-turn, or observed
                    by --await-ack)
  MESSAGE: <line>   the speaker's real message landed (--await-message)
  MATCH: <line>     a new line matched --pattern
  ENDED: <line>     the meeting is over
  KICKED: <line>    you were kicked; stop participating
  TIMEOUT: <detail> nothing actionable within --timeout; just run it again
  ERROR: <detail>   bad usage or IO problem

State-derived modes vs. new-lines-only mode
-------------------------------------------
`--await-turn`, `--await-ack` and `--await-message` derive their answer from
the *whole* file every poll, anchored on the last `[meeting] turn: <Name>`
line. They are therefore immune to the baseline race that `--pattern` has:
`--pattern` snapshots the line count at startup and only ever matches lines
appended after that, so anything written between the caller's own last write
and this process starting is invisible to it. That race produced real false
kicks in the 01_world-domination retro — a participant that acked *quickly*,
before the owner's waiter had started, was recorded as never having acked.
Never use `--pattern` to wait for an ack or a reply; use the `--await-*`
mode for it.

`--await-turn` also writes the ack itself, the instant it sees the grant, so
no model turn sits between "grant seen" and "ack written". That removes the
participant-side latency the ack timeout used to punish.
"""

import argparse
import re
import sys
import time
from datetime import datetime
from pathlib import Path

from append_entry import append_entry

ENDED_RE = re.compile(r"\[meeting\] ended:")


def read_lines(log_path: Path) -> list[str]:
    if not log_path.exists():
        return []
    return log_path.read_text(encoding="utf-8").splitlines()


def last_index(lines: list[str], regex: re.Pattern[str]) -> int:
    """Index of the last line matching `regex`, or -1 when there is none."""
    for i in range(len(lines) - 1, -1, -1):
        if regex.search(lines[i]):
            return i
    return -1


def name_patterns(name: str) -> dict[str, re.Pattern[str]]:
    quoted = re.escape(name)
    return {
        "turn": re.compile(rf"\[meeting\] turn: {quoted}\s*$"),
        "ack": re.compile(rf"\[meeting\] ack: {quoted}\s*$"),
        "kicked": re.compile(rf"\[meeting\] kicked: {quoted}(?:\s|$)"),
        "message": re.compile(rf"^\d{{2}}:\d{{2}} {quoted}: "),
    }


def check_await_turn(lines: list[str], name: str, log_path: Path) -> list[str] | None:
    """Participant: report an outstanding turn grant, acking it immediately."""
    pat = name_patterns(name)
    turn_i = last_index(lines, pat["turn"])
    ended_i = last_index(lines, ENDED_RE)
    kicked_i = last_index(lines, pat["kicked"])
    ack_i = last_index(lines, pat["ack"])

    if ended_i > turn_i:
        return [f"ENDED: {lines[ended_i]}"]
    if kicked_i > turn_i:
        return [f"KICKED: {lines[kicked_i]}"]
    if turn_i >= 0 and turn_i > ack_i:
        # Write the ack before returning: the caller must not have to spend a
        # model turn on it. The timestamp comes from the OS clock here, so it
        # is also never a guessed one.
        ack_line = f"{datetime.now().strftime('%H:%M')} [meeting] ack: {name}"
        append_entry(log_path, ack_line)
        return [f"TURN: {lines[turn_i]}", f"ACK: {ack_line}"]
    return None


def check_await_ack(lines: list[str], name: str) -> list[str] | None:
    """Owner: has `name` acked the turn grant it currently holds?"""
    pat = name_patterns(name)
    turn_i = last_index(lines, pat["turn"])
    if turn_i < 0:
        return None
    ack_i = last_index(lines, pat["ack"])
    if ack_i > turn_i:
        return [f"ACK: {lines[ack_i]}"]
    return None


def check_await_message(lines: list[str], name: str) -> list[str] | None:
    """Owner: has `name` posted a real message since its turn grant?"""
    pat = name_patterns(name)
    turn_i = last_index(lines, pat["turn"])
    if turn_i < 0:
        return None
    msg_i = last_index(lines, pat["message"])
    if msg_i > turn_i:
        return [f"MESSAGE: {lines[msg_i]}"]
    return None


def run() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--log", required=True, help="Path to the meeting's log.md")
    parser.add_argument("--await-turn", metavar="NAME", help="Participant: wait for your own turn grant and ack it automatically")
    parser.add_argument("--await-ack", metavar="NAME", help="Owner: wait for NAME to ack the turn grant it holds")
    parser.add_argument("--await-message", metavar="NAME", help="Owner: wait for NAME's message after its turn grant")
    parser.add_argument("--pattern", help="Wait for a NEW line matching this regex (not for acks/replies — see module docstring)")
    parser.add_argument("--timeout", type=float, default=300.0, help="Seconds to wait before reporting TIMEOUT")
    parser.add_argument("--poll", type=float, default=3.0, help="Seconds between checks")
    args = parser.parse_args()

    modes = [bool(args.await_turn), bool(args.await_ack), bool(args.await_message), bool(args.pattern)]
    if sum(modes) != 1:
        print("ERROR: pass exactly one of --await-turn / --await-ack / --await-message / --pattern")
        return

    log_path = Path(args.log)

    regex = None
    if args.pattern:
        try:
            regex = re.compile(args.pattern)
        except re.error as exc:
            print(f"ERROR: invalid --pattern: {exc}")
            return

    # --pattern is new-lines-only by design; the --await-* modes deliberately
    # take no baseline so they can see what is already in the file.
    baseline = len(read_lines(log_path)) if regex else 0
    deadline = time.monotonic() + args.timeout

    while True:
        lines = read_lines(log_path)

        result = None
        if args.await_turn:
            result = check_await_turn(lines, args.await_turn, log_path)
        elif args.await_ack:
            result = check_await_ack(lines, args.await_ack)
        elif args.await_message:
            result = check_await_message(lines, args.await_message)
        else:
            for line in lines[baseline:]:
                if regex.search(line):
                    result = [f"MATCH: {line}"]
                    break

        if result:
            for line in result:
                print(line)
            return

        remaining = deadline - time.monotonic()
        if remaining <= 0:
            print(f"TIMEOUT: nothing actionable within {args.timeout}s")
            return

        time.sleep(min(args.poll, remaining))


def main() -> int:
    try:
        run()
    except Exception as exc:  # noqa: BLE001 - always report as fixed output
        print(f"ERROR: {type(exc).__name__}: {exc}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
