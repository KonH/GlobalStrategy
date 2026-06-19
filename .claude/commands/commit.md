Create a git commit for the staged changes.

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

## Rules
- Subject line: short, imperative, no period
- Explain *why*, not *what* — the diff already shows what changed
- No bullet-point summaries of changed files
- Always add `Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>` trailer
