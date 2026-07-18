# GitHub Issue → Spec Automation

An hourly scheduled Routine fires `/handle-feature-issue` (`.claude/commands/handle-feature-issue.md`) into a fresh Claude Code Remote session. It looks for open GitHub issues from the repo owner that follow a fixed convention and turns them into a spec PR.

## Issue convention

Issue body must contain:

```
topic: <feature name>
description: /specify <feature description>
```

Issues that don't match this shape are ignored by the automation — safe to use GitHub issues normally alongside it.

## Scope: spec stage only

The automation only takes a feature from issue → `Docs/Specs/.../spec.md` → PR with the spec and any clarifying questions as a PR comment. It never runs `/plan` or `/implement` automatically — those stay manual, user-triggered steps, same as normal interactive use. This mirrors the `workflow.md` approval-checkpoint rule ("after `/specify` writes a spec — present it to the user and stop") — the only change is that "present" means a PR comment instead of a chat message, since nobody is watching a Routine's chat transcript.

## State tracking without labels

No GitHub labels are used for state. Instead:

- **New vs. already handled**: an issue is "new" if no open (or merged/closed) PR exists with head branch `claude/issue-<issue-number>-*`. Once such a PR exists, the issue is never reprocessed from scratch.
- **Waiting vs. answered**: on an existing PR, the automation looks at who posted the *most recent* comment. If it's the repo owner, there's a new reply to incorporate; if it's Claude's own prior comment (or nothing since), there's nothing to do this cycle.

This avoids needing a label-creation step or any separate state store — GitHub's own PR/comment history is the source of truth, so this is naturally reviewable by anyone reading the PR.

## Routine configuration

- Trigger: cron, hourly (the minimum interval Routines support — this is not a real-time webhook, expect up to ~1h latency between an issue/reply and a response).
- Mode: fresh session per firing (`create_new_session_on_fire`), same environment as this repo's session, so each run starts clean with the repo already checked out.
- Prompt: literally `/handle-feature-issue` — all actual logic lives in the versioned command file, not in the Routine config, so it's reviewable/editable via normal commits.

## Why GitHub Actions wasn't used

This project has repo-admin-independent iteration as a priority during early setup — Actions would need `ANTHROPIC_API_KEY` as a repo secret, the Claude GitHub App installed, and workflow YAML permissions tuned, all requiring repo-admin access. The Routine approach reuses the already-authenticated Claude Code Remote environment and its GitHub MCP connection instead. Trade-off: hourly polling latency instead of instant webhook delivery. Revisit Actions if latency becomes a problem.
