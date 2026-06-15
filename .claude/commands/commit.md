Create a git commit for the staged changes.

## Pre-commit step

Before creating the commit, run the version bump script:

```powershell
powershell -File .claude/bump_version.ps1
```

This increments `bundleVersion` in `ProjectSettings/ProjectSettings.asset` and stages the file automatically. Always run this first so the version bump is included in the commit.

## Rules
- Subject line: short, imperative, no period
- Explain *why*, not *what* — the diff already shows what changed
- No bullet-point summaries of changed files
- Always add `Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>` trailer
