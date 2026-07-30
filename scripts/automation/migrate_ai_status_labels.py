#!/usr/bin/env python3
"""One-shot migration to shared ``ai-*`` automation status labels.

Creates ``ai-in-progress``, ``ai-need-attention``, and ``ai-complete``; rewrites those
status labels on open issues/PRs that still carry provider-prefixed status labels; then
deletes the nine obsolete provider status labels.

Run once from a machine with repo write access (this cloud agent's ``gh`` token is
read-only for label mutations):

  python3 scripts/automation/migrate_ai_status_labels.py
"""

import subprocess
import sys

OWNER = "KonH"
REPO = "GlobalStrategy"
NEW_LABELS = (
    ("ai-in-progress", "FBCA04", "Automation actively working this item"),
    ("ai-need-attention", "D93F0B", "Automation waiting on the owner"),
    ("ai-complete", "0E8A16", "Automation finished this item's prompt"),
)
OLD_TO_NEW = {
    "claude-in-progress": "ai-in-progress",
    "codex-in-progress": "ai-in-progress",
    "cursor-in-progress": "ai-in-progress",
    "claude-needs-attention": "ai-need-attention",
    "codex-needs-attention": "ai-need-attention",
    "cursor-needs-attention": "ai-need-attention",
    "claude-complete": "ai-complete",
    "codex-complete": "ai-complete",
    "cursor-complete": "ai-complete",
}


def run_gh(args, check=True):
    result = subprocess.run(["gh", *args], capture_output=True, text=True)
    if check and result.returncode != 0:
        raise RuntimeError(f"gh {' '.join(args)} failed: {result.stderr.strip()}")
    return result


def create_labels():
    for name, color, description in NEW_LABELS:
        result = run_gh([
            "label", "create", name, "--repo", f"{OWNER}/{REPO}",
            "--color", color, "--description", description, "--force",
        ], check=False)
        if result.returncode != 0:
            print(f"WARN: could not create label {name}: {result.stderr.strip()}", file=sys.stderr)
        else:
            print(f"Ensured label {name}")


def list_open_with_label(label):
    numbers = []
    for kind, command in (("issue", "issue"), ("pr", "pr")):
        result = run_gh([
            command, "list", "--repo", f"{OWNER}/{REPO}", "--label", label,
            "--state", "open", "--json", "number",
        ])
        import json
        numbers.extend((kind, item["number"]) for item in json.loads(result.stdout))
    return numbers


def migrate_open_items():
    for old, new in OLD_TO_NEW.items():
        for kind, number in list_open_with_label(old):
            print(f"Migrating {kind} #{number}: {old} -> {new}")
            run_gh([
                "api", f"repos/{OWNER}/{REPO}/issues/{number}/labels",
                "-f", f"labels[]={new}",
            ])
            run_gh([
                "api", "-X", "DELETE",
                f"repos/{OWNER}/{REPO}/issues/{number}/labels/{old}",
            ], check=False)


def delete_old_labels():
    for old in OLD_TO_NEW:
        result = run_gh(["label", "delete", old, "--repo", f"{OWNER}/{REPO}", "--yes"], check=False)
        if result.returncode != 0:
            print(f"WARN: could not delete label {old}: {result.stderr.strip()}", file=sys.stderr)
        else:
            print(f"Deleted label {old}")


def main():
    create_labels()
    migrate_open_items()
    delete_old_labels()
    print("Migration finished.")


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print(f"Migration failed: {exc}", file=sys.stderr)
        raise SystemExit(1)
