#!/usr/bin/env python3
"""Backfills missing `implement`-stage rows in Docs/Specs/<dir>/usage.csv for specs
whose spec/plan rows were backfilled from git history (see the "Backfill usage.csv
for pre-existing specs missing stats" commit) but never got an `implement` row,
because that backfill's keyword-overlap heuristic for locating the implementing
commit(s) produced false positives on generic words and ties, so those candidates
were deliberately left unwritten rather than guessed.

This script uses a different, more precise signal: a plan.md's own Steps checkboxes.
`/implement` checks off `- [ ]` -> `- [x]` as it completes each step, so the commit
where a plan's checked-count last increases to its final maximum is - for the common
"implement was done in one continuous pass" case - the actual commit (or the last of
a tight run) that did the real work. That single commit's `Co-Authored-By` trailer is
then mapped to (provider, model) via TRAILER_MODEL_MAP, an empirically-derived table
built by cross-referencing every trailer already resolved by the existing spec/plan
backfill's own rows (100% consistent - no trailer maps to more than one (provider,
model) pair anywhere in current history).

Explicitly NOT done: a majority vote across every commit between plan-write and
checkbox-completion. That range was tested against real history (see chat) and found
to contain dozens of commits from unrelated specs worked in the same window, since
this repo does not isolate spec work onto branches - a vote over that range would be
dominated by whatever else was busiest that week, not by who actually did this spec's
work. Anchoring to the single checkbox-completing commit avoids that noise entirely,
mirroring the same one-commit-is-the-anchor trust model the existing spec/plan backfill
already uses successfully.

Specs left untouched by design (still a documented gap in the milestone-complete
report, not silently guessed):
  - plan.md has no checkbox syntax at any revision, or its checked-count never
    increases after creation (steps were never checked off, or were already checked
    at authoring time - no signal to anchor to).
  - plan.md has no discoverable git history at all (very recent specs whose files
    were introduced via a squash-merge that --follow can't trace).
  - the checkbox-completion commit's trailer isn't in TRAILER_MODEL_MAP (an unseen
    trailer variant - printed as a warning rather than guessed).

Usage:
  python scripts/stats/backfill_implement_rows.py --dry-run   # preview only, writes nothing
  python scripts/stats/backfill_implement_rows.py             # writes rows
"""

import argparse
import re
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent.parent))
from scripts.stats.collect_usage import file_size_kb  # noqa: E402
from scripts.stats.csv_store import upsert_row  # noqa: E402
from scripts.stats.token_estimate import file_tokens  # noqa: E402
from scripts.stats.version_info import read_bundle_version  # noqa: E402

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
SPECS_DIR = REPO_ROOT / "Docs" / "Specs"

# Empirically derived from every trailer the existing spec/plan backfill already
# resolved (cross-referenced against its own recorded provider/model - see the
# script docstring). Every trailer below maps to exactly one (provider, model) pair
# across every occurrence found; nothing here is a guess.
TRAILER_MODEL_MAP = {
    "Claude Sonnet 5 <noreply@anthropic.com>": ("claude", "claude-sonnet-5"),
    "Claude Sonnet 4.6 <noreply@anthropic.com>": ("claude", "claude-sonnet-4-6"),
    "Claude Fable 5 <noreply@anthropic.com>": ("claude", "claude-fable-5"),
    "Cursor Grok 4.5 <noreply@cursor.com>": ("cursor", "cursor-grok-4.5-high"),
    "Cursor <cursoragent@cursor.com>": ("cursor", "cursor-grok-4.5-high"),
    "Codex <noreply@openai.com>": ("codex", "gpt-5.6-sol"),
    "Codex GPT-5 <noreply@openai.com>": ("codex", "gpt-5"),
    "OpenAI Codex <noreply@openai.com>": ("codex", "gpt-5.6-sol"),
}

# Same "no trailer at all" fallback the original backfill used, per its own commit
# message: "A handful of commits carry no Co-Authored-By trailer at all ... those
# rows default to claude/claude-sonnet-5, the repo's overwhelming norm."
DEFAULT_PROVIDER_MODEL = ("claude", "claude-sonnet-5")

CHECKED_RE = re.compile(r"^- \[x\]", re.MULTILINE)
UNCHECKED_RE = re.compile(r"^- \[ \]", re.MULTILINE)
TRAILER_RE = re.compile(r"Co-Authored-By:\s*(.+)")


def run(args, cwd=REPO_ROOT):
    return subprocess.run(args, cwd=cwd, capture_output=True, text=True).stdout


def base_stage(stage):
    return re.sub(r"_user_input_\d+$", "", stage)


def specs_missing_implement():
    import csv

    result = []
    for spec_dir in sorted(SPECS_DIR.iterdir()):
        usage_csv = spec_dir / "usage.csv"
        if not usage_csv.exists():
            continue
        with usage_csv.open(newline="", encoding="utf-8") as f:
            rows = list(csv.DictReader(f))
        stages = {base_stage(r["stage"]) for r in rows}
        if "implement" not in stages:
            result.append(spec_dir)
    return result


