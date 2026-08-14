---
name: milestone-complete
description: Generate a milestone-completion report (summary.md, stats_code.md, stats_dev.md) under Docs/Milestones/<major>_<version-name>/, comparing code LoC and Docs/Specs/*/usage.csv dev stats against the previous milestone. Load when the user asks to close out, wrap up, or report on a project milestone/version.
---

# Milestone Complete

Produces a point-in-time report of everything shipped in the current major-version
milestone: codebase size (LoC per tracked extension) and development cost/effort
(aggregated from every spec's `usage.csv`, see `Docs/Specs/26_07_22_17_spec-dev-stats/`),
each compared against the previous milestone if one exists.

This does **not** bump `bundleVersion`'s major digit or rename `_versionName` in
`Assets/Scenes/MainMenu.unity` — per `.claude/commands/commit.md`, only a human
decides when the milestone actually turns over. This skill just snapshots stats for
whatever milestone (major version + version name) is currently set, using today as
the report's end date. Re-running it before the next major-version bump simply
regenerates the same milestone's report with an updated end date.

## What it does

Runs `scripts/milestones/generate_milestone_report.py`, which:

1. Reads the current milestone identity: major version (`bundleVersion`'s `X` in
   `X.YYY`, from `ProjectSettings/ProjectSettings.asset`) and version name
   (`_versionName` on `MainMenuDocument` in `Assets/Scenes/MainMenu.unity`).
2. Resolves the output directory: `Docs/Milestones/<major>_<slug-of-name>/`.
3. Resolves the date range: `end_date` = today; `start_date` = the day after the
   previous milestone's `end_date` (read from its `summary.md`), or the repo's first
   commit date if no previous milestone exists.
4. Writes `stats_code.md` — LoC + file count for `.cs`, `.py`, `.sh`, `.ps1`,
   `.prefab`, `.unity` (Unity's actual scene extension), `.asset`, `.json`, `.md`
   (via `git ls-files`, so untracked/ignored files never skew the count), diffed
   against the previous milestone's numbers.
5. Writes `stats_dev.md` — every spec whose `Docs/Specs/<YY_MM_DD_HH>_<name>/`
   timestamp falls in the date range, its `usage.csv` rows aggregated into one table,
   plus meta stats (specs count, provider/model share, min/max/avg spec/plan size in
   tokens, avg cost per stage, total cost/tokens), diffed against the previous
   milestone's numbers.
6. Writes `summary.md` — `# <major>. <name>` header, the date range with duration,
   and a `## Dev Notes` section with short insight bullets pulled from both stats
   files.

Each generated file also carries a hidden `<!-- milestone-meta: {...} -->` /
`<!-- milestone-stats-code: {...} -->` / `<!-- milestone-stats-dev: {...} -->`
HTML-comment JSON block — this is how the *next* milestone run finds this one's exact
prior numbers without re-parsing markdown tables. Don't strip these comments when
editing a generated file by hand.

## Steps

1. Run, in a single Bash call (no `cd`, matching this repo's shell rule):

   ```
   python3 scripts/milestones/generate_milestone_report.py
   ```

   Useful overrides (rarely needed — defaults cover the normal case):
   - `--major X` / `--name "..."` — force the milestone identity instead of reading
     it from `ProjectSettings.asset` / `MainMenu.unity`.
   - `--start-date YYYY-MM-DD` / `--end-date YYYY-MM-DD` — force the date range.
   - `--out <path>` — force the output directory.

2. Read the three generated files under the printed `Docs/Milestones/<dir>/` path
   and present the `summary.md` content (and anything notable from the two stats
   files) to the user.
3. Do not commit the generated files unless the user explicitly asks — this is a
   reporting snapshot, not something that needs approval-gated planning, but it's
   still new tracked content the user should see before it lands in git history.

## Notes

- Pure stdlib Python (mirrors `scripts/stats/`'s no-third-party-deps convention) —
  no LLM calls, safe to re-run.
- If `Docs/Milestones/` has no prior entries yet (first-ever run), both stats files
  clearly say so instead of fabricating a comparison — that's expected, not a bug.
- A spec with no `usage.csv` (shouldn't happen post-`spec-dev-stats`, but possible for
  a brand-new spec mid-session) is silently skipped in `stats_dev.md`'s aggregation.
