Create a pull request for the current branch, using the shared `cc:pr` skill.

## Delegate

Invoke the `cc:pr` skill (from the `cc` plugin). If that skill is not in this session's skill list, read `~/.claude/plugins/marketplaces/claude-tools/plugins/cc/skills/pr/SKILL.md` (or the matching cache copy under `~/.claude/plugins/cache/claude-tools/`) and follow it.

It handles status/diff/log over the full default-branch..HEAD range, drafting the title and body, pushing if needed, `gh pr create`, and opening the PR in the browser.
