# Spec: Auto-AI Provider Routing

## Feature Intent

As the repository owner, I want unlabeled provider work marked `auto-ai` to be routed to an available AI provider automatically, so that scheduled issue and PR automation continues fairly without manual provider selection.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

- An open, owner-authored issue or PR has the `auto-ai` label and no provider label.
  - The auto-AI automation runs => it assigns exactly one eligible provider label from `claude`, `codex`, and `cursor` => the existing provider automation can process that item through its normal lifecycle.
  - More than one eligible provider exists => the automation selects the provider least recently selected by auto-AI routing => selection rotates fairly instead of repeatedly favoring one provider.
  - Several eligible items are routed in the same run => the automation records each assignment before selecting the next item => the batch rotates providers in sequence.
  - A provider is selected => the persistent routing record records that provider as most recently selected => its selection order is retained for later runs.
- A provider has an active usage or session limit.
  - The auto-AI automation considers pending `auto-ai` work => it does not select that provider => limited capacity is not assigned new work.
  - Every selectable provider has an active limit => the automation leaves pending `auto-ai` work without a provider label => it can be considered again after capacity returns.
- The auto-AI automation is scheduled.
  - It has completed routing eligible `auto-ai` items => it runs each existing provider-specific issue handler => newly assigned and previously provider-labeled work continues to use the established provider workflows.
  - An item already has any provider label => it is not reassigned by auto-AI routing => a provider-specific workflow remains the sole owner of that item's execution.
- A provider automation detects a usage or session limit.
  - It records the limit state => the shared persistent routing file is updated for that provider => future routing decisions use one extensible source of provider availability.
- An issue automation makes file changes for an issue.
  - It creates or resumes the issue work branch => the branch name follows `feature/<feature_name>` regardless of whether Claude, Codex, or Cursor was selected => work is provider-neutral and consistently named.

## Tech Notes

- Auto-AI discovery, assignment, and dispatch:
  - Add `scripts/automation/handle_issues_auto` as the scheduled orchestration entry point, with the repository's supported launcher conventions as needed.
  - Reuse `scripts/automation/common/issue_handler.py` GitHub helpers and item model so discovery includes both owner-authored open issues and PRs.
  - Discover only `auto-ai` items with none of the registered provider labels (`claude`, `codex`, `cursor`); all three are eligible from the initial release, and the dispatcher preserves the `auto-ai` label when assigning exactly one provider label.
  - After routing, invoke `scripts/automation/claude/handle_issues.py`, `scripts/automation/codex/handle_issues.py`, and `scripts/automation/cursor/handle_issues.py` as the existing provider execution entry points.
- Provider availability and fair selection:
  - Replace per-provider `Logs/handle_issues_<provider>.limit.json` bookkeeping with one persistent, atomic routing-state file that has an extensible provider-keyed structure.
  - Store each provider's active-limit retry timestamp and last auto-AI selection timestamp in that file; initialize and update provider records without requiring schema changes when a provider is added.
  - Adapt `load_limit_retry_at`, `save_limit_retry_at`, and `limit_active` in `scripts/automation/common/issue_handler.py`, and their use from all three provider wrappers, to read/write the shared provider-specific record.
  - The dispatcher chooses among providers with no active retry window by oldest recorded selection; providers never selected sort before timestamped providers, and it saves each choice before processing the next item in a batch.
- Provider-neutral issue branches:
  - Update the issue-processing instructions used by Claude, Codex, and Cursor (`.claude/commands/handle-issue.md`, `.codex/skills/codex-issue/SKILL.md`, and `.cursor/commands/cursor-issue.md`) so issue work uses `feature/<feature_name>` and resumes the matching remote feature branch.
  - Keep PR candidates on their existing PR head branch, as established by `candidate_branch` and `checkout_clean`.

## Out of Scope

- Changing the provider-specific execution lifecycle, status labels, crash reclaim behavior, model selection, or prompt content beyond the shared branch convention.
- Automatically creating GitHub labels or changing scheduler/Task Scheduler installation.
- Replacing the three provider handlers with a single provider-agnostic CLI runner.
- Merging pull requests or deleting branches.
