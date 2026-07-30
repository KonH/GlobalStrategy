---
name: github-issue-automation
description: Reference for scripts/automation/claude/handle_issues.py (and the codex/common equivalents) — the cron-driven, label-driven "execute the prompt from a GitHub issue/PR" automation. Load when working on, debugging, or extending that automation, or its .claude/commands/handle-issue.md command.
---

# Label-Driven GitHub Issue/PR Automation

`scripts/automation/claude/handle_issues.{py,sh,ps1}` runs on a cron schedule **in the user's own environment** (a personal machine or server the user controls — not this Claude Code Remote session, not a GitHub Actions runner). The script itself does cheap discovery via plain `gh` calls (no LLM usage); it only invokes `claude -p "/handle-issue ..."` (`.claude/commands/handle-issue.md`) — and only then spends subscription usage — when discovery actually finds a candidate. Discovery/locking/reclaim logic shared with the Codex equivalent (`scripts/automation/codex/handle_issues.py`, driving `.codex/skills/codex-issue/SKILL.md`) lives in `scripts/automation/common/issue_handler.py`.

## The labels are the whole state machine

There is no local state file, no timestamp bookkeeping, no comment-heading parsing, and no reaction handling — the label set on each issue/PR is the entire state, visible and manually overridable on GitHub:

- **`claude` / `codex` / `cursor`** — provider opt-in (owner or auto-router). Description plus trusted later comments are the prompt — no required format, no hardcoded spec→plan pipeline; whatever workflow is wanted (e.g. "run /specify for X") goes in the prompt itself.
- **`ai-in-progress`** — a run is actively working the item. Skipped by discovery. Shared across providers.
- **`ai-need-attention`** — the automation stopped and is waiting on the owner (question asked, blocked, partial work, missing environment). Skipped by discovery. Shared across providers.
- **`ai-complete`** — the prompt is fully done. Skipped by discovery. Shared across providers.

**A candidate is any open, configured-contributor-authored issue or PR carrying the provider opt-in label and none of the three shared `ai-*` status labels.** That single rule is all of discovery — one `gh issue list` plus one `gh pr list`, filtered locally. Reclaim/release only touch items that also carry the calling provider's opt-in label.

**Each candidate gets its own CLI invocation from a guaranteed-clean checkout of its valid branch** — `main` for an issue, the PR's head branch for a PR. The wrapper prepares it itself before every invocation (`git fetch` + if the local branch exists and is ahead of `origin/<branch>`, `git push -u origin <branch>` first + `git checkout -f -B <branch> origin/<branch>` + `git clean -fd`, which keeps ignored files like `Logs/` intact). The ahead-push preserves unpushed salvage commits; if that push fails, the force-reset is skipped so the local tip is not discarded. The CLI run never starts from a stale or dirty tree, and candidates needing different branches can't contaminate each other. The CLI run must never do its own reset/clean at the start.

**Resume semantics:** the owner resumes a need-attention/complete item by replying in a comment and removing that status label. Removing the label is the explicit "go again" signal — the automation never guesses whether a comment was new instructions. The trade-off (forgetting to remove the label leaves the item parked, visibly) is accepted for the predictability.

## Per-item lifecycle (the command side)

For each candidate, the CLI run (see `.claude/commands/handle-issue.md` for the exact rules):

1. Adds `ai-in-progress` first.
2. Reads the item's live description + owner comments as the prompt (later comments override earlier ones; its own `<!-- claude-automation -->`-marked comments are context, never instructions).
3. Executes it — on the PR's head branch for PR candidates, on `feature/<feature_name>` for issue candidates (reused if it already exists on origin).
4. **Always commits and pushes**, even partial work — the pushed branch is the next run's starting point.
5. Ensures a PR exists for issue work (`Closes #N`). **Never merges anything** — merging is always the owner's click.
6. Posts one summary comment (marker first line) — the handoff for both the owner and the next memory-less run.
7. Applies `ai-complete` or `ai-need-attention`, **then** removes `ai-in-progress` — every item ends the run with exactly one outcome label.

## Crash recovery: stale-label reclaim with a GitHub-side counter

The wrapper holds an exclusive process lock, so at run start no other run of this provider can be active — any of that provider's items still labeled `ai-in-progress` is by definition leftover from a crashed/interrupted run (step 7's ordering guarantees a healthy run never leaves only that label). `reclaim_stale_in_progress` in `issue_handler.py`:

- Posts a `<!-- claude-automation:reclaim -->` marker comment and removes the label, so normal discovery picks the item up again — up to `MAX_AUTO_RECLAIMS` (2) times.
- On the crash after that (3rd interrupted attempt), it parks the item with `ai-need-attention` and an explanatory comment instead — repeated crashes never burn subscription usage forever.
- The reclaim comments *are* the counter — stored on GitHub, no local state. A real owner comment (non-marker, authored by a configured contributor) resets it, so a resumed item gets fresh retries.

