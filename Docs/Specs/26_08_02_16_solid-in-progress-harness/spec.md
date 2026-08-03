# Spec: Solid `ai-in-progress` Harness Ownership

## Feature Intent

As the repository owner running multiple automation instances, I want the Python harness itself to set `ai-in-progress` immediately before work starts and clear it when the agent CLI returns (success or failure), with no automatic stale-label reclaim, so that a peer instance cannot strip a still-running run's label and duplicate work, while true process crashes are left for manual cleanup.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

- An automation instance has won the claim on an open, provider-labeled candidate and is about to start git/CLI work on it.
  - Work begins => `ai-in-progress` is already present on that item before any branch checkout or agent CLI spawn for that candidate.
- An instance has claimed a candidate and the agent CLI for that candidate has returned (success or non-limit failure).
  - The harness finishes handling that candidate's CLI result => `ai-in-progress` is no longer on that item (regardless of whether the agent itself applied an outcome label).
  - A second instance later discovers candidates => that item is not treated as live in-progress solely because of a leftover label from the finished run => it is either parked by an outcome label the agent applied, or eligible again as a plain candidate if no outcome label remains.
- An instance has claimed a candidate and the agent CLI is cut short by a session/usage-limit pause (existing planned-pause path).
  - The limit path runs => `ai-in-progress` is cleared the same way today's limit handling already clears it (silent release or escalate-to-need-attention), without any stale-reclaim comment or crash-retry counter.
- Two automation instances (separate machines or schedules, ~minutes apart) are both able to see the same provider's items.
  - Instance A still has a live CLI run holding `ai-in-progress` on an item => instance B starts a scheduled pass => instance B does **not** remove that item's `ai-in-progress` at startup and does **not** claim or process that item while the label remains => no peer-steal / duplicate work of the kind verified on issues #111/#112.
- A claim attempt never successfully owns the item (e.g. `add_label` / claim protocol fails before this instance wins).
  - The instance skips the candidate without running the CLI => it is acceptable if `ai-in-progress` was never applied or was rolled back by the claim path; a rare miss of cleanup in this claim-only failure window is acceptable.
- The wrapper process is hard-killed (SIGKILL, power loss, host crash) after a successful claim and before harness post-CLI cleanup.
  - No automatic reclaim runs on later ticks => `ai-in-progress` may remain indefinitely => discovery keeps skipping the item until the owner manually removes `ai-in-progress` (and any needed follow-up labels) on GitHub.
