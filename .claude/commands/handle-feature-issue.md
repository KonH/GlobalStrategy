Poll GitHub for new feature-request issues from the repo owner and drive them through `/specify`, opening a PR with the spec and any clarifying questions. This command is invoked by a scheduled Routine (see `.claude/rules/github_issue_automation.md`) — it must be fully self-contained since each firing starts a fresh session with no memory of previous runs.

Repo: `KonH/GlobalStrategy`. Author to act on: `KonH`. Base branch: `main`.

## 1. Find candidate issues

Use the GitHub tools (`search_issues`, `issue_read`, `pull_request_read`, `list_pull_requests`) — never `git` — for anything that reads or writes issues/PRs/comments on GitHub itself. Use plain `git`/local file tools for the actual code/spec changes.

Search: open issues in `KonH/GlobalStrategy` authored by `KonH`.

For each issue, check whether its body contains a line starting with `topic:` and a line starting with `description:` (case-insensitive, order doesn't matter). If it doesn't match this convention, skip the issue entirely — it isn't a feature-automation request, leave it alone.

## 2. Classify each matching issue

Search for an existing PR whose head branch is `claude/issue-<issue-number>-*` (`list_pull_requests` with `head: "KonH:claude/issue-<issue-number>"` won't match a prefix — instead list open PRs and filter by branch name prefix client-side, or use `search_pull_requests` with `query: "repo:KonH/GlobalStrategy is:pr head:claude/issue-<issue-number>"`).

- **No matching PR exists** → this is a **new request**. Go to step 3.
- **An open PR exists** → check who posted the most recent comment (`pull_request_read` method `get_comments`, last item).
  - Most recent comment author is `KonH` → there's a new, unanswered reply. Go to step 4.
  - Most recent comment author is Claude's own account (or there are no comments beyond the initial spec-summary one Claude posted) → nothing to do, skip this issue this cycle.
- **A matching PR exists but is closed/merged** → skip; this issue is done.

## 3. New request: write the spec and open a PR

1. Parse the `topic:` line's value → this is the feature name. Slugify it (lowercase, spaces/punctuation → `-`) for branch/folder naming.
2. Parse the `description:` line's value. It conventionally starts with `/specify` — strip that leading token; the remainder is the feature description to hand to the spec skill.
3. `git fetch origin main` then create a new branch `claude/issue-<issue-number>-<slug>` from `origin/main`.
4. Invoke the `specify` command/skill (see `.claude/commands/specify.md`) with the parsed feature name + description. This writes `Docs/Specs/<YY_MM_DD_HH>_<slug>/spec.md` and normally "presents it to the user and stops" per `.claude/rules/workflow.md` — here, capture that same summary/questions content instead of just saying it in the chat turn, per step 5 below.
5. Commit the new spec file(s) and push the branch (`git add`, `git commit`, `git push -u origin claude/issue-<issue-number>-<slug>`).
6. Open a PR (`create_pull_request`) from that branch into `main`. Title: the feature name. Body: brief summary + `Closes #<issue-number>`.
7. Post the spec's presentation/questions content (whatever `/specify` would normally present in chat) as a comment on the new PR (`add_issue_comment`, using the PR number as `issue_number`).
8. Stop. Do **not** run `/plan` — that stays a manual step the user triggers separately, even after the spec is finalized.

## 4. Existing PR: incorporate a reply

1. Read the full comment thread on the PR (`pull_request_read` method `get_comments`) to get the question(s) Claude asked and the answer(s) KonH just gave.
2. Read the current `spec.md` on that PR's branch, update it to incorporate the answers (edit directly — this is a normal file edit, not a re-run of `/specify` from scratch).
3. Commit and push the update to the same branch.
4. Post a follow-up PR comment summarizing what changed. If the reply resolved all open questions, say so explicitly and note the spec is ready for `/plan` (manual, user-triggered). If questions remain, ask them in this same comment.

## Non-goals

- Never touch issues that don't match the `topic:`/`description:` convention.
- Never advance to `/plan` or `/implement` automatically — this command's scope ends at a reviewed, PR'd spec.
- Never process an issue/PR authored by anyone other than `KonH`.