A crash *before* the run even applies `ai-in-progress` simply leaves the item a plain candidate — it retries next tick with no counter. That window is one label call wide; accepted.

## Session/usage limits: a planned pause, never a crash

A subscription session/usage-limit hit is handled entirely separately from crashes:

- **Detection (Claude):** matches legacy `… limit reached` and production `You've hit your session/weekly limit` (and usage) phrasing. Looks at non-JSON CLI lines and the final error result always; also consults assistant stream-json text **only when** the run already ended non-zero or error-shaped (`is_error` / subtype starts with `error`) — so an issue whose prompt merely *talks about* limits on a successful run can't false-positive. Reset time preference: embedded `|epoch` → parseable `resets 2:10pm (UTC)` / `resets 12am (UTC)` wall-clock (next occurrence, aware UTC) → else now + `--limit-backoff-minutes` (default 60).
- **Detection (Codex):** `usage limit|rate limit|quota exceeded` in error/failed output only. Reset time preference: parseable `try again at Aug 5th, 2026 9:31 AM` (aware UTC; message has no timezone) → else now + `--limit-backoff-minutes` (default 60).
- **Detection (Cursor):** `usage limit|rate limit|requests limit|too many requests|resource_exhausted|quota exceeded|spend limit` in error/failed output only. Production CLI phrasing includes `You've hit your usage limit` / `free requests limit` plus `Your usage limits will reset when your monthly cycle ends on 8/14/2025`. Reset time preference: that `M/D/YYYY` as aware-UTC midnight (next day if already past) → else now + `--limit-backoff-minutes` (default 60).
- **Salvage:** before writing the limit file / releasing labels, the wrapper runs deterministic Python git (`salvage_uncommitted_work`): if the tree is dirty, `git add -A`, commit with fixed message `chore: salvage uncommitted work after session limit` under `GIT_AUTHOR_*` / `GIT_COMMITTER_*` identity `GlobalStrategy Automation <automation@local>`, then `git push -u origin HEAD`. Clean tree → no commit. No agent/`commit.md` pipeline, no version bump, no backup branches.
- **Pause orchestration (`handle_limit_pause`):** salvage → always write the shared provider-keyed state file → on clean/committed: `release_in_progress_silently` (no reclaim comment, no crash-retry), then `reroute_auto_item_after_limit`, then best-effort automation note on the item (salvage outcome + either the reroute or pause-until-`retry_at`); on salvage failure: apply `ai-need-attention`, remove `ai-in-progress` **directly** (never via silent release after need-attention), then best-effort failure comment - no reroute, since the item is now waiting on the owner, not just paused. Note posting is best-effort *after* save/release so a failed `post_comment` cannot leave `ai-in-progress` for reclaim. Exits 0.
- **Immediate reroute for auto-routed items (`reroute_auto_item_after_limit`):** if the limit-hit candidate still carries `auto-ai` (i.e. it was routed rather than opted in with a plain provider label), the release above is followed by dropping the current provider label and re-running `select_auto_provider` over the other two providers right there in the same process - no waiting for this provider's own backoff window, and no waiting for the next scheduled `handle_issues_auto.py` tick. If every other provider is limited too, `park_auto_item_unroutable` applies `ai-need-attention` and posts an `<!-- auto-ai-automation -->` note. Items opted in with a plain provider label (not auto-routed) are left on that label and simply wait out the pause, same as before.
- Every later run checks the stored timestamp right after acquiring the lock, comparing aware-UTC to aware-UTC so the machine's local timezone never skews it, and **skips the whole run** (no GitHub calls, no CLI invocation) while the window is still in effect. Once it has passed, the file is deleted and normal runs resume.
- **`checkout_clean`:** if the local branch exists and is ahead of `origin/<branch>`, push it first, then force-reset; a failed ahead-push does not reset over the local tip (so an unpushed salvage commit survives until push succeeds).

## Concurrency: a process lock, not GitHub state