def plan_history(spec_dir):
    """[(commit, path_at_that_commit), ...] oldest -> newest, following renames."""
    plan_path = f"Docs/Specs/{spec_dir.name}/plan.md"
    out = run(["git", "log", "--follow", "--name-only", "--format=COMMIT:%H", "--", plan_path]).splitlines()
    entries = []
    current_commit = None
    for line in out:
        if line.startswith("COMMIT:"):
            current_commit = line[len("COMMIT:"):]
        elif line.strip():
            entries.append((current_commit, line.strip()))
    entries.reverse()
    return entries


def checkbox_counts(text):
    return len(CHECKED_RE.findall(text)), len(UNCHECKED_RE.findall(text))


def find_checkbox_completion_commit(spec_dir):
    """Returns the commit where plan.md's checked-checkbox count last increases to
    its final maximum, or None if no such event exists in its history."""
    history = plan_history(spec_dir)
    prev_checked = None
    last_increase_commit = None
    for commit, path in history:
        text = run(["git", "show", f"{commit}:{path}"])
        checked, unchecked = checkbox_counts(text)
        if checked == 0 and unchecked == 0:
            continue  # this revision predates checkbox syntax, or isn't a plan.md shape
        if prev_checked is not None and checked > prev_checked:
            last_increase_commit = commit
        prev_checked = checked
    return last_increase_commit


def trailer_for_commit(commit):
    message = run(["git", "show", "-s", "--format=%B", commit])
    match = TRAILER_RE.search(message)
    return match.group(1).strip() if match else None


def commit_author_iso(commit):
    return run(["git", "show", "-s", "--format=%aI", commit]).strip()


def commit_diff_lines(commit):
    shortstat = run(["git", "show", "--shortstat", commit])
    total = 0
    for line in shortstat.splitlines():
        if "changed" not in line:
            continue
        for part in line.split(","):
            part = part.strip()
            if "insertion" in part or "deletion" in part:
                total += int(part.split()[0])
    return total


def build_implement_row(spec_dir, commit, provider, model):
    commit_iso = commit_author_iso(commit)
    return {
        "spec_id": spec_dir.name,
        "version": read_bundle_version(),
        "stage": "implement",
        "mode": "automated",
        "context": "",
        "start": commit_iso,
        "end": commit_iso,
        "provider": provider,
        "model": model,
        "effort": "",
        "cost_usd": "",
        "input_tokens": "",
        "cached_input_tokens": "",
        "output_tokens": "",
        "spec_size_kb": file_size_kb(spec_dir / "spec.md"),
        "spec_size_tokens": file_tokens(spec_dir / "spec.md"),
        "plan_size_kb": file_size_kb(spec_dir / "plan.md"),
        "plan_size_tokens": file_tokens(spec_dir / "plan.md"),
        "prd_size_kb": file_size_kb(REPO_ROOT / ".ralph" / "prd.md"),
        "diff_lines": commit_diff_lines(commit),
        "session_id": "backfilled",
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dry-run", action="store_true", help="Preview what would be written without writing anything")
    args = parser.parse_args()

    candidates = specs_missing_implement()
    written = []
    defaulted = []
    skipped_no_signal = []
    skipped_unknown_trailer = []

    for spec_dir in candidates:
        if not (spec_dir / "plan.md").exists():
            skipped_no_signal.append((spec_dir.name, "no plan.md"))
            continue

        commit = find_checkbox_completion_commit(spec_dir)
        if commit is None:
            skipped_no_signal.append((spec_dir.name, "no checkbox-completion commit"))
            continue

        trailer = trailer_for_commit(commit)
        if trailer is None:
            provider, model = DEFAULT_PROVIDER_MODEL
            defaulted.append((spec_dir.name, commit[:8]))
        elif trailer in TRAILER_MODEL_MAP:
            provider, model = TRAILER_MODEL_MAP[trailer]
        else:
            skipped_unknown_trailer.append((spec_dir.name, commit[:8], trailer))
            continue

        row = build_implement_row(spec_dir, commit, provider, model)
        written.append((spec_dir.name, commit[:8], provider, model, row["diff_lines"]))
        if not args.dry_run:
            upsert_row(spec_dir, row)

    print(f"{'Would write' if args.dry_run else 'Wrote'} {len(written)} implement rows:")
    for spec_id, commit, provider, model, diff_lines in written:
        print(f"  {spec_id:55s} {commit} {provider}/{model:22s} diff_lines={diff_lines}")

    if defaulted:
        print(f"\n{len(defaulted)} of those had no Co-Authored-By trailer, defaulted to {DEFAULT_PROVIDER_MODEL[0]}/{DEFAULT_PROVIDER_MODEL[1]}:")
        for spec_id, commit in defaulted:
            print(f"  {spec_id:55s} {commit}")

    if skipped_unknown_trailer:
        print(f"\n{len(skipped_unknown_trailer)} skipped - checkbox-completion commit has an unrecognized trailer (add it to TRAILER_MODEL_MAP to resolve):")
        for spec_id, commit, trailer in skipped_unknown_trailer:
            print(f"  {spec_id:55s} {commit} trailer={trailer!r}")

    print(f"\n{len(skipped_no_signal)} left as a documented gap (no reliable signal found):")
    for spec_id, reason in skipped_no_signal:
        print(f"  {spec_id:55s} {reason}")


if __name__ == "__main__":
    main()
