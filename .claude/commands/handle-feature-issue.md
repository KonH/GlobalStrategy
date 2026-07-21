Drive a pre-filtered list of feature-request issues/PRs from the repo owner through `/specify`, opening a PR with the spec and any clarifying questions. Invoked by `scripts/handle_feature_issues.py` (via `.sh`/`.ps1` wrappers), run on a cron schedule **in the user's own environment** (not this Claude Code Remote session) — see `.claude/rules/github_issue_automation.md` for why.

The invocation prompt already contains the full candidate list — every open issue/PR labeled `claude` that was created or updated within the wrapper's lookback window, each as a `[ISSUE #N]`/`[PR #N]` block with its URL, title, and body. **Do not re-scan the repo for other candidates** — that `gh issue list`/`gh pr list` work already happened in the wrapper specifically so this process doesn't have to spend a turn (and subscription usage) rediscovering what it already knows. Process only what's in that list.

Each invocation is a fresh `claude -p` process with no memory of previous runs; use `gh` CLI (already authenticated as the repo owner in that environment) and plain `git` for everything — this command must not assume any MCP tools are present.

Repo: `KonH/GlobalStrategy`. Author to act on: `KonH`. Base branch: `main`.

## Comment marker convention

The `gh`/git credentials in the user's own environment are their own personal account — a comment or commit this automation makes is indistinguishable from the human's own activity *by author alone*. Every comment this command posts must therefore start with this exact first line:

```
<!-- claude-automation -->
```

(an HTML comment — invisible when rendered on GitHub). This is the only reliable way later runs can tell "we already said this" apart from "the human just replied."

## 1. Classify each candidate

The `claude` label on an issue is the only opt-in signal needed — no required body format. Use the issue's own title and body naturally: title = feature name, body = feature description (see step 2 for the exact parsing).

- **`[ISSUE #N]` block** → check whether a PR already exists with head branch `claude/issue-<N>-*` (`gh pr list --repo KonH/GlobalStrategy --state all --json number,headRefName`):
  - No such PR → **new request** → go to step 2.
  - PR exists and is open → treat this issue as already handled; the follow-up work happens via the `[PR #N]` block instead (see below), not here.
  - PR exists but closed/merged → done, skip.
- **`[PR #N]` block** → fetch its comments:
  ```
  gh api repos/KonH/GlobalStrategy/issues/<pr-number>/comments
  ```
  Look at the **last** comment:
  - Body starts with `<!-- claude-automation -->` → that was this automation's own comment (the PR's `updatedAt` moved because of that comment, not a new human reply) → skip.
  - Otherwise → a new human reply → go to step 3.

## 2. New request: write the spec and open a PR

1. Feature name = the issue's title, as-is. Slugify it (lowercase, spaces/punctuation → `-`) for branch/folder naming.
2. Feature description = the issue's body; strip a leading `/specify` token if the body happens to start with one (either is fine — some issues will, some won't, both mean the same thing here since the `claude` label is what actually signals intent).
3. `git fetch origin main`, then create and check out branch `claude/issue-<issue-number>-<slug>` from `origin/main`.
4. Invoke the `specify` command/skill (`.claude/commands/specify.md`) with the feature name + description. It writes `Docs/Specs/<YY_MM_DD_HH>_<slug>/spec.md` and normally "presents it to the user and stops" per `.claude/rules/workflow.md` — capture that same summary/questions content as text for step 8, since nobody is watching this process's stdout.
5. `git add`, `git commit`, `git push -u origin claude/issue-<issue-number>-<slug>`.
6. Open the PR:
   ```
   gh pr create --repo KonH/GlobalStrategy --title "<feature name>" \
     --base main --head claude/issue-<issue-number>-<slug> \
     --body "Closes #<issue-number>

   <brief summary>"
   ```
7. **Label the new PR `claude`** (`gh pr edit <pr-number> --repo KonH/GlobalStrategy --add-label claude`) — this is what makes the wrapper's next poll pick up future replies on it at all; without this the PR is invisible to `--label claude` discovery and no reply would ever be processed.
8. Post the spec's presentation/questions content as a PR comment, marker line first:
   ```
   gh pr comment <pr-number> --repo KonH/GlobalStrategy --body "<!-- claude-automation -->
   <spec summary + questions>"
   ```
9. Stop. Do **not** run `/plan` — that stays a manual step the user triggers separately, even after the spec is finalized.

## 3. Existing PR: incorporate a reply

1. Re-fetch the PR's comments (same endpoint as step 1) to read the full thread — the question(s) asked and the human's latest answer(s). The candidate block already gives the PR's current body/title; comments need the API call since they aren't included in the prompt.
2. `git fetch origin <branch>` and check it out. Read the current `spec.md`, edit it directly to incorporate the answers — a normal file edit, not a from-scratch `/specify` re-run.
3. `git add`, `git commit`, `git push` to the same branch.
4. Post a follow-up comment (marker line first, same as step 8 above) summarizing what changed. If all open questions are resolved, say so explicitly and note the spec is ready for `/plan` (manual, user-triggered). If questions remain, ask them in this same comment.

## Non-goals

- Never touch issues or PRs authored by anyone other than `KonH`.
- Never advance to `/plan` or `/implement` automatically — this command's scope ends at a reviewed, PR'd spec.
- Never re-run the repo-wide `gh issue list`/`gh pr list` discovery the wrapper already did — operate only on the candidates given in the prompt.
