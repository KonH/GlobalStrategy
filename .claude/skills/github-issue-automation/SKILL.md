---
name: github-issue-automation
description: Reference for scripts/automation/claude/handle_issues.py (and the codex/common equivalents) — the cron-driven GitHub issue → spec/plan → merge pipeline. Load when working on, debugging, or extending that automation, or its .claude/commands/handle-feature-issue.md command.
---

# GitHub Issue → Spec/Plan Automation

`scripts/automation/claude/handle_issues.{py,sh,ps1}` runs on a cron schedule **in the user's own environment** (a personal machine or server the user controls — not this Claude Code Remote session, not a GitHub Actions runner). The script itself does cheap discovery via plain `gh` calls (no LLM usage); it only invokes `claude -p "/handle-feature-issue ..."` (`.claude/commands/handle-feature-issue.md`) — and only then spends subscription usage — when that discovery actually finds something to act on. Discovery/locking/state logic shared with the Codex equivalent (`scripts/automation/codex/handle_issues.py`) lives in `scripts/automation/common/issue_handler.py`.

## Scope: spec → plan → merge, never `/implement`

The automation drives a feature issue through `/specify` → (owner review) → `/plan` → (owner review) → merge to `main`, entirely via comments/reactions on the issue. It never runs `/implement` automatically — actual implementation (e.g. via `scripts/automation/claude/ralph.py`) stays a manual step after the spec+plan lands on `main`. This mirrors the `workflow.md` approval-checkpoint rule that `/specify` and `/plan` each stop and wait for the user — "present to the user" here means an issue comment instead of a chat message, since nobody is watching this process's stdout, and "wait for the user" means waiting for a comment or 👍 reaction instead of the next chat turn.

## All conversation happens on the issue, not the PR

The PR only carries the code (spec.md/plan.md) and is linked via `Closes #N`. Every summary, question, and conclusion is posted as an issue comment. This keeps one single thread across the whole spec→plan→merge lifecycle instead of splitting between the issue and a PR that doesn't exist yet at issue-creation time.

## Progress checklist: one comment, edited in place

Every issue gets a single tracking comment (its own `<!-- claude-automation:checklist -->` marker, separate from the general one) showing checkboxes for spec/plan drafted/approved, merged, and classified, plus a one-line status. It's created as the issue's first comment and edited via `gh api -X PATCH` on every subsequent cycle rather than ever being reposted — the checkbox state is fully derived from the current phase each time, not something a human can toggle to signal approval (that stays the reaction-based flow above). Full template and derivation rules are in the command file.

## Discovery: label + lookback window + reaction check

Every cron tick, the script:

1. Pulls `main` (so it always runs the current command file, never a stale checkout).
2. `gh issue list --label claude --author KonH --state open`.
3. An issue is a candidate if either: its `updatedAt` falls within the lookback window (`--since-hours`/`--since-minutes`, combined; defaults to 1h if both are omitted), **or** any owner reaction on one of the automation's own marker comments has a `created_at` within the window.
4. If nothing qualifies, **exits without ever invoking `claude -p`** — zero usage spent on an empty poll.
5. If something qualifies, invokes `claude -p` once with every candidate's link, title, body, and a `reason` hint embedded in the prompt — the command investigates each one's actual comment/reaction history itself; the wrapper only says *which* issues need attention, not *what* changed.

**Reactions do not update `updatedAt` on GitHub's side.** A 👍 on a summary comment would be invisible to a plain "list issues updated since X" check — that's why step 3 is a separate check, not just a timestamp filter on the issue list. It costs a couple of extra read-only `gh api` calls per open candidate (fetch its marker comments, fetch reactions on each) — still free of LLM usage, and bounded by however many `claude`-labeled issues are open at once, which is small for a personal repo.

**One-time setup**: labels must exist before they can be applied —
```
gh label create claude --color 5319E7 --description "Feature-issue automation"
gh label create claude-in-progress --color FBCA04 --description "Automation actively working this issue"
gh label create claude-needs-attention --color D93F0B --description "Automation stopped, needs a human"
gh label create code-only --color 0E8A16 --description "Implementable without Unity Editor/MCP or image generation"
gh label create full-env-required --color 5319E7 --description "Needs Unity Editor/MCP or image generation to implement"
```

**Add the `claude` label manually when creating a qualifying issue** — no required body format, just write it naturally (title → feature name, body → feature description, optional leading `/specify` stripped if present). The other four labels are applied/removed by the automation itself.

## Post-merge classification: `code-only` vs `full-env-required`

Right after a successful merge, the command labels the (now auto-closed) issue with exactly one of:

- **`code-only`** — the plan is confined to `src/` (Unity-independent logic), pure C#/ECS work under `Assets/Scripts/` verifiable via `dotnet build`/tests without the Editor open, or `scripts/utils/*.py` config generators.
- **`full-env-required`** — anything touching `Assets/UI/`, `Assets/Prefabs/`, scenes, ScriptableObject/prefab assets, textures/flags/portraits, or that otherwise needs Unity MCP tool usage or the image-generation pipeline to implement or verify.

This is a forward-looking signal for whatever picks up implementation later (manually, or a future automated `/implement` step) — it can filter to `code-only` issues for anything meant to run without a Unity Editor available. Ambiguous/mixed plans default to `full-env-required`, since an implementation attempt wrongly assuming no Editor is needed fails worse than a human just double-checking one that didn't need it.

## Concurrency: a process lock, not GitHub state