`handle_issues.py` acquires an exclusive `flock` on `Logs/handle_issues_claude.lock` before doing anything else; a run that can't get the lock exits immediately. This stays a local OS-level lock rather than a GitHub label because it releases automatically the moment the process exits, crash or not — and it's precisely what makes the stale-reclaim logic above sound. (On Windows it uses `msvcrt` locking; also set Task Scheduler's "don't start a new instance" option as a second safeguard.)

## Security

Every action only ever triggers on content authored by `KonH` — the issue/PR itself (discovery filters `--author KonH`) and its comments (the command re-verifies per comment). Content from anyone else is ignored entirely, even a collaborator. Applying labels requires triage access, so the opt-in label itself is also gated. The automation authenticates with the owner's own credentials, so its own comments are distinguished from the owner's by the `<!-- claude-automation -->` marker prefix, not by author.

## Environment limits

The automation host has no Unity Editor, no Unity MCP, and no image-generation pipeline. Prompts needing those get everything that *is* possible (C# verifiable via `dotnet build`/`dotnet test`, configs, docs, scripts), an explicit list of what was skipped, and an `ai-need-attention` finish. Implementation tooling like `scripts/automation/claude/ralph.py` remains available as a standalone tool but is no longer wired into this automation.

## One-time setup

Labels must exist before they can be applied:

```
gh label create claude --color 5319E7 --description "Execute this item's prompt via the Claude automation"
gh label create codex --color 5319E7 --description "Execute this item's prompt via the Codex automation"
gh label create cursor --color 5319E7 --description "Execute this item's prompt via the Cursor automation"
gh label create auto-ai --color 1D76DB --description "Auto-route this item to an available AI provider"
gh label create ai-in-progress --color FBCA04 --description "Automation actively working this item"
gh label create ai-need-attention --color D93F0B --description "Automation waiting on the owner"
gh label create ai-complete --color 0E8A16 --description "Automation finished this item's prompt"
```

Only the plain provider / `auto-ai` labels are ever applied by the owner when opting an item in — the three shared `ai-*` status labels are managed by the automation (removal of `ai-need-attention`/`ai-complete` to resume being the one owner-side exception). When `auto-ai` routing (or a post-limit immediate reroute) finds every provider usage/session-limited, `park_auto_item_unroutable` parks the item with `ai-need-attention` and an `<!-- auto-ai-automation -->` comment rather than leaving it unlabeled for endless empty polls.

## Setup checklist

- `gh auth login` on the machine that will run this, authenticated as `KonH`.
- The label create commands above (once, on the repo).
- Subscription-based `claude` auth on that same machine, via a **long-lived token** rather than interactive login — the cron job runs unattended, so there's nobody there to complete a browser OAuth redirect or paste a fallback code each time:
  1. On any machine with normal browser access (doesn't have to be the automation host), run `claude setup-token`. It opens the browser OAuth flow and prints a token to the terminal after approval — it does not save the token anywhere itself.
  2. On the automation host, `export CLAUDE_CODE_OAUTH_TOKEN=<that token>` (in the cron job's environment, e.g. the crontab's own env or a sourced profile — cron doesn't inherit an interactive shell's exports).
  3. Do **not** also set `ANTHROPIC_API_KEY` — its presence makes the CLI bill the API instead of the subscription.
- A **dedicated clone** of this repo for the automation to run against — `handle_issues.py` force-resets to the candidate's start branch (and removes untracked files) before every CLI invocation, which would blow away uncommitted work in a normal dev checkout.
- A cron entry (Linux/macOS/WSL) or Scheduled Task (Windows) calling `scripts/automation/handle_issues_auto.sh` / `.ps1` (or a provider-specific wrapper) from that dedicated clone's root, on whatever interval the user wants (this is real polling, not a webhook — the interval is simply the latency until a new label/reply is noticed; an empty poll costs a few `gh` calls and zero LLM usage).

### Interactive testing (not the cron path)

Running `claude` by hand in a remote container (Codespaces, SSH, WSL2) to sanity-check things: the OAuth browser redirect can't reach the CLI's local callback server there, so instead of redirecting, the browser shows a short code — paste it into the terminal at the `Paste code here if prompted` prompt. This is automatic CLI behavior, not something to configure. It's a one-off login for manual testing; the cron job itself should still use `CLAUDE_CODE_OAUTH_TOKEN` as above.

## Why "the user's own environment" and not Actions or a Routine

Two earlier designs were tried and abandoned:

1. **Claude Code Remote scheduled Routine** — fired the handler command into a fresh session hourly. Abandoned after hitting three compounding blockers: fired sessions get no MCP connector tools, the environment's injected `GITHUB_TOKEN` is blocked at the proxy layer for every repo-scoped REST endpoint (`GET /repos/{owner}/{repo}/...`) even after installing the Claude GitHub App, and fired sessions don't even start inside a git checkout of the repo.
2. **GitHub Actions** (`claude-code-action`) — solves all three of the above (real checkout, real `gh` CLI, real API access), and was actually working. Abandoned anyway because it only supports `anthropic_api_key` authentication — pay-per-token API billing, entirely separate from a claude.ai Pro/Max subscription. No OAuth/subscription option exists for the action.

Running `claude -p` directly in an environment where the user is logged into their own subscription (`claude login`, no API key) avoids that billing split entirely — usage draws from the same subscription pool as any other interactive Claude Code session, at the cost of owning the polling infrastructure (cron, the machine staying on, `gh` already authenticated there) instead of getting it for free from a hosted trigger.

## History: the previous spec→plan→merge state machine

Before 2026-07, this automation was a multi-phase pipeline (spec draft → 👍 → plan draft → 👍 → auto-merge → implementation proposal → 👍 → Ralph loop → 👍 → auto-merge) driven by comment-heading parsing, reaction detection, a timeline-based activity scan, a lookback-window state file, and an edited-in-place checklist comment. It was replaced by the label-driven design above to remove the hardcoded workflow, the approval round-trips, and all hidden state — see the git history of this file and `.claude/commands/handle-feature-issue.md` (deleted) for the old design.
