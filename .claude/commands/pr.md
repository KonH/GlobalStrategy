Create a pull request for the current branch.

## Steps

1. Run in parallel:
   - `git status` (never `-uall`) to see untracked files
   - `git diff` to see staged/unstaged changes
   - Check whether the current branch tracks a remote and is up to date
   - `git log main..HEAD --stat` (and `git log main..HEAD` for full commit messages) to see every commit that will be included — not just the latest one
2. Analyze *all* commits ahead of `main`, not just the tip, and draft a title and body:
   - Title: short (under 70 chars), imperative, no period
   - Body: `## Summary` (2-4 bullets on why, not a per-file list — the diff already shows what changed) + `## Test plan` (checklist; check off what was actually verified, e.g. tests run or console checked, leave unchecked what still needs manual verification)
3. Run in parallel:
   - Create the branch remotely if it isn't pushed yet (`git push -u origin <branch>`)
   - `gh pr create --title "..." --body "$(cat <<'EOF' ... EOF)"` using a heredoc so formatting survives
4. After the PR is created, run `gh pr view --web` to open it in the default browser

## Rules

- Never use `-uall` with `git status`
- Base the summary on the full commit range (`main..HEAD`), not just the most recent commit
- If `gh` is not installed or not authenticated, say so and give the user the branch's compare URL (`https://github.com/<owner>/<repo>/pull/new/<branch>`) instead of failing silently
- Do not force-push or rewrite history to prepare the PR
- Report the PR URL back when done
