# Spec: Claude Session-Limit Detection and Commit Salvage

## Feature Intent

As the automation owner, I want the Claude (and Codex, for shared salvage) issue-handler wrappers to reliably detect a subscription session/usage limit — including the CLI's current "You've hit your session/weekly limit" phrasing when it appears in assistant stream-json text — and to commit and push any dirty working-tree changes before the planned pause release, so that a limit pause does not burn a crash retry via reclaim and the next run's `checkout_clean` cannot destroy uncommitted implementation work.

## Acceptance Criteria

- Detect `You've hit your session/weekly limit` (with optional `resets … (UTC)`) including assistant-only text on non-zero / error-shaped runs; keep legacy `… limit reached` / `|epoch`.
- Parse wall-clock `resets 2:10pm (UTC)` / `resets 12am (UTC)` into aware-UTC `retry_at`; backoff only when neither epoch nor parseable resets is present.
- Dirty tree on limit → deterministic Python commit+push (`chore: salvage uncommitted work after session limit`, automation git identity, `git push -u origin HEAD`) before limit-file write / release.
- Clean tree → no salvage commit; planned pause still runs.
- Success/clean: automation note (best-effort after save/release) + silent in-progress release + candidate pool; no reclaim.
- Salvage fail: marker comment + `<label>-needs-attention` + direct remove in-progress (never via `release_in_progress_silently`); no backup branches.
- `checkout_clean`: if local branch exists and is ahead of origin, push first then force-reset; push fail → do not reset over local tip.
- No false positives on successful runs that merely mention limits; distant "hit … session limit" narration must not match.

## Decisions (owner-resolved 2026-07-29)

1. Codex detector phrasing unchanged; shared salvage on Codex pause path.
2. Deterministic Python salvage only — no `commit.md` / version bump; explicit `GIT_AUTHOR_*` / `GIT_COMMITTER_*`.
3. Salvage fail → comment + needs-attention + direct remove in-progress; no backup branch.
4. Parse wall-clock `resets … (UTC)`.
5. Always post automation note (best-effort after save/release).
6. Push current HEAD.
7. `checkout_clean` pushes when local is ahead, then continues; no backup refs.
