# GitHub Issue → Spec Automation

`scripts/handle_feature_issues.{py,sh,ps1}` runs on a cron schedule **in the user's own environment** (a personal machine or server the user controls — not this Claude Code Remote session, not a GitHub Actions runner). Each invocation pulls `main`, then runs `claude -p "/handle-feature-issue"` once (`.claude/commands/handle-feature-issue.md`), which polls for owner-authored issues matching a fixed convention and turns them into a spec PR.

## Issue convention

Issue body must contain:

```
topic: <feature name>
description: /specify <feature description>
```

Issues that don't match this shape are ignored — safe to use GitHub issues normally alongside it.

## Scope: spec stage only

The automation only takes a feature from issue → `Docs/Specs/.../spec.md` → PR with the spec and any clarifying questions as a PR comment. It never runs `/plan` or `/implement` automatically — those stay manual, user-triggered steps. This mirrors the `workflow.md` approval-checkpoint rule ("after `/specify` writes a spec — present it to the user and stop") — "present" here means a PR comment instead of a chat message, since nobody is watching this process's stdout.

## Why "the user's own environment" and not Actions or a Routine

Two earlier designs were tried and abandoned:

1. **Claude Code Remote scheduled Routine** — fired `/handle-feature-issue` into a fresh session hourly. Abandoned after hitting three compounding blockers: fired sessions get no MCP connector tools, the environment's injected `GITHUB_TOKEN` is blocked at the proxy layer for every repo-scoped REST endpoint (`GET /repos/{owner}/{repo}/...`) even after installing the Claude GitHub App, and fired sessions don't even start inside a git checkout of the repo.
2. **GitHub Actions** (`claude-code-action`) — solves all three of the above (real checkout, real `gh` CLI, real API access), and was actually working. Abandoned anyway because it only supports `anthropic_api_key` authentication — pay-per-token API billing, entirely separate from a claude.ai Pro/Max subscription. No OAuth/subscription option exists for the action.

Running `claude -p` directly in an environment where the user is logged into their own subscription (`claude login`, no API key) avoids that billing split entirely — usage draws from the same subscription pool as any other interactive Claude Code session, at the cost of owning the polling infrastructure (cron, the machine staying on, `gh` already authenticated there) instead of getting it for free from a hosted trigger.

## Comment marker convention (author-based detection doesn't work here)

Unlike the Actions design (where `github-actions[bot]` is a distinct identity from the human), this script authenticates as the user's own personal `gh`/git credentials — a comment or commit the automation makes is indistinguishable from the human's own activity *by author alone*. Every comment the automation posts starts with a fixed marker line, `<!-- claude-automation -->` (invisible HTML comment). "Is there a new human reply?" is answered by checking whether the most recent PR comment's body has that marker, not by checking who posted it.

## State tracking without labels

No GitHub labels are used for state:

- **New vs. already handled**: an issue is "new" if no PR exists with head branch `claude/issue-<issue-number>-*`.
- **Waiting vs. answered**: on an existing open PR, check the *last* comment's body for the marker above.

## Setup checklist

- `gh auth login` on the machine that will run this, authenticated as `KonH`.
- `claude` logged into a Pro/Max subscription on that same machine (no `ANTHROPIC_API_KEY` env var set, or the CLI will bill the API instead of the subscription).
- A **dedicated clone** of this repo for the automation to run against — `scripts/handle_feature_issues.py` does `git reset --hard origin/main` on every run, which would blow away uncommitted work in a normal dev checkout.
- A cron entry (Linux/macOS/WSL) or Scheduled Task (Windows) calling `scripts/handle_feature_issues.sh` / `.ps1` from that dedicated clone's root, on whatever interval the user wants (this is real polling, not a webhook — interval directly trades off cost of polling vs. latency until a new issue/reply is noticed).
