# Spec: Spec Development Statistics Collection

## Feature Intent

As the repo owner, I want per-spec development statistics (LLM token usage, duration, cost, output sizes) collected automatically from Claude Code and Codex CLI session logs into a `usage.csv` file inside each spec directory, so that I can compare the real cost and effort of developing each feature across providers, models, and workflow modes without spending any LLM usage on the collection itself.

## Design Summary

The foundation is an idempotent post-hoc scanner, `scripts/stats/collect_usage.py` (pure Python, no LLM calls), with thin `scripts/stats/*.sh` / `*.ps1` wrappers. It:

- Scans Claude Code transcripts (`~/.claude/projects/<project-slug>/<session-id>.jsonl`) and Codex rollouts (`~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl`) newer than a stored watermark (a gitignored local state file).
- Segments each session into stages by the slash-command-invoking user turns (Claude transcripts carry `<command-name>` markers; Codex rollouts carry the raw user messages). Per-stage token sums are exact because each assistant message carries its own per-API-call usage block (input / cached-input / output tokens) plus model.
- Any human turn after a command's first completed response starts a `<stage>_user_input_N` sub-stage.
- Attributes interactive sessions to a spec by file writes under `Docs/Specs/<dir>/` within the segment (each stage segment attributed to the spec dir it wrote to — low-effort split, no heroics), falling back to the branch name convention (`ralph/<spec_id>`). Sessions touching no spec dir produce no rows.
- Dedups by `(session_id, stage)` so re-runs never duplicate rows; appends/updates rows in the spec's `usage.csv`.

Invocation points (freshness optimizations only — the scanner alone is sufficient for correctness):

1. Claude Code `SessionEnd` hook (fires on exit, `/clear`, logout; receives `transcript_path`, `cwd`, and reason via stdin JSON) → runs the collector on that transcript.
2. The existing automation wrappers — `scripts/automation/claude/ralph.py`, `scripts/automation/codex/ralph.py`, `scripts/automation/{claude,codex}/handle_issues.py` — already invoke `claude -p --output-format json/stream-json` and `codex exec --json`; they call the collector (or record rows directly) with the stage labels they already know, marking rows `mode=automated`.
3. Codex `notify` config (agent-turn-complete) where available.
4. Manual / cron catch-all run of the scanner.

### Output format

One file per spec: `Docs/Specs/<spec_dir>/usage.csv`, multi-row. Columns:

| Column | Meaning |
|---|---|
| `spec_id` | The spec directory name |
| `version` | Project version (`bundleVersion` from `ProjectSettings/ProjectSettings.asset`) read at collection time |
| `stage` | `spec` \| `plan` \| `implement` \| `spec_user_input_N` \| `plan_user_input_N` \| `implement_user_input_N` |
| `mode` | `automated` \| `interactive` (per row; "semi-automated" for a whole spec is derived later as "mixed rows", never stored) |
| `context` | `fresh` \| `continued` — whether the stage ran in a fresh session/context or continued an existing one (e.g. `/plan` right after `/specify` without `/clear`); stages stay separate rows either way, this flag preserves comparability since continued stages carry inflated cache-read input |
| `start`, `end` | UTC ISO 8601 timestamps (e.g. `2026-07-22T14:03:11Z`) |
| `provider` | `claude` \| `codex` |
| `model` | Model identifier from the session log |
| `cost_usd` | Computed from a checked-in, hand-maintained pricing table (`model → $/Mtok`, separate rates for uncached input, cached input, output) for BOTH providers in ALL modes; CLI-reported cost figures are ignored |
| `input_tokens`, `cached_input_tokens`, `output_tokens` | Split because cached reads cost ~10x less; always recorded as-is (what the API actually processed) — `continued` stages carry inflated cache-read input and are never discounted, `context` is the comparability flag |
| `spec_size_kb`, `plan_size_kb` | Snapshot of `spec.md` / `plan.md` sizes at the row's end time; empty cell when the file does not exist yet (distinguishable from a zero-byte file) |
| `prd_size_kb` | Snapshot of `.ralph/prd.md` size (repo root, not per-spec) at the row's end time; empty cell when the file does not exist yet |
| `diff_lines` | `git diff --shortstat` changed-line count vs merge-base with `main` on the spec's branch; captured at complete-prd/PR-creation and re-computed on user-input rounds; exactly one branch per spec is supported |
| `session_id` | Provider session id; `(session_id, stage)` is the idempotency/dedup key |

## Acceptance Criteria

### Automated runs

- **Given** a Ralph loop run via `scripts/automation/claude/ralph.py` (or the Codex equivalent) completes an `implement` stage for a spec **When** the wrapper's collection step runs **Then** `Docs/Specs/<spec_dir>/usage.csv` contains an `implement` row with `mode=automated`, the correct `provider`, `model`, `session_id`, `start`/`end` UTC ISO 8601 timestamps, and exact token counts (`input_tokens`, `cached_input_tokens`, `output_tokens`) summed from the session's per-API-call usage blocks.
- **Given** a `handle_issues.py` automation run drives `/specify` and later `/plan` for an issue's spec **When** collection runs after each stage **Then** the spec's `usage.csv` gains a `spec` row and a `plan` row, each `mode=automated`, with the stage labels supplied by the wrapper (not inferred).
- **Given** an implement stage ends at complete-prd/PR-creation **When** the row is recorded **Then** `diff_lines` equals the `git diff --shortstat` changed-line count of the spec's branch vs its merge-base with `main`.

