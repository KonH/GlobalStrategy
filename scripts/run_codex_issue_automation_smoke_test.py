#!/usr/bin/env python3
"""Run the Codex issue-automation smoke test using the scheduler's Codex environment.

This intentionally uses danger-full-access so the test can verify the Git metadata writes that
workspace-write correctly forbids. Run it only from the dedicated automation checkout.
"""

import argparse
import json
import re
import subprocess
import sys

from handle_codex_feature_issues import (
    EFFORT,
    MODEL,
    SANDBOX_CHOICES,
    build_codex_arguments,
    build_codex_environment,
)


def build_prompt():
    return """Read and follow .codex/skills/codex-issue-automation-smoke-test/SKILL.md.

This is an explicitly authorized smoke test. Use the exact issue, branch, file, commit, and PR
scope defined by that skill. Do not modify any other issue, branch, file, label, or pull request.
"""


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model", default=MODEL)
    parser.add_argument("--effort", default=EFFORT,
                        choices=["minimal", "low", "medium", "high", "xhigh"])
    parser.add_argument("--sandbox", default="danger-full-access", choices=SANDBOX_CHOICES)
    args = parser.parse_args()

    process = subprocess.Popen(
        build_codex_arguments(args.model, args.effort, args.sandbox),
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding="utf-8",
        errors="replace",
        bufsize=1,
        env=build_codex_environment(),
    )
    process.stdin.write(build_prompt())
    process.stdin.close()

    result = None
    for line in process.stdout:
        line = line.rstrip()
        if not line:
            continue
        print(line)
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            continue
        item = event.get("item", {})
        if event.get("type") == "item.completed" and item.get("type") == "agent_message":
            match = re.search(r"^AUTOMATION_RESULT:\s*(COMPLETED|BLOCKED)\s*$", item.get("text", ""), re.MULTILINE)
            if match:
                result = match.group(1)

    process.wait()
    if process.returncode != 0:
        return process.returncode
    if result != "COMPLETED":
        print(f"Smoke test did not complete (result: {result or 'missing'}).", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
