Create a git commit for the staged changes: run the project-specific version bump below, then delegate to the shared `k:commit` skill.

## Pre-commit step: version bump

Before creating the commit, increment `bundleVersion` in `ProjectSettings/ProjectSettings.asset` and stage the file.

The version format is `X.YY` (e.g. `0.47`). Increment it by treating the whole thing as hundredths:
- Parse `X` and `YY` from the current value
- Compute `hundredths = X * 100 + YY + 1`
- Reformat: `newMajor = hundredths / 100`, `newMinor = hundredths % 100`, result is `"{newMajor}.{newMinor:D2}"`

Steps:
1. `Read` `ProjectSettings/ProjectSettings.asset` to find the current `bundleVersion` line
2. `Edit` the file to replace `  bundleVersion: X.YY` with the new value (keep the two leading spaces)
3. Run `git add ProjectSettings/ProjectSettings.asset` via Bash

Always run this before committing so the version bump is included in the commit.

## Usage stats catch-all scan (best-effort)

Run `python scripts/stats/collect_usage.py --scan` (or `scripts/stats/collect_usage.ps1 -Scan` / `collect_usage.sh --scan`) once, before invoking `k:commit` below. This is the manual/cron catch-all scan from `Docs/Specs/26_07_22_17_spec-dev-stats/plan.md` §16, piggybacked onto the one command that already runs at the end of nearly every work session (both Claude and Codex) — it needs no global machine config and no scheduled task. It reads Claude Code transcripts and Codex rollouts newer than the local watermark and updates each affected `Docs/Specs/<dir>/usage.csv`. **Never block or fail the commit on this step** — if it errors (missing Python, a locked file, anything), log the error and continue straight to the commit; usage-stats freshness is not a commit precondition.

## Commit

After the version bump is staged, invoke the `k:commit` skill (from the `k` plugin) and follow it — it handles branch selection off the default branch and the commit message rules.