- Cross-instance discovery race on a still-unclaimed candidate (the failure mode fixed by PR #109).
  - Two instances race to claim the same unlabeled candidate => existing `claim_candidate` behavior is unchanged: exactly one winner proceeds to git/CLI work; the loser skips without disturbing the winner's claim or label.

## Tech Notes

Maps each product-facing behaviour above to its concrete implementation — specific files, classes, methods, commands, state paths.

- **Keep PR #109 `claim_candidate`; abandon lease-based reclaim:**
  - Keep `claim_candidate(...)` in `scripts/automation/common/issue_handler.py` and the per-candidate call sites in `scripts/automation/claude/handle_issues.py`, `scripts/automation/codex/handle_issues.py`, and `scripts/automation/cursor/handle_issues.py` (claim before `checkout_clean` / CLI). This remains the cross-instance discovery-race fix.
  - Do **not** implement the lease-aware reclaim design from the rejected PR #117 draft / `Docs/Specs/26_08_02_14_prevent-cross-instance-reclaim/` — that approach is superseded/abandoned in favor of removing reclaim entirely.
- **Remove stale reclaim (peer-steal cause):**
  - Delete (or stop exporting and stop calling) `reclaim_stale_in_progress(...)` in `scripts/automation/common/issue_handler.py`.
  - Remove the startup calls in all three provider wrappers (`reclaim_stale_in_progress(logger, LABEL, MARKER)` near the top of `main` in claude/codex/cursor `handle_issues.py`).
  - Remove supporting reclaim-only machinery that exists solely for that path: `MAX_AUTO_RECLAIMS`, `count_reclaims_since_owner_comment(...)`, and reclaim-marker comment protocol (`<!-- …:reclaim -->`).
  - Update / remove reclaim-focused unit tests in `scripts/automation/common/test_issue_handler.py` (and any wrapper tests that assert startup reclaim). Keep tests for `claim_candidate`, `release_in_progress_silently`, and `handle_limit_pause`.
- **Harness owns post-CLI `ai-in-progress` removal:**
  - After each claimed candidate's agent CLI returns — success **or** non-limit failure — the Python wrapper must clear `ai-in-progress` for that item (provider-scoped the same way existing release helpers are), instead of relying on the agent CLI lifecycle step to be the sole removers.
  - Prefer a small shared helper in `issue_handler.py` (alongside `release_in_progress_silently`) called from each wrapper's candidate loop after CLI return, including paths that today only `sys.exit` / `raise SystemExit` on non-zero return codes (notably Cursor).
  - Session/usage-limit paths keep using `handle_limit_pause` / `release_in_progress_silently` (Claude/Codex) or Cursor's existing silent release; those already clear the label and must not go through reclaim.
  - Hard-kill before that cleanup leaves a stuck label by design; no auto-reclaim compensates.
- **Docs / skills / agent lifecycle text:**
  - Rewrite module/header docs that currently teach the false “local flock ⇒ leftover `ai-in-progress` is crash debris” assumption: `issue_handler.py` module docstring; claude/codex/cursor `handle_issues.py` headers; `.claude/skills/github-issue-automation/SKILL.md`; and any parallel Codex/Cursor automation docs that describe stale-run reclaim.
  - Document recovery for stuck labels after true crashes: owner manually removes `ai-in-progress` on GitHub (accepted; no automatic reclaim).
  - Agent command/skill steps that still say “add `ai-complete`/`ai-need-attention`, then remove `ai-in-progress`” need a deliberate update once the Ambiguities decision on harness-only vs belt-and-suspenders is resolved (see Ambiguities).
- **Unchanged by this feature:**
  - Local `acquire_lock` / `flock` behavior (same-machine single-process serialization only).
  - `find_candidates` rule: provider label present and none of `ai-in-progress` / `ai-need-attention` / `ai-complete`.
  - Claim race protocol internals (settle delay, claim comments, freshness window) beyond deleting reclaim references in comments/docs.
  - Agent responsibility to apply terminal outcome labels (`ai-complete` / `ai-need-attention`) unless a separate decision changes that.

## Out of Scope

- Cleaning up historical duplicate PRs/branches from past double-runs (e.g. fallout from #104 / #111 / #112).
- Changing the `claim_candidate` cross-instance race protocol (tokened claim comments, settle delay, comment-id winner).
- Reintroducing or redesigning automatic stale-label reclaim, lease heartbeats, or any distributed lock service.
- Unity / game-runtime work.
- External locking infrastructure (databases, Redis, queues, hosted coordinators).
- Changing session/usage-limit detection, salvage, or auto-ai reroute beyond removing reclaim coupling in docs/comments.
- Implementing the feature (this document is `/specify` only).

## Ambiguities

- **Resolved (owner 2026-08-03):** Agents must not add or remove `ai-in-progress` at all — only the Python wrappers own that label (set via `claim_candidate` before work; cleared after CLI return / limit paths). Lifecycle docs (`.claude/commands/handle-issue.md`, `.codex/skills/codex-issue/SKILL.md`, `.cursor/commands/cursor-issue.md`, github-issue-automation skill, and parallel Codex/Cursor automation docs) must drop agent-side add/remove instructions, including the “manual invocation: add it yourself” fallback. Agents still apply `ai-complete` / `ai-need-attention` only.
