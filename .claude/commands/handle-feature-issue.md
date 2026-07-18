Poll GitHub for new feature-request issues from the repo owner and drive them through `/specify`, opening a PR with the spec and any clarifying questions. This command is invoked by a scheduled Routine (see `.claude/rules/github_issue_automation.md`) — it must be fully self-contained since each firing starts a fresh session with no memory of previous runs, and **no MCP tools are available in that fresh session** — do everything below via `Bash` (`curl` + `jq` against the GitHub REST API, plus plain `git`), never `mcp__github__*`.

Repo: `KonH/GlobalStrategy`. Author to act on: `KonH`. Base branch: `main`.

## Auth

`$GITHUB_TOKEN` is already present in the environment. Every API call needs:

```
-H "Authorization: Bearer $GITHUB_TOKEN" -H "Accept: application/vnd.github+json"
```

**Important:** this token authenticates as `KonH`'s own GitHub account — comments and PRs the automation creates are indistinguishable from KonH's own activity *by author alone*. See the marker convention below; it's the only reliable way to tell "Claude's own prior comment" apart from "a new human reply."

## Comment marker convention

Every comment this command posts must start with this exact first line:

```
<!-- claude-automation -->
```

(an HTML comment — invisible when rendered on GitHub). This is how later steps recognize "we already said this" vs. "the human just replied."

## 1. Find candidate issues

```
curl -s -H "Authorization: Bearer $GITHUB_TOKEN" -H "Accept: application/vnd.github+json" \
  "https://api.github.com/search/issues?q=repo:KonH/GlobalStrategy+is:issue+is:open+author:KonH"
```

For each item, read its `body` text directly (no need for brittle regex — just read it). Skip any issue whose body doesn't contain a line starting with `topic:` and a line starting with `description:` (case-insensitive, either order) — it isn't a feature-automation request, leave it untouched.

## 2. Classify each matching issue

```
curl -s -H "Authorization: Bearer $GITHUB_TOKEN" -H "Accept: application/vnd.github+json" \
  "https://api.github.com/repos/KonH/GlobalStrategy/pulls?state=all&per_page=100"
```

Look for a PR whose `head.ref` starts with `claude/issue-<issue-number>-`.

- **No such PR** → **new request** → go to step 3.
- **PR exists and is open** → fetch its comments (PRs share the issues comments endpoint):
  ```
  curl -s -H "Authorization: Bearer $GITHUB_TOKEN" -H "Accept: application/vnd.github+json" \
    "https://api.github.com/repos/KonH/GlobalStrategy/issues/<pr-number>/comments"
  ```
  Look at the **last** comment in the array:
  - Its body starts with `<!-- claude-automation -->` → that was Claude's own comment, nothing new since → skip this issue this cycle.
  - Otherwise → it's a new human reply → go to step 4.
- **PR exists but closed/merged** → done, skip.

## 3. New request: write the spec and open a PR

1. Parse the `topic:` line's value → feature name. Slugify it (lowercase, spaces/punctuation → `-`) for branch/folder naming.
2. Parse the `description:` line's value; strip a leading `/specify` token if present — the remainder is the feature description.
3. `git fetch origin main`, then create and check out branch `claude/issue-<issue-number>-<slug>` from `origin/main`.
4. Invoke the `specify` command/skill (`.claude/commands/specify.md`) with the parsed feature name + description. It writes `Docs/Specs/<YY_MM_DD_HH>_<slug>/spec.md` and normally "presents it to the user and stops" per `.claude/rules/workflow.md` — here, capture that same summary/questions content as text to post in step 7 instead of just saying it in the chat turn.
5. `git add`, `git commit`, `git push -u origin claude/issue-<issue-number>-<slug>`.
6. Open the PR:
   ```
   jq -n --arg title "<feature name>" --arg head "claude/issue-<issue-number>-<slug>" \
         --arg base "main" --arg body "Closes #<issue-number>

   <brief summary>" \
     '{title:$title, head:$head, base:$base, body:$body}' | \
   curl -s -X POST -H "Authorization: Bearer $GITHUB_TOKEN" -H "Accept: application/vnd.github+json" \
     https://api.github.com/repos/KonH/GlobalStrategy/pulls -d @-
   ```
   Note the returned `.number` — that's the PR number, same as the issue-comments endpoint number below.
7. Post the spec's presentation/questions content as a PR comment, marker line first:
   ```
   jq -n --arg body "<!-- claude-automation -->
   <spec summary + questions>" '{body:$body}' | \
   curl -s -X POST -H "Authorization: Bearer $GITHUB_TOKEN" -H "Accept: application/vnd.github+json" \
     https://api.github.com/repos/KonH/GlobalStrategy/issues/<pr-number>/comments -d @-
   ```
8. Stop. Do **not** run `/plan` — that stays a manual step the user triggers separately, even after the spec is finalized.

## 4. Existing PR: incorporate a reply

1. Re-fetch the PR's comments (same endpoint as step 2) to read the full thread — the question(s) Claude asked and the human's latest answer(s).
2. `git fetch origin <branch>` and check it out. Read the current `spec.md`, edit it directly to incorporate the answers (this is a normal file edit — not a from-scratch `/specify` re-run).
3. `git add`, `git commit`, `git push` to the same branch.
4. Post a follow-up comment (marker line first, same as step 7 above) summarizing what changed. If all open questions are resolved, say so explicitly and note the spec is ready for `/plan` (manual, user-triggered). If questions remain, ask them in this same comment.

## Non-goals

- Never touch issues that don't match the `topic:`/`description:` convention.
- Never touch issues or PRs authored by anyone other than `KonH`.
- Never advance to `/plan` or `/implement` automatically — this command's scope ends at a reviewed, PR'd spec.
