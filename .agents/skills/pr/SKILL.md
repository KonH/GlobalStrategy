---
name: pr
description: Create a pull request for the current branch using the shared Codex workflow.
---

# Pull Request

Invoke `cd:pr` (from the `cd` plugin). It handles git status/diff/log over the full default-branch..HEAD range, drafting the title and body, pushing if needed, `gh pr create`, and opening the PR in the browser.
