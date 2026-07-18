# GitHub Issue → Spec Automation

`.github/workflows/handle-feature-issue.yml` runs `claude-code-action` on two events — a new issue (`issues: opened`) or a new comment on one of this automation's PRs (`issue_comment: created`) — filtered to the repo owner (`KonH`) via the workflow's `if:` condition. Each event fires `/handle-feature-issue` (`.claude/commands/handle-feature-issue.md`), which turns a correctly-formatted issue into a spec PR.

## Issue convention

Issue body must contain:

```
topic: <feature name>
description: /specify <feature description>
```

Issues that don't match this shape are ignored — safe to use GitHub issues normally alongside it.

## Scope: spec stage only

The automation only takes a feature from issue → `Docs/Specs/.../spec.md` → PR with the spec and any clarifying questions as a PR comment. It never runs `/plan` or `/implement` automatically — those stay manual, user-triggered steps. This mirrors the `workflow.md` approval-checkpoint rule ("after `/specify` writes a spec — present it to the user and stop") — "present" here means a PR comment instead of a chat message, since nobody is watching an Actions run's own transcript.

## Why this is event-driven, not polling

Each of the two workflow triggers corresponds to exactly one unambiguous action — a brand-new issue always means "start a new spec," a new PR comment from the owner always means "incorporate this reply." There's no need to scan for "is this issue already handled" the way a polling design would, since `issues: opened` only ever fires once per issue and `issue_comment: created` only fires on the actual new comment.

## Why comment/commit author works here (unlike the earlier design)

An earlier version of this automation ran via a Claude Code Remote scheduled Routine instead of Actions, authenticating to the GitHub REST API with a token that turned out to belong to the human user's own account — making "Claude's automated comment" and "a real reply" indistinguishable by author, and requiring a hidden marker-comment workaround. That approach was abandoned after also hitting two more blockers: Routine-fired sessions get no MCP connector tools, and don't start inside a git checkout of the repo at all (no way to reach the custom command file).

Actions doesn't have any of these problems: `actions/checkout` gives a real checkout on every run, and the default `GITHUB_TOKEN` posts commits/comments as `github-actions[bot]` — a genuinely different identity from the human — so plain author-based checks are reliable without a marker convention.

## Required one-time setup (repo admin)

- Add `ANTHROPIC_API_KEY` as a repository secret (Settings → Secrets and variables → Actions).
- No separate GitHub App install should be required for this workflow specifically — it only needs the standard `GITHUB_TOKEN` with the `permissions:` block already declared in the workflow file (`contents: write`, `issues: write`, `pull-requests: write`).
- If the default branch has protection rules requiring PR review before merge, that's fine — this automation only ever pushes to its own `claude/issue-*` branches and opens PRs, it never pushes directly to `main`.
