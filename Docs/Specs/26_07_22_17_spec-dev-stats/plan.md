# Plan: Spec Development Statistics Collection

## Spec

Source: `Docs/Specs/26_07_22_17_spec-dev-stats/spec.md`.

**Intent.** Collect per-spec development statistics (LLM token usage, duration, cost, output sizes) automatically from Claude Code and Codex CLI session logs into a `usage.csv` file inside each spec directory, so the real cost/effort of developing each feature is comparable across providers, models, and workflow modes — with zero LLM usage spent on collection itself.

**Design.** `scripts/stats/collect_usage.py` (pure Python stdlib, no LLM calls, no third-party deps) plus thin `.sh`/`.ps1` wrappers. It scans Claude Code transcripts (`~/.claude/projects/<project-slug>/<session-id>.jsonl`) and Codex rollouts (`~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl`) newer than a watermark, segments each session into `spec`/`plan`/`implement` stages (plus `<stage>_user_input_N` sub-stages for human turns after a stage's first completed response), sums exact per-API-call token usage per stage, attributes each stage segment to a spec directory (via `Docs/Specs/<dir>/` file writes within the segment, falling back to `ralph/<spec_id>` branch-name matching), computes `cost_usd` from a checked-in hand-maintained pricing table, and appends/updates rows in `Docs/Specs/<dir>/usage.csv`, deduped by `(session_id, stage)`. Four invocation points, all "freshness optimizations" only — the scanner alone is correct on its own: (1) a Claude Code `SessionEnd` hook, (2) direct calls from `scripts/automation/{claude,codex}/ralph.py` and `scripts/automation/{claude,codex}/handle_issues.py` using data those wrappers already have, (3) Codex `notify` config, (4) a manual/cron catch-all scan.

**`usage.csv` columns** (verbatim from spec): `spec_id`, `version` (bundleVersion from `ProjectSettings/ProjectSettings.asset`), `stage`, `mode` (`automated`|`interactive`), `context` (`fresh`|`continued`), `start`, `end` (UTC ISO 8601), `provider` (`claude`|`codex`), `model`, `cost_usd`, `input_tokens`, `cached_input_tokens`, `output_tokens`, `spec_size_kb`, `plan_size_kb`, `prd_size_kb`, `diff_lines`, `session_id`.

**Acceptance criteria** (see spec for full Given/When/Then list): automated Ralph/handle_issues runs produce correctly-labeled rows with exact token counts and `diff_lines` at PR time; interactive sessions segment correctly on `<command-name>` markers, `/clear` boundaries, and multiple user-input rounds; multi-spec-dir sessions attribute per-segment; sessions touching no spec dir and no matching branch produce no rows; cost is always computed from the pricing table (never the CLI's own reported cost), with an unknown model producing an empty `cost_usd` + warning, never a crash or blocked run; `version` is read fresh at collection time; re-runs never duplicate rows and update in place when a segment's data changed; the watermark file is gitignored and safe to delete (triggers full rescan, still dedup-safe); `.sh` is committed executable, `.ps1` runs the same logic via the project venv; a missing/failed hook never loses data since the catch-all scan picks up the transcript later.

**Out of scope**: backfilling old specs; a separate aggregate `stats.csv`; >1 branch per spec for `diff_lines`; automatic pricing-table updates; using CLI-reported cost figures; sub-segment attribution splitting; discounting `continued`-stage cache-read tokens.

## Constitution Check

Checked against `Docs/Constitution.md`. **No conflicts.**

- *Rendering (URP) / Game Logic (ECS in `src/`) / Dependency Injection (VContainer) / UI (UI Toolkit)* — not applicable: this feature is pure Python tooling under `scripts/stats/` plus a hook-config change in `.claude/settings.json`. It touches no `Assets/`, no `src/` game logic, no rendering, no DI container, no UI. Noted explicitly rather than silently skipped, per instructions.
- *Planning Discipline — plan before implement.* This plan is the gate; no code changes precede it.
- *Specification Discipline — spec before plan for feature work.* `spec.md` already exists and precedes this plan.
- *File Organisation.* This plan lives at `Docs/Specs/26_07_22_17_spec-dev-stats/plan.md`, alongside its spec, per convention.
- *Assembly Structure / C# Code Style* — not applicable, no C#/Unity code in this feature.

## Goal

Add `scripts/stats/` (pure Python stdlib): a segmentation + attribution + cost-computation + idempotent-CSV pipeline for both Claude Code transcripts and Codex rollouts, a hand-maintained pricing table, `.sh`/`.ps1` wrappers, a `SessionEnd` hook wired into `.claude/settings.json`, direct-call integration points in the four existing automation wrapper scripts, and a `scripts/stats/tests/` unittest suite. No Unity/C# changes.

## Approach

### 1. Module layout

```
scripts/stats/
  collect_usage.py       # CLI entry point: full scan, single-transcript scan, direct record
  pricing.py             # load pricing_table.json, compute cost_usd, warn on unknown model
  pricing_table.json     # hand-maintained: {"<model>": {"uncached_input_per_mtok":, "cached_input_per_mtok":, "output_per_mtok":}}
  claude_transcript.py   # parse a Claude Code session .jsonl into stage/sub-stage segments
  codex_rollout.py       # parse a Codex rollout .jsonl into stage/sub-stage segments
  segmentation.py        # shared segment/sub-stage dataclasses + user-input-round splitting
  attribution.py         # spec-dir attribution: file-write scan, branch-name fallback
  csv_store.py           # load/dedup-merge/write a spec's usage.csv, keyed by (session_id, stage)
  watermark.py           # gitignored local state: last-scanned timestamp per provider
  version_info.py        # read bundleVersion from ProjectSettings/ProjectSettings.asset
  hook_session_end.py    # SessionEnd hook shim: reads stdin JSON, calls collect_usage on transcript_path
  collect_usage.sh
  collect_usage.ps1
  tests/
    test_pricing.py
    test_claude_transcript.py
    test_codex_rollout.py
    test_attribution.py
    test_csv_store.py
    test_watermark.py
```

**Stdlib-only constraint.** Every module under `scripts/stats/` (excluding `tests/`, which may use whatever `scripts/automation/common/test_ralph.py` already uses — plain `unittest`) uses only the Python standard library (`json`, `csv`, `re`, `argparse`, `datetime`, `pathlib`, `subprocess`, `os`). This is deliberate: the `SessionEnd` hook must be invokable as a bare `python`/`python3` call without requiring the project `.venv` to be active, since Claude Code hooks run in whatever shell/environment the editor process has, not a shell the user set up by hand. The `.ps1`/`.sh` wrappers (used by the manual/cron catch-all and the automation wrappers, where a controlled environment is guaranteed) may still shell out via the project venv per `.claude/rules/temp_scripts.md`'s convention, but `collect_usage.py` itself must run standalone.

### 2. Pricing table (`pricing_table.json` + `pricing.py`)

```json
{
  "claude-sonnet-5": { "uncached_input_per_mtok": 3.0, "cached_input_per_mtok": 0.3, "output_per_mtok": 15.0 },
  "claude-opus-5":   { "uncached_input_per_mtok": 15.0, "cached_input_per_mtok": 1.5, "output_per_mtok": 75.0 },
  "gpt-5.4":         { "uncached_input_per_mtok": 0, "cached_input_per_mtok": 0, "output_per_mtok": 0 }
}
```

Placeholder rates above — real published per-Mtok rates for every model actually seen in this repo's transcripts (`claude-sonnet-5` confirmed in local transcripts; Codex models seen locally include `codex-auto-review`, `gpt-5.4-mini`, etc.) must be filled in from the current public pricing during implementation. Use the `claude-api` skill's pricing reference for Claude models; look up Codex/OpenAI model pricing from the current public rate card for the exact model strings seen in `~/.codex/sessions/*.jsonl` `token_count`/`session_meta` events.

`pricing.compute_cost(model, input_tokens, cached_input_tokens, output_tokens) -> (cost_usd: float | None, warning: str | None)`:
- `cost_usd = input_tokens/1e6 * uncached_rate + cached_input_tokens/1e6 * cached_rate + output_tokens/1e6 * output_rate`.
- Model absent from table → return `(None, f"unknown model '{model}', cost_usd left empty")`. Caller logs the warning (stderr) and still writes the row — never raises, never blocks the run (automation-host safety requirement from the spec).

### 3. Claude Code transcript segmentation (`claude_transcript.py`)

One `.jsonl` file = one session. Each line is a JSON object; the outer, per-line `"sessionId"` field (matches the filename, stable for the whole file) is the authoritative `session_id` — **not** the separate, differently-valued `"session_id"` field nested at the same top level on assistant-message lines (confirmed present as a distinct sibling key in real local transcripts; its exact semantics — likely a resumed/forked prior session's id — need a short confirmation pass against more samples during implementation, but it is not used for the dedup key regardless of what it turns out to mean).

Walk lines in order:
- A `type:"user"` line whose `message.content` contains `<command-name>/specify</command-name>` (or `/plan`, `/implement`) starts a new top-level stage (`spec`/`plan`/`implement` respectively). Other slash commands (e.g. `/commit`, `/clear`) do not start a named stage; a bare `/clear` line is otherwise ignored by the segmenter (Claude Code's own session/file boundaries already separate genuinely fresh contexts — confirm against real transcripts whether `/clear` ever appears as a mid-file marker without a following file/session-id change, and treat it as a no-op marker if so).
- `context` for a stage = `"fresh"` if no earlier stage segment exists earlier in this same file, else `"continued"`.
- Within a stage, the first `type:"assistant"` line with `stop_reason` other than `"tool_use"` (i.e. a genuine end-of-turn response, not a tool-call step) marks "first completed response". Every subsequent `type:"user"` line that is a real human turn (not a tool_result, not another `<command-name>` marker, not a meta/system line) starts a new `<stage>_user_input_N` sub-stage (N incrementing per stage).
- Per stage/sub-stage segment: sum `message.usage.input_tokens`, `cache_read_input_tokens` (→ `cached_input_tokens`), `output_tokens` across every assistant line in the segment; `model` from the first assistant line's `message.model`; `start`/`end` from the first/last line's `timestamp` (already UTC ISO 8601 in the transcript); `gitBranch` per line (used by attribution's branch fallback).

### 4. Codex rollout segmentation (`codex_rollout.py`)

One rollout `.jsonl` file = one session (`session_meta.payload.id`, filename suffix). `session_meta.payload.cwd`/`git.branch` give the working directory and branch at session start.

- **Filter first:** only process rollouts whose `session_meta.payload.cwd` matches this repo (either the main checkout or the dedicated automation clone noted in `.claude/rules/github_issue_automation.md`) and whose `thread_source` is `"user"` — **not** `"subagent"`. Confirmed on a real local sample: Codex spawns short-lived `"subagent"`-sourced rollouts for internal risk-classification ("guardian") judge calls with their own `session_meta` and no relation to a spec/plan/implement stage. These already produce zero rows under the attribution rule (no `Docs/Specs/` writes, no matching branch), but filtering them out at the `thread_source` check is cheaper and clearer than relying on that fallback.
- Codex has no `<command-name>` structural marker — `event_msg`/`type:"user_message"` lines carry only plain prompt text (confirmed: a real automated-wrapper turn's prompt was literally `"continue implementation\n"`, not a slash command). **Investigate and finalize during implementation:** read `scripts/automation/codex/ralph.py`'s and `scripts/automation/codex/handle_issues.py`'s actual prompt-construction strings for each phase (spec/plan/implement) and derive a small literal/regex match table from them, since those wrapper-authored prompts are the only structured signal available for Codex-side interactive-session staging. Document the match table as a constant at the top of `codex_rollout.py` so it's a one-place update if a wrapper's prompt wording changes.
- Sub-stage splitting (`_user_input_N`) uses the same "first completed response, then subsequent human `user_message` events" rule as Claude.
- Per segment: sum `token_count` event `info.total_token_usage.{input_tokens,cached_input_tokens,output_tokens}` deltas between segment boundaries (each `token_count` event reports a *cumulative* total per the sample seen — take the delta between the last `token_count` event before the segment and the last one at/before its end, not a running sum of every event); `model` from the session's `thread_settings_applied` event (or `session_meta` if present there); `start`/`end` from event `timestamp`s.
- `context` is always `"fresh"` for Codex automated-wrapper rows, since `scripts/automation/codex/ralph.py`'s own docstring states it runs "a fresh context per iteration" — each `codex exec` call is a new rollout file. Interactive Codex Desktop sessions (like the real sample inspected) can still be `"continued"` across multiple stage markers within one rollout file, using the same fresh/continued rule as Claude.

### 5. Attribution (`attribution.py`)

`attribute_segment(segment, repo_root) -> spec_dir | None`:
1. Scan the segment's own tool-call records for file-write targets: Claude `Write`/`Edit` tool_use inputs' `file_path`; Codex `apply_patch`/file-write tool calls' target path. Match any path under `Docs/Specs/<dir>/`; if found, that `<dir>` is the attribution (first match wins — a segment touching two spec dirs still maps to exactly one, per spec's explicit non-goal of finer splitting).
2. Else, fall back to the segment's `gitBranch`/`git.branch`: if it matches `ralph/<spec_id>` and `Docs/Specs/<spec_id>/` exists, attribute there.
3. Else, no row is produced for this segment (silently — not an error condition; matches the spec's "sessions touching no spec dir produce no rows").

### 6. CSV store (`csv_store.py`)

`upsert_row(spec_dir, row: dict)`:
- Load `usage.csv` if it exists (via `csv.DictReader`) into an ordered list of row dicts plus a `{(session_id, stage): index}` map.
- If `(row["session_id"], row["stage"])` already present, overwrite that row in place (preserves file order — an update, not a re-append). Else append.
- Rewrite the whole file via `csv.DictWriter` with the fixed column order from the spec. Create the file with a header if it doesn't exist yet.
- `spec_size_kb`/`plan_size_kb` and `diff_lines` are (re)computed at write time from the row's `end` timestamp context (current `spec.md`/`plan.md` sizes on disk right now, and current `git diff --shortstat <merge-base>..<branch>` — not historical), matching the spec's "user-input rows re-snapshot at that row's end time" rule.

### 7. Watermark (`watermark.py`)

Gitignored `​.stats/watermark.json` at repo root (mirrors the existing `.ralph/` gitignore pattern — add `.stats/` to `.gitignore` alongside it): `{"claude_last_scanned": "<iso ts>", "codex_last_scanned": "<iso ts>"}`. Full scan mode only reads session files with mtime newer than the relevant provider's watermark; after a successful scan, the watermark is advanced to "now". Deleting the file causes a full rescan of every transcript — safe, since `(session_id, stage)` dedup in `csv_store` prevents duplicate rows regardless.

### 8. Version info (`version_info.py`)

`read_bundle_version(repo_root) -> str`: regex `^\s*bundleVersion:\s*(\S+)` against `ProjectSettings/ProjectSettings.asset`, read fresh on every row write (not cached), per the spec's acceptance criterion.

### 9. CLI (`collect_usage.py`)

```
python scripts/stats/collect_usage.py --scan                     # full/incremental catch-all scan (cron/manual)
python scripts/stats/collect_usage.py --hook                     # SessionEnd hook mode: reads stdin JSON {transcript_path, cwd, reason}, processes that one Claude transcript
python scripts/stats/collect_usage.py --record --provider claude --stage implement --spec-dir <dir> --mode automated \
    --session-id <id> --model <model> --start <iso> --end <iso> \
    --input-tokens N --cached-input-tokens N --output-tokens N [--diff-lines N]
python scripts/stats/collect_usage.py --record --provider codex --stage implement --spec-dir <dir> --mode automated \
    --scan-latest-rollout-since <iso> [--diff-lines N]
```

- `--scan`: for each provider, list session files newer than the watermark, run the provider's segmenter, attribute each segment, upsert every attributed row, advance the watermark.
- `--hook`: single-file fast path for the `SessionEnd` hook — segments and attributes just the one transcript named in stdin JSON, `mode=interactive` always (hooks only ever fire for interactive sessions).
- `--record` (direct mode, no transcript re-parse): used by the four automation wrappers. Claude form takes every field directly from the wrapper's already-parsed `claude -p --output-format json` result — no file I/O beyond the CSV write. Codex form takes `--scan-latest-rollout-since <iso>` because `codex exec --json`'s own stdout stream has no single clean summary object analogous to Claude's `result` JSON (confirmed against `scripts/automation/codex/ralph.py`, which currently writes blank usage fields to its own metrics CSV for exactly this reason) — the collector instead locates the newest rollout file under `~/.codex/sessions/` with `session_meta.timestamp >= <iso>` (the wrapper passes the timestamp from just before it invoked `codex exec`) and cwd matching this repo, parses it with the normal Codex segmenter, and overrides its auto-detected `stage`/`mode` with the wrapper-supplied values (the wrapper already knows unambiguously which phase this was — no need to trust auto-segmentation for automated calls).
- Every mode: on an unknown model, print the pricing warning to stderr and continue (row still written, `cost_usd` empty) — never a non-zero exit for this reason alone.

### 10. `.sh`/`.ps1` wrappers

`collect_usage.sh`: `#!/usr/bin/env bash` + `exec python3 "$(dirname "$0")/collect_usage.py" "$@"`. Committed executable via `git update-index --chmod=+x scripts/stats/collect_usage.sh` (per spec's cross-platform acceptance criterion).

`collect_usage.ps1`: resolves the project venv Python (`.venv\Scripts\python.exe` relative to repo root, falling back to bare `python` if the venv doesn't exist — the collector itself needs no third-party packages, so either interpreter works) and forwards all arguments.

### 11. `SessionEnd` hook wiring (`.claude/settings.json`)

```json
"hooks": {
  "SessionEnd": [
    {
      "hooks": [
        { "type": "command", "command": "python \"${CLAUDE_PROJECT_DIR}/scripts/stats/collect_usage.py\" --hook" }
      ]
    }
  ]
}
```

`hook_session_end.py` is folded directly into `collect_usage.py --hook` (§9) rather than a separate script, to avoid a second entry point with its own stdin-parsing duplication — reads the hook's stdin JSON (`transcript_path`, `cwd`, `reason`), guards against a missing/malformed `transcript_path` (logs to stderr and exits 0 — a hook failure must never surface as an error to the user, and the catch-all scan will pick the session up later regardless per the spec's acceptance criterion). **Verify during implementation** which shell actually executes the `command` string on the Windows dev machine (Claude Code's hook runner, not the user's default shell) — confirm plain `python ...` resolves correctly there before relying on it, and adjust the command string if a fully-qualified interpreter path turns out to be required.

### 12. Wrapper integration — `scripts/automation/claude/ralph.py`

In `invoke_claude_step`, immediately after the existing `usage = result.get("usage", {})` line and CSV-metrics write (this feature's row is separate from and in addition to `.ralph/metrics_<spec>.csv`), call:

```python
record_usage_row(
    provider="claude", stage=map_phase_to_stage(phase), spec_dir=spec_dir_name, mode="automated",
    session_id=result.get("session_id", ""), model=result.get("model", ""),
    start=iteration_start_iso, end=datetime.utcnow().isoformat() + "Z",
    input_tokens=usage.get("input_tokens", 0), cached_input_tokens=usage.get("cache_read_input_tokens", 0),
    output_tokens=usage.get("output_tokens", 0),
    diff_lines=git_diff_shortstat(spec_branch) if phase == "complete-prd" else None,
)
```

where `record_usage_row` is imported from `scripts/stats/collect_usage.py` (added to `sys.path` the same way `scripts/automation/common` is already imported) and directly calls the same `csv_store.upsert_row` + `pricing.compute_cost` + `version_info.read_bundle_version` machinery `--record` uses — no subprocess, no re-parsing. `map_phase_to_stage` maps Ralph's `phase` strings (`"create-prd"`/loop iteration phases/`"complete-prd"`) to the spec's stage vocabulary (`spec`/`plan`/`implement` — Ralph's loop phase for spec-development work is `"implement"` in all cases relevant to this repo's spec→plan→implement flow; confirm the exact phase-string-to-stage mapping against `scripts/automation/common/ralph.py`'s actual phase names during implementation, since this plan does not re-derive the full phase list). A failure inside `record_usage_row` (e.g. pricing table read error) must not abort the Ralph loop itself — wrap the call in `try/except`, log a warning, continue.

### 13. Wrapper integration — `scripts/automation/codex/ralph.py`

Same shape in `invoke_codex_step`, using the `--scan-latest-rollout-since` path (§9) since there is no clean parsed `result` object here — call `record_usage_row_codex(spec_dir_name, stage, mode="automated", since_iso=iteration_start_iso, diff_lines=...)`.

### 14. Wrapper integration — `scripts/automation/{claude,codex}/handle_issues.py` (revised design: `USAGE_STAGE` marker convention)

**Investigated during implementation, actual shape differs from what this plan assumed.** Both `handle_issues.py` scripts invoke `claude -p`/`codex exec` exactly **once per cron tick**, with a single batched prompt covering every qualifying issue (`build_prompt(candidates)` in both files) — the command being invoked (`/handle-feature-issue`, `.codex/skills/codex-feature-issue/SKILL.md`) then decides internally, per issue, whether that issue needs `/specify`, `/plan`, or a merge this round. The wrapper's own Python code only ever sees one aggregate `claude -p`/`codex exec` result for the whole batch — it has no visibility into which stage ran for which issue's spec, so it cannot supply the `stage="spec"` or `stage="plan"` label this plan originally assumed it could, and a single invocation can legitimately span multiple issues at different stages (or none) at once, which the fixed one-`spec_id`-per-row schema can't represent from the wrapper side alone.

**Resolved via a marker convention, not a wrapper-side inference.** Since only the command running *inside* the `claude -p`/`codex exec` session knows which issue reached which stage this round, `.claude/commands/handle-feature-issue.md` and `.codex/skills/codex-feature-issue/SKILL.md` were both updated to emit a line of the form `USAGE_STAGE: <spec-folder-name> spec` or `USAGE_STAGE: <spec-folder-name> plan` in their own agent output immediately after completing that phase for an issue (mirroring the `AUTOMATION_RESULT: COMPLETED|BLOCKED` convention `codex-feature-issue/SKILL.md` already used). Both wrapper scripts now capture their own subprocess output (`assistant`-type text blocks for Claude's `--output-format stream-json`; `item.completed`/`agent_message` text for `codex exec --json`, already partially captured for the existing `AUTOMATION_RESULT` scan) across the whole run, scan it with `USAGE_STAGE_RE` after the process exits, and call `record_usage_row`/`record_usage_row_codex` once per match — Claude's form using the run's own `result` event's `usage`/`session_id`/`model` fields directly (no re-parse needed, matching `ralph.py`'s pattern); Codex's form using the same `record_usage_row_codex(since_iso=run_start)` rollout-lookup path `ralph.py`'s Codex integration already uses, since `codex exec`'s own stdout still has no single clean usage-summary object.

**Known imprecision, accepted deliberately:** if one cron tick's batched prompt advances two different issues to two different specs' `spec`/`plan` stage in the same run, both rows get the *same* whole-run `usage`/cost figures (there's no way to split a single aggregate result across multiple markers) — the same "no sub-segment splitting" tradeoff already accepted elsewhere in this design (see spec's Out of Scope). In the common case (one cron tick advances at most one issue by one phase), this is exact. Recording is wrapped in `try/except` on both wrappers so a stats-recording failure never blocks the real automation run's success/failure exit code.

### 15. Codex `notify` config — finding: skipped by user request, replaced by a `commit.md`-triggered scan (§16)

**Investigated during implementation.** `~/.codex/config.toml` on this machine already has a `notify` entry:

```toml
notify = [ "C:\\Users\\KonH\\AppData\\Local\\OpenAI\\Codex\\runtimes\\cua_node\\...\\codex-computer-use.exe", "turn-ended" ]
```

Unlike Claude Code's `hooks.SessionEnd` (an array — multiple hooks can coexist), Codex's `notify` config key holds exactly **one** external-program invocation. It's already pointed at an existing `codex-computer-use` integration on this machine; overwriting it to point at `collect_usage.py` instead would silently break that other integration for this user, and — separately — the user explicitly did not want a personal, global, hard-to-track machine config change for this feature (a dispatcher-script approach was proposed and declined for that reason, not because it wouldn't have worked technically).

**Resolved without touching `~/.codex/config.toml` at all** — see §16: `.claude/commands/commit.md` (fully repo-owned, unlike `specify.md`/`plan.md` which just delegate to the external `k` plugin) now runs the catch-all `--scan` as a best-effort step before every commit, for both providers, with no global config and no OS-level scheduled task. This trades "notify fires after every single Codex turn" for "scan fires at least once per commit" — a coarser cadence, but one that needs zero maintenance outside this repo and was the user's stated preference.

### 16. Manual/cron catch-all — wired via `commit.md`, not a scheduled task

`python scripts/stats/collect_usage.py --scan` (or `collect_usage.sh --scan` / `collect_usage.ps1 -Scan`) can always be run by hand on whatever schedule the user wants. During implementation, the user asked for it to run automatically without touching global machine config (no Codex `notify` rewrite, no Windows Scheduled Task) — `.claude/commands/commit.md` (§14/§15) now runs it as a best-effort step before every commit for both providers, which is repo-tracked, needs no machine-specific setup, and fires at least once per unit of real work either provider does. The CLI itself remains directly cron-invokable for anyone who additionally wants a time-based schedule.

## Steps

### Agent Steps

- [x] **Confirm Codex prompt-construction strings** — read `scripts/automation/codex/ralph.py`'s and `scripts/automation/codex/handle_issues.py`'s actual prompt text per phase, to finalize the Codex stage-detection match table in `codex_rollout.py` (§4).
- [x] **Confirm Ralph phase-to-stage mapping** — read `scripts/automation/common/ralph.py`'s phase name constants to finalize `map_phase_to_stage` (§12). Finding: Ralph only ever runs after a spec+plan already exist (it's the `/implement` step of the flow), so `create-prd`/`loop`/`complete-prd` all map to the constant stage `"implement"` — no per-phase table needed.
- [x] **Write the pricing table and `pricing.py`** — `pricing_table.json` with real per-Mtok rates (use the `claude-api` skill for Claude models; current public OpenAI/Codex rates for the model strings seen in local `~/.codex/sessions/*.jsonl`), `compute_cost` + unknown-model warning path, with `tests/test_pricing.py` written first (test-first per implement skill convention).
- [x] **Write `version_info.py`** — `read_bundle_version`, with a test against a small fixture `ProjectSettings.asset`-shaped file.
- [x] **Write `segmentation.py`** — shared segment/sub-stage dataclasses and the "first completed response, then subsequent human turns become user-input sub-stages" splitting logic used by both providers.
- [x] **Write `claude_transcript.py`** — full segmentation per §3, using this machine's real transcripts under `~/.claude/projects/E--Users-KonH-Git-GlobalStrategy/` as manual fixtures/spot-checks (do not commit real transcript content — extract small synthetic fixture files for the test suite).
- [x] **Write `codex_rollout.py`** — full segmentation per §4, including the `thread_source != "subagent"` filter and cwd filter, using this machine's real rollouts under `~/.codex/sessions/` as manual fixtures/spot-checks.
- [x] **Write `attribution.py`** — file-write scan + branch-name fallback per §5. Fixed during implementation: a write-path match must also verify the spec dir currently exists on disk (same rule already applied to the branch fallback) — otherwise stale `Docs/Specs/<dir>/` paths referenced in old transcripts (from specs since renamed/removed) fabricate empty spec directories on every `--scan`. Caught via a real full-repo `--scan` run that created a dozen bogus directories before the fix.
- [x] **Write `csv_store.py`** — dedup/upsert/rewrite per §6.
- [x] **Write `watermark.py`** — per §7; add `.stats/` to `.gitignore`.
- [x] **Write `collect_usage.py` CLI** — `--scan`, `--hook`, `--record` (both provider forms) per §9, wiring together all the above modules.
- [x] **Write `collect_usage.sh`** — per §10; mark executable via `git update-index --chmod=+x`.
- [x] **Write `collect_usage.ps1`** — per §10.
- [x] **Wire the `SessionEnd` hook** — add the `hooks.SessionEnd` block to `.claude/settings.json` per §11.
- [x] **Integrate `scripts/automation/claude/ralph.py`** — per §12; verify existing Ralph loop behavior (metrics CSV, activity log) is unchanged aside from the new `record_usage_row` call.
- [x] **Integrate `scripts/automation/codex/ralph.py`** — per §13.
- [x] **Integrate `scripts/automation/claude/handle_issues.py`** — per §14 (revised: `USAGE_STAGE` marker convention, since the wrapper itself has no per-stage visibility — see §14).
- [x] **Integrate `scripts/automation/codex/handle_issues.py`** — per §14, same marker convention.
- [x] **Investigate and wire Codex `notify` config** — per §15; document findings (or the graceful-degradation fallback actually used) directly in this plan's §15 if the real config shape differs from what's written here. Finding: user declined a global `~/.codex/config.toml` edit (hard to maintain, would risk the existing `codex-computer-use` hook) — see §15/§16 for the `commit.md`-triggered scan used instead.
- [x] **Wire the catch-all scan into `.claude/commands/commit.md`** — per the revised §16: runs `collect_usage.py --scan` as a best-effort step before every commit, for both providers, with no global machine config or scheduled task.
- [x] **Document the manual/cron catch-all command** — confirm `--scan` works end-to-end against this repo's own real `Docs/Specs/*/usage.csv` output for at least one real spec directory with actual interactive history (e.g. this spec's own directory once it has transcripts). Confirmed against `Docs/Specs/26_07_22_08_province-info-panel/usage.csv` — real multi-row output with correct stage/sub-stage segmentation, costs, and token sums.
- [x] **Full test suite run** — `scripts/stats/tests/` per the Tests section below, plus `scripts/automation/common/test_ralph.py` (unchanged) to confirm no regression in the existing Ralph test suite.

### User Steps

None — this feature is pure Python tooling (scripts, hook config, wrapper integration) with no Unity Editor, scene, prefab, or asset work required.

## Tests

Location: `scripts/stats/tests/`, `unittest`-based (matching `scripts/automation/common/test_ralph.py`'s existing convention — plain `unittest.TestCase`, no pytest dependency). Run via the project venv: `.venv\Scripts\python.exe -m unittest discover scripts/stats/tests` (Windows) — confirm the equivalent POSIX invocation works too since this suite must be runnable on the Linux automation host.

- **`test_pricing.py`**
  - `known_model_cost_computed_from_rates` — exact `input×rate + cached×rate + output×rate` arithmetic for a synthetic pricing table entry.
  - `unknown_model_returns_none_cost_and_warning` — model absent from table → `(None, warning)` tuple, warning names the model, no exception raised.
  - `zero_token_counts_produce_zero_cost` — boundary case, not a division error.

- **`test_claude_transcript.py`** (synthetic small `.jsonl` fixtures built inline, not real transcript content)
  - `command_name_marker_starts_new_stage` — a `<command-name>/specify</command-name>` line starts a `spec` stage segment.
  - `stage_after_first_stage_in_session_is_continued` — second `<command-name>` marker in the same file → `context="continued"`; first → `"fresh"`.
  - `user_turns_after_first_completed_response_become_user_input_substages` — two human turns after the stage's first non-tool-use assistant response produce `_user_input_1`/`_user_input_2` segments.
  - `tool_result_lines_are_not_treated_as_human_turns` — a `type:"user"` line that is actually a tool_result does not start a spurious user-input sub-stage.
  - `token_sums_are_exact_across_segment` — segment totals equal the sum of each assistant line's `usage.input_tokens`/`cache_read_input_tokens`/`output_tokens` within its boundaries, excluding lines outside it.
  - `outer_session_id_used_not_nested_message_session_id` — regression guard for the two-different-session-id-fields gotcha found during planning (§3).

- **`test_codex_rollout.py`**
  - `subagent_thread_source_is_excluded` — a rollout with `thread_source="subagent"` yields zero segments.
  - `cwd_mismatch_is_excluded` — a rollout whose `cwd` doesn't match either known repo location yields zero segments.
  - `token_count_deltas_not_cumulative_totals` — two sequential `token_count` events in one segment; the segment's token sum is the delta between them, not the raw cumulative value of the last one.
  - `stage_match_table_detects_wrapper_prompts` — the literal prompt strings confirmed in the "Confirm Codex prompt-construction strings" agent step correctly map to `spec`/`plan`/`implement`.

- **`test_attribution.py`**
  - `spec_dir_write_wins_over_branch_fallback` — a segment with both a `Docs/Specs/<dir>/` write and a matching branch attributes to the write-derived dir.
  - `branch_fallback_used_when_no_spec_dir_write` — `ralph/<spec_id>` branch with a matching existing spec dir attributes correctly.
  - `no_write_and_no_matching_branch_produces_no_attribution` — returns `None`, no row.
  - `multi_dir_segment_attributes_to_first_match_only` — a segment writing under two different spec dirs attributes to exactly one (no finer split), per spec's explicit non-goal.

- **`test_csv_store.py`**
  - `new_row_appended_when_key_absent` — `(session_id, stage)` not yet in file → appended, file order preserved for prior rows.
  - `existing_row_updated_in_place_when_key_present` — same `(session_id, stage)` re-submitted with different token counts → row overwritten, not duplicated, position unchanged.
  - `header_written_for_new_file` — a spec dir with no prior `usage.csv` gets one created with the full fixed column order from the spec.
  - `column_order_matches_spec_exactly` — regression guard against accidental column reordering.

- **`test_watermark.py`**
  - `missing_watermark_file_scans_everything` — no `.stats/watermark.json` → all session files considered "new".
  - `watermark_advances_after_successful_scan` — post-scan watermark timestamp is newer than the latest processed file's mtime.
  - `deleting_watermark_does_not_duplicate_rows` — simulate a full rescan after deletion; `csv_store` dedup still prevents duplicate rows for already-recorded `(session_id, stage)` keys (integration-style test combining `watermark` + `csv_store`).

Use the implement skill to start working on the plan or request changes.
