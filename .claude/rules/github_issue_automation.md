# GitHub Issue → Spec Automation

`scripts/handle_feature_issues.{py,sh,ps1}` runs on a cron schedule **in the user's own environment** (a personal machine or server the user controls — not this Claude Code Remote session, not a GitHub Actions runner). The script itself does cheap discovery via plain `gh` calls (no LLM usage); it only invokes `claude -p "/handle-feature-issue ..."` (`.claude/commands/handle-feature-issue.md`) — and only then spends subscription usage — when that discovery actually finds something to act on.

## Discovery: label + lookback window, not a full repo scan

Every cron tick, the script:

1. Pulls `main` (so it always runs the current command file, never a stale checkout).
2. `gh issue list`/`gh pr list`, both filtered to `--label claude`, `--state open`.
3. Keeps only results whose `updatedAt` falls within the lookback window (`--since-hours`, default `1` — should match the cron interval).
4. If nothing qualifies, **exits without ever invoking `claude -p`** — zero usage spent on an empty poll.
5. If something qualifies, invokes `claude -p` once with every candidate's link, title, and body embedded directly in the prompt, so the command doesn't have to spend a turn re-discovering what the wrapper already found.

This means the `claude` label is required, not just the `topic:`/`description:` body convention below — an issue without the label is never even looked at, regardless of its body.

**One-time setup**: the label must exist in the repo before it can be applied —
```
gh label create claude --color 5319E7 --description "Feature-issue automation"
```

**Add the label manually when creating a qualifying issue.** The automation applies the label itself to the PR it opens (so future replies on that PR are discovered the same way next poll) — you don't need to label the PR yourself.

## Issue convention

Issue body must contain:

```
topic: <feature name>
description: /specify <feature description>
```

Issues that don't match this shape are ignored even if labeled `claude` — safe to use GitHub issues normally alongside it.

## Scope: spec stage only

The automation only takes a feature from issue → `Docs/Specs/.../spec.md` → PR with the spec and any clarifying questions as a PR comment. It never runs `/plan` or `/implement` automatically — those stay manual, user-triggered steps. This mirrors the `workflow.md` approval-checkpoint rule ("after `/specify` writes a spec — present it to the user and stop") — "present" here means a PR comment instead of a chat message, since nobody is watching this process's stdout.

## Why "the user's own environment" and not Actions or a Routine

Two earlier designs were tried and abandoned:

1. **Claude Code Remote scheduled Routine** — fired `/handle-feature-issue` into a fresh session hourly. Abandoned after hitting three compounding blockers: fired sessions get no MCP connector tools, the environment's injected `GITHUB_TOKEN` is blocked at the proxy layer for every repo-scoped REST endpoint (`GET /repos/{owner}/{repo}/...`) even after installing the Claude GitHub App, and fired sessions don't even start inside a git checkout of the repo.
2. **GitHub Actions** (`claude-code-action`) — solves all three of the above (real checkout, real `gh` CLI, real API access), and was actually working. Abandoned anyway because it only supports `anthropic_api_key` authentication — pay-per-token API billing, entirely separate from a claude.ai Pro/Max subscription. No OAuth/subscription option exists for the action.

Running `claude -p` directly in an environment where the user is logged into their own subscription (`claude login`, no API key) avoids that billing split entirely — usage draws from the same subscription pool as any other interactive Claude Code session, at the cost of owning the polling infrastructure (cron, the machine staying on, `gh` already authenticated there) instead of getting it for free from a hosted trigger.

## Comment marker convention (author-based detection doesn't work here)

Unlike the Actions design (where `github-actions[bot]` is a distinct identity from the human), this script authenticates as the user's own personal `gh`/git credentials — a comment or commit the automation makes is indistinguishable from the human's own activity *by author alone*. Every comment the automation posts starts with a fixed marker line, `<!-- claude-automation -->` (invisible HTML comment). "Is there a new human reply?" is answered by checking whether the most recent PR comment's body has that marker, not by checking who posted it.

## State tracking: the label is discovery-only, not a state machine

The `claude` label answers "should this be looked at at all," not "what state is it in" — classification still happens the same way it always has, inside the command itself:

- **New vs. already handled**: an issue is "new" if no PR exists with head branch `claude/issue-<issue-number>-*`.
- **Waiting vs. answered**: on an existing open PR, check the *last* comment's body for the marker above.

This matters because a PR's `updatedAt` moves every time *any* comment lands on it — including the automation's own — so a PR can legitimately show up as a "candidate" in a poll where nothing actually changed from the human's side. The marker check is what filters that out.

## Setup checklist

- `gh auth login` on the machine that will run this, authenticated as `KonH`.
- `gh label create claude --color 5319E7 --description "Feature-issue automation"` (once, on the repo) — see the discovery section above.
- Subscription-based `claude` auth on that same machine, via a **long-lived token** rather than interactive login — the cron job runs unattended, so there's nobody there to complete a browser OAuth redirect or paste a fallback code each time:
  1. On any machine with normal browser access (doesn't have to be the automation host), run `claude setup-token`. It opens the browser OAuth flow and prints a token to the terminal after approval — it does not save the token anywhere itself.
  2. On the automation host, `export CLAUDE_CODE_OAUTH_TOKEN=<that token>` (in the cron job's environment, e.g. the crontab's own env or a sourced profile — cron doesn't inherit an interactive shell's exports).
  3. Do **not** also set `ANTHROPIC_API_KEY` — its presence makes the CLI bill the API instead of the subscription.
- A **dedicated clone** of this repo for the automation to run against — `scripts/handle_feature_issues.py` does `git reset --hard origin/main` on every run, which would blow away uncommitted work in a normal dev checkout.
- A cron entry (Linux/macOS/WSL) or Scheduled Task (Windows) calling `scripts/handle_feature_issues.sh` / `.ps1` from that dedicated clone's root, on whatever interval the user wants (this is real polling, not a webhook — interval directly trades off cost of polling vs. latency until a new issue/reply is noticed).

### Interactive testing (not the cron path)

Running `claude` by hand in a remote container (Codespaces, SSH, WSL2) to sanity-check things: the OAuth browser redirect can't reach the CLI's local callback server there, so instead of redirecting, the browser shows a short code — paste it into the terminal at the `Paste code here if prompted` prompt. This is automatic CLI behavior, not something to configure. It's a one-off login for manual testing; the cron job itself should still use `CLAUDE_CODE_OAUTH_TOKEN` as above.
