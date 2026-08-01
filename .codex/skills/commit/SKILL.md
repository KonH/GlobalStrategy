---
name: commit
description: Create a repository commit using GlobalStrategy's required workflow. Use when the user asks to commit staged or current changes in the GlobalStrategy repository.
---

# Commit Changes

1. Read `ProjectSettings/ProjectSettings.asset` and locate `bundleVersion`.
2. Split its `X.YYY` value at the period. Keep `X` unchanged and increment only the unbounded integer `YYY`; do not treat it as a decimal.
3. Edit the version line, then stage `ProjectSettings/ProjectSettings.asset` with `git add`.
4. Run `python scripts/stats/collect_usage.py --scan` once. Treat failures as non-blocking: report them, then continue.
5. Follow the available `k:commit` skill to select a branch if necessary, review the staged scope, choose an intentional commit message, and create the commit.

Do not create a commit before the version bump is staged. Do not change the major bundle-version marker.
