Handle a single GitHub event for the feature-issue-to-spec automation. Invoked by `.github/workflows/handle-feature-issue.yml` on either a new issue (`issues: opened`) or a new comment on one of this automation's PRs (`issue_comment: created`) — the workflow's `if:` condition already restricts both to the repo owner (`KonH`). The repo is already checked out on `main` by the workflow; `gh` CLI is pre-authenticated via `GH_TOKEN`.

Read `$GITHUB_EVENT_PATH` (JSON) and use `$GITHUB_EVENT_NAME` to tell the two cases apart — don't re-derive issue/PR/comment numbers by searching, they're already in the event payload.

Repo: `KonH/GlobalStrategy`. Base branch: `main`.

## Case A — new issue (`GITHUB_EVENT_NAME=issues`)

1. Read `.issue.body` from the event payload. If it doesn't contain a line starting with `topic:` and a line starting with `description:` (case-insensitive, either order), stop — this isn't a feature-automation request, leave the issue alone.
2. Parse the `topic:` value → feature name; slugify it (lowercase, spaces/punctuation → `-`) for branch/folder naming.
3. Parse the `description:` value; strip a leading `/specify` token if present — the remainder is the feature description.
4. Create and check out branch `claude/issue-<issue-number>-<slug>` from `main`.
5. Invoke the `specify` command (`.claude/commands/specify.md`) with the parsed feature name + description. It writes `Docs/Specs/<YY_MM_DD_HH>_<slug>/spec.md` and normally "presents it to the user and stops" per `.claude/rules/workflow.md` — capture that same summary/questions content as text for step 8, since nobody is watching this run's chat output directly.
6. `git add`, `git commit`, `git push -u origin claude/issue-<issue-number>-<slug>`.
7. `gh pr create --title "<feature name>" --base main --head claude/issue-<issue-number>-<slug> --body "Closes #<issue-number>

<brief summary>"`.
8. `gh pr comment <pr-number> --body "<spec summary + questions>"`.
9. Stop. Do **not** run `/plan` — that stays a manual step the user triggers separately, even after the spec is finalized.

## Case B — new PR comment (`GITHUB_EVENT_NAME=issue_comment`)

The workflow only fires this for comments on PRs (`.issue.pull_request` present), from `KonH`.

1. Read `.comment.body` from the event payload — the human's reply — and `.issue.number` for the PR number.
2. `gh pr view <pr-number> --json headRefName -q .headRefName` to get the branch name, then `git fetch origin <branch>` and check it out.
3. Read the current `spec.md` on that branch, edit it directly to incorporate the answer(s) — a normal file edit, not a from-scratch `/specify` re-run.
4. `git add`, `git commit`, `git push` to the same branch.
5. `gh pr comment <pr-number> --body "..."` summarizing what changed. If all open questions are resolved, say so explicitly and note the spec is ready for `/plan` (manual, user-triggered). If questions remain, ask them in this same comment.

## Non-goals

- Never touch issues that don't match the `topic:`/`description:` convention.
- Never advance to `/plan` or `/implement` automatically — this command's scope ends at a reviewed, PR'd spec.
