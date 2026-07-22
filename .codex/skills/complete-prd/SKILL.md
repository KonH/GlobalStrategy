---
name: complete-prd
description: Finish a Ralph run by committing remaining work, opening or updating its pull request, then clearing the Ralph run artifacts in a separate follow-up commit.
---

# Complete Ralph PRD

Use this skill to finish an existing Ralph run. Accept the same argument forms as the Claude `/complete-prd` command: a spec index, `bot:<featureId>`, or `perf:<target>`.

1. Resolve the relevant spec context and read `.ralph/prd.md` plus `.ralph/activity.md`. Record the passed/total task count, manual-verification tasks, and blockers for the pull-request body.
2. Inspect `git status` and `git diff`. Commit any remaining implementation changes using the repository commit workflow.
3. Create or update the pull request. Keep it draft if any task is unfinished or the activity journal records a blocker. Include the Ralph-run summary and, where applicable, the eval or benchmark verdict from its committed source files. Do not alter PRD `passes` flags in this skill.
4. Once the pull request exists, clear the completed run artifacts in a **separate follow-up commit**: replace `.ralph/prd.md` with `# Ralph PRD` and a short statement that no Ralph run is active; reset `.ralph/activity.md` to its standard header only (`# Ralph Activity Journal`, its intro line, and `---`). Commit and push this cleanup to the same PR branch using the repository commit workflow.
5. Report the pull-request URL and the cleanup commit.

The cleanup commit is mandatory for ready and draft PRs alike. The PR body is the durable record of the final Ralph state; `.ralph/prd.md` and `.ralph/activity.md` must not leak completed-run state into a later run.
