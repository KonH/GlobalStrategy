Poll GitHub for new feature-request issues from the repo owner and drive them through `/specify`, opening a PR with the spec and any clarifying questions. Invoked by `scripts/handle_feature_issues.sh`, run on a cron schedule **in the user's own environment** (not this Claude Code Remote session) — see `.claude/rules/github_issue_automation.md` for why. Each invocation is a fresh `claude -p` process with no memory of previous runs; use `gh` CLI (already authenticated as the repo owner in that environment) and plain `git` for everything — this command must not assume any MCP tools are present.

Repo: `KonH/GlobalStrategy`. Author to act on: `KonH`. Base branch: `main`.

## Comment marker convention

The `gh`/git credentials in the user's own environment are their own personal account — a comment or commit this automation makes is indistinguishable from the human's own activity *by author alone*. Every comment this command posts must therefore start with this exact first line:

```
<!-- claude-automation -->
```

(an HTML comment — invisible when rendered on GitHub). This is the only reliable way later runs can tell "we already said this" apart from "the human just replied."

## 1. Find candidate issues

```
gh issue list --repo KonH/GlobalStrategy --author KonH --state open --json number,title,body
```

For each issue, read its `body` directly. Skip any issue whose body doesn't contain a line starting with `topic:` and a line starting with `description:` (case-insensitive, either order) — it isn't a feature-automation request, leave it untouched.

## 2. Classify each matching issue

```
gh pr list --repo KonH/GlobalStrategy --state all --json number,headRefName,state
```

Look for a PR whose `headRefName` starts with `claude/issue-<issue-number>-`.

- **No such PR** → **new request** → go to step 3.
- **PR exists and is open** → fetch its comments:
  ```
  gh api repos/KonH/GlobalStrategy/issues/<pr-number>/comments
  ```
  Look at the **last** comment:
  - Body starts with `<!-- claude-automation -->` → that was this automation's own comment, nothing new since → skip this issue this cycle.
  - Otherwise → a new human reply → go to step 4.
- **PR exists but closed/merged** → done, skip.

## 3. New request: write the spec and open a PR

1. Parse the `topic:` line's value → feature name. Slugify it (lowercase, spaces/punctuation → `-`) for branch/folder naming.
2. Parse the `description:` line's value; strip a leading `/specify` token if present — the remainder is the feature description.
3. `git fetch origin main`, then create and check out branch `claude/issue-<issue-number>-<slug>` from `origin/main`.
4. Invoke the `specify` command/skill (`.claude/commands/specify.md`) with the parsed feature name + description. It writes `Docs/Specs/<YY_MM_DD_HH>_<slug>/spec.md` and normally "presents it to the user and stops" per `.claude/rules/workflow.md` — capture that same summary/questions content as text for step 7, since nobody is watching this process's stdout.
5. `git add`, `git commit`, `git push -u origin claude/issue-<issue-number>-<slug>`.
6. Open the PR:
   ```
   gh pr create --repo KonH/GlobalStrategy --title "<feature name>" \
     --base main --head claude/issue-<issue-number>-<slug> \
     --body "Closes #<issue-number>

   <brief summary>"
   ```
7. Post the spec's presentation/questions content as a PR comment, marker line first:
   ```
   gh pr comment <pr-number> --repo KonH/GlobalStrategy --body "<!-- claude-automation -->
   <spec summary + questions>"
   ```
8. Stop. Do **not** run `/plan` — that stays a manual step the user triggers separately, even after the spec is finalized.

## 4. Existing PR: incorporate a reply

1. Re-fetch the PR's comments (same endpoint as step 2) to read the full thread — the question(s) asked and the human's latest answer(s).
2. `git fetch origin <branch>` and check it out. Read the current `spec.md`, edit it directly to incorporate the answers — a normal file edit, not a from-scratch `/specify` re-run.
3. `git add`, `git commit`, `git push` to the same branch.
4. Post a follow-up comment (marker line first, same as step 7) summarizing what changed. If all open questions are resolved, say so explicitly and note the spec is ready for `/plan` (manual, user-triggered). If questions remain, ask them in this same comment.

## Non-goals

- Never touch issues that don't match the `topic:`/`description:` convention.
- Never touch issues or PRs authored by anyone other than `KonH`.
- Never advance to `/plan` or `/implement` automatically — this command's scope ends at a reviewed, PR'd spec.
