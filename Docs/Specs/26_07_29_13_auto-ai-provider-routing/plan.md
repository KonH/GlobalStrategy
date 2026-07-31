# Implementation Plan: Auto-AI Provider Routing

## Spec

Route open `auto-ai` issues and PRs that lack a provider label to `claude`, `codex`, or `cursor`. Select only providers whose recorded limit window has expired, using persistent least-recently-selected ordering and saving each assignment before routing the next item. Then execute all existing provider handlers. Use one extensible provider-keyed state file for limit and selection data, allow configured contributor accounts to originate automated work, and use `feature/<feature_name>` branches for issues regardless of provider.

## Goal

Deliver the same deterministic, safe auto-routing workflow in GlobalStrategy and IterativeGame, while replacing owner-only discovery with an explicit contributor allow-list and preserving the established provider-specific execution lifecycle.

## Approach

- Introduce a small shared provider registry/state API in each project's `scripts/automation/common/issue_handler.py`. The atomic JSON state file contains provider-keyed `limit_retry_at` and `last_auto_selection_at` fields, tolerates providers added later, and removes/clears only an expired provider limit.
- Port IterativeGame's contributor configuration into GlobalStrategy, initially allowing only `KonH`, and route all issue/PR author filtering and trusted-comment checks through that configuration in both projects.
- Add an `auto` automation entry point plus PowerShell/shell launchers consistent with the existing provider runners. It discovers trusted, open `auto-ai` items with no registered provider label, assigns exactly one eligible provider using sequential LRU selection, and then invokes Claude, Codex, and Cursor handlers.
- Update each provider wrapper to pass its provider name into the shared state API. Update the three issue-handling instructions to authorize configured contributors and require provider-neutral `feature/<feature_name>` issue branches, without changing PR checkout behavior.

## Agent Steps

- [x] **Characterize shared automation behavior with tests first** — extend the common handler test suite in each project for contributor-author filtering, comment trust, provider-keyed limit persistence, expired-limit cleanup, and backward-safe corrupt/missing state handling.
- [x] **Add state and contributor configuration** — port IterativeGame's contributor configuration format to GlobalStrategy with only `KonH`; make both projects load it once through shared helpers and use it for issue/PR discovery and owner/comment trust decisions. Replace individual `handle_issues_<provider>.limit.json` files with one atomic provider-routing state file.
- [x] **Implement auto-router unit coverage** — create focused tests with mocked GitHub/process helpers covering unlabeled `auto-ai` discovery across issues and PRs, exclusion of provider/status-labeled items, exclusion of limited providers, never-selected-before-selected ordering, persisted sequential batch rotation, and parking with `ai-need-attention` + comment when all providers are limited.
- [x] **Implement dispatch entry point and launchers** — add `scripts/automation/handle_issues_auto.py` and supported `.ps1`/`.sh` wrappers in both projects. Acquire its own process lock, label each selected item once while retaining `auto-ai`, persist selection immediately, and run Claude, Codex, and Cursor handlers only after routing completes; propagate/report handler failures consistently with the existing launcher style.
- [x] **Wire provider runners to shared state** — change Claude, Codex, and Cursor limit checks/saves in both repositories to identify their provider key; retain each wrapper's limit detection/backoff behavior, logging, locking, reclaim, and silent release semantics.
- [x] **Make issue instructions provider-neutral and contributor-aware** — update the Claude, Codex, and Cursor issue automation instructions in both projects to trust only configured contributors and their comments, use `feature/<feature_name>` for issue work (including resume of the matching remote branch), and continue using the existing PR head branch.
- [x] **Run verification and update checklists** — run the Python automation test modules for both repositories, add edge-case tests discovered during implementation, and mark the corresponding IterativeGame implementation checklist items complete.

## Tests

- Shared handler tests: configured authors are included for both issues and PRs; non-contributors are excluded; only comments by configured contributors can reset/revise an item.
- Shared-state tests: three independent provider records coexist; a limit for one provider does not block another; writes are atomic; corrupt/missing data fails closed only for the invalid record without crashing discovery; expired timestamps clear that provider's limit but retain selection history.
- Auto-router tests: provider-label detection, eligible-provider LRU ordering, no-limit and all-limited cases, assignment persistence before the next candidate, labels applied exactly once, and dispatch order after routing.
- Wrapper regression tests: each provider reads/writes only its key, detects a limit as before, releases its own in-progress labels, and does not call GitHub while its own limit is active.
- Instruction/config review: all six provider instruction surfaces name the contributor configuration and `feature/<feature_name>` convention; PR behavior remains explicitly unchanged.

## Constitution Check

No conflicts found — this is Python automation and repository workflow configuration only. It does not alter Unity rendering, ECS game logic, VContainer composition, UI Toolkit, assembly boundaries, or C# style. The approved spec satisfies the required spec-before-plan discipline.

Use the implement skill to start working on the plan or request changes.