### Interactive sessions

- **Given** an interactive Claude Code session where `/specify` runs and then `/plan` runs in the same session without `/clear` **When** the scanner processes the transcript **Then** it produces two rows — `stage=spec` with `context=fresh` and `stage=plan` with `context=continued` — both `mode=interactive`, segmented at the `<command-name>` user turns.
- **Given** an interactive session where the user runs `/specify`, then `/clear`, then `/plan` in a new session/context **When** the scanner processes both transcripts **Then** both resulting rows have `context=fresh` and carry their own distinct `session_id`s.
- **Given** a stage's command has produced its first completed response and the user then sends two further human turns refining the output **When** the scanner segments the session **Then** it emits `<stage>_user_input_1` and `<stage>_user_input_2` rows in addition to the base stage row, and each user-input row re-computes `diff_lines` and re-snapshots `spec_size_kb`/`plan_size_kb` at that row's end time.
- **Given** an interactive session that writes files under two different `Docs/Specs/<dir>/` directories in different stage segments **When** the scanner attributes rows **Then** each stage segment's row lands in the `usage.csv` of the spec dir that segment wrote to (per-segment attribution, no finer splitting).
- **Given** an interactive session that writes to no `Docs/Specs/<dir>/` path and whose branch does not match the `ralph/<spec_id>` convention **When** the scanner processes it **Then** no rows are produced anywhere.
- **Given** a session with no spec-dir writes but running on branch `ralph/<spec_id>` where `<spec_id>` matches an existing spec directory **When** the scanner processes it **Then** rows are attributed to that spec via the branch-name fallback.

### Cost computation

- **Given** the checked-in pricing table contains rates (uncached input, cached input, output, in $/Mtok) for a row's `model` **When** `cost_usd` is computed **Then** it equals `input_tokens × uncached_rate + cached_input_tokens × cached_rate + output_tokens × output_rate` (scaled per Mtok), for both `claude` and `codex` rows in both `automated` and `interactive` modes, and any cost value reported by the CLI itself is ignored.
- **Given** a session uses a model absent from the pricing table **When** collection runs **Then** the row is still written with its token counts and an empty `cost_usd`, and an explicit warning is emitted naming the missing model — never a silent zero or wrong cost, and never a failed run (a new model must not block collection on the automation host).

### Version column

- **Given** any row is written or updated **When** the collector records it **Then** `version` holds the `bundleVersion` value read from `ProjectSettings/ProjectSettings.asset` in the working tree at collection time.

### Idempotency and incremental scanning

- **Given** `usage.csv` already contains a row for a given `(session_id, stage)` **When** the scanner re-processes the same transcript (hook re-fire, manual re-run, overlapping cron) **Then** no duplicate row is added; if the segment's data changed (e.g. new user-input turns appended to a still-open session), the existing row is updated in place.
- **Given** the scanner ran previously and stored its watermark in the gitignored local state file **When** it runs again **Then** only session files newer than the watermark are read; deleting the state file causes a full rescan that still produces no duplicates thanks to `(session_id, stage)` dedup.

### Cross-platform and packaging

- **Given** the Windows dev machine **When** `scripts/stats/collect_usage.ps1` is run from the project root **Then** the collector runs via the project venv Python, resolves `~/.claude` and `~/.codex` under the user profile, and writes/updates `usage.csv` files with the same content it would produce on Linux.
- **Given** the Linux automation host **When** `scripts/stats/collect_usage.sh` is run **Then** it is directly executable (the executable bit committed via `git update-index --chmod=+x`) and produces identical CSV output for the same inputs.
- **Given** a Claude Code `SessionEnd` hook is configured **When** a session ends (exit, `/clear`, or logout) **Then** the hook invokes the collector on the `transcript_path` received via stdin JSON, and a hook failure or missing hook never loses data — the next catch-all scanner run picks the session up from the watermark scan.

## Out of Scope

- Backfilling statistics for pre-existing specs from old transcripts.
- A separate aggregate `stats.csv` file — per-spec totals and cross-spec aggregates are derivable from `usage.csv` rows on demand by any later analysis.
- Supporting more than one branch per spec for `diff_lines` computation.
- Automatic updates of the pricing table (it is maintained by hand).
- Using CLI-reported cost values (Claude interactive transcripts lack per-message cost; Codex never reports cost).
- Any LLM involvement in collection — the pipeline is pure Python plus sh/ps1 wrappers.
- Fine-grained attribution when a single stage segment touches multiple spec dirs (each segment maps to exactly one spec dir; no sub-segment splitting).
- Discounting carried-context tokens for `continued` stages — there is no separable "prior-stage context" quantity in the API's resent history, so any discount would be a fabricated heuristic that also breaks the cost column's correspondence to reality; token counts are recorded as-is.