`handle_feature_issues.py` acquires an exclusive `flock` on `Logs/handle_feature_issues.lock` before doing anything else. If a previous run is still executing (e.g. a slow `claude -p` call) when the next cron tick fires, the new invocation exits immediately instead of racing it. This is deliberately a local OS-level lock, not a GitHub label: a label has a check-then-act race and can get stuck "on" forever if the process holding it crashes, whereas the flock releases automatically the moment the process exits, crash or not. (POSIX only — on Windows, rely on Task Scheduler's own "don't start a new instance if one is already running" setting instead.)

The `claude-in-progress` label is applied/removed by the *command* (not the wrapper) around each issue it's actively handling within a run — that's purely for visibility (glance at GitHub, see what the bot is touching right now), not the actual mutex.

## Comment marker and heading convention

Every comment the automation posts starts with `<!-- claude-automation -->` (invisible HTML comment) so later runs can tell "the bot's own comment" apart from a real reply — necessary because this script authenticates as the owner's own personal `gh`/git credentials, so author alone can't distinguish the two. The second line is always one of a small fixed set of headings (`## Spec Summary`, `## Spec Conclusion`, `## Plan Summary`, `## Plan Conclusion`, `## Clarification Needed`, `## Needs Manual Attention`) — this is what lets a later run deterministically figure out which phase (spec review vs. plan review) an issue is in and what triggered each round, without re-interpreting free text. Full mechanics in the command file.

## Bounded clarification loop

If the same phase (spec or plan) needs a 4th `Clarification Needed` round without converging to a Conclusion, the automation stops asking, posts `## Needs Manual Attention`, and applies `claude-needs-attention`. A later comment from the owner is still picked up as a normal candidate next run and resumes things.

## Merge-conflict handling: auto-resolve only the trivial version-bump case

Every commit in this repo bumps `bundleVersion` in `ProjectSettings/ProjectSettings.asset` (see `.claude/commands/commit.md`), so a feature branch merging `main` back in will very often conflict on exactly that one line and nothing else. The automation auto-resolves *only* that specific case: if the only conflicted file is `ProjectSettings.asset` and the only conflicting hunk is the `bundleVersion:` line, it takes the greater of the two conflicting values and applies the normal commit-time increment on top (so the resolved version is higher than both sides, not equal to either) — then completes the merge and pushes. **Any other conflict, in any file, aborts the merge** and posts `## Needs Manual Attention` instead of guessing. Unattended auto-resolution of a real content conflict is exactly the kind of failure that stays invisible until it's already on `main`, so this stays a hard line.

## Why "the user's own environment" and not Actions or a Routine

Two earlier designs were tried and abandoned:

1. **Claude Code Remote scheduled Routine** — fired `/handle-feature-issue` into a fresh session hourly. Abandoned after hitting three compounding blockers: fired sessions get no MCP connector tools, the environment's injected `GITHUB_TOKEN` is blocked at the proxy layer for every repo-scoped REST endpoint (`GET /repos/{owner}/{repo}/...`) even after installing the Claude GitHub App, and fired sessions don't even start inside a git checkout of the repo.
2. **GitHub Actions** (`claude-code-action`) — solves all three of the above (real checkout, real `gh` CLI, real API access), and was actually working. Abandoned anyway because it only supports `anthropic_api_key` authentication — pay-per-token API billing, entirely separate from a claude.ai Pro/Max subscription. No OAuth/subscription option exists for the action.

Running `claude -p` directly in an environment where the user is logged into their own subscription (`claude login`, no API key) avoids that billing split entirely — usage draws from the same subscription pool as any other interactive Claude Code session, at the cost of owning the polling infrastructure (cron, the machine staying on, `gh` already authenticated there) instead of getting it for free from a hosted trigger.

## Setup checklist

- `gh auth login` on the machine that will run this, authenticated as `KonH`.
- The three `gh label create` commands above (once, on the repo).
- Subscription-based `claude` auth on that same machine, via a **long-lived token** rather than interactive login — the cron job runs unattended, so there's nobody there to complete a browser OAuth redirect or paste a fallback code each time:
  1. On any machine with normal browser access (doesn't have to be the automation host), run `claude setup-token`. It opens the browser OAuth flow and prints a token to the terminal after approval — it does not save the token anywhere itself.
  2. On the automation host, `export CLAUDE_CODE_OAUTH_TOKEN=<that token>` (in the cron job's environment, e.g. the crontab's own env or a sourced profile — cron doesn't inherit an interactive shell's exports).
  3. Do **not** also set `ANTHROPIC_API_KEY` — its presence makes the CLI bill the API instead of the subscription.
- A **dedicated clone** of this repo for the automation to run against — `scripts/automation/claude/handle_issues.py` does `git reset --hard origin/main` on every run, which would blow away uncommitted work in a normal dev checkout.
- A cron entry (Linux/macOS/WSL) or Scheduled Task (Windows) calling `scripts/automation/claude/handle_issues.sh` / `.ps1` from that dedicated clone's root, on whatever interval the user wants (this is real polling, not a webhook — interval directly trades off cost of polling vs. latency until a new issue/reply/reaction is noticed).

### Interactive testing (not the cron path)

Running `claude` by hand in a remote container (Codespaces, SSH, WSL2) to sanity-check things: the OAuth browser redirect can't reach the CLI's local callback server there, so instead of redirecting, the browser shows a short code — paste it into the terminal at the `Paste code here if prompted` prompt. This is automatic CLI behavior, not something to configure. It's a one-off login for manual testing; the cron job itself should still use `CLAUDE_CODE_OAUTH_TOKEN` as above.
