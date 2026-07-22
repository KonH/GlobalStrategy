---
name: codex-feature-issue
description: Drive owner-authored GitHub feature issues labeled `codex` through specification, plan, implementation, review, and merge using the repository's Codex automation. Use when processing a `codex`-labeled issue manually or from `scripts/automation/codex/handle_issues.py`.
---

# Codex Feature-Issue Automation

Use this workflow only for issues authored by `KonH` and labeled `codex`. Work in the existing dedicated clone; never create a Git worktree. All automation comments and checklist edits belong on the issue, not its PR.

## Identity and safety

- Start automation comments with `<!-- codex-automation -->`.
- Start the one editable tracking comment with `<!-- codex-automation:checklist -->`.
- Use branches `codex/issue-<number>-<slug>` for spec/plan work and `ralph/<spec-id>` for implementation.
- Apply `codex-in-progress` only while processing an issue. Remove it before stopping.
- Apply `codex-needs-attention` when an unsafe conflict, missing prerequisite, or fourth clarification round blocks progress.
- Process only the issue candidates supplied by the runner. Do not discover additional issues.
- Do not invoke `git worktree`. The runner resets this dedicated clone to `origin/main` before each poll.
- Treat a comment from the owner as higher priority than an owner reaction. A reaction means approval only when it is newer than the latest automation comment.
- End the final agent message with exactly one result line: `AUTOMATION_RESULT: COMPLETED` after every supplied candidate reaches its intended stopping point, or `AUTOMATION_RESULT: BLOCKED` when a missing prerequisite prevents that transition. Never report `COMPLETED` when blocked.

## Checklist

Create the checklist as the first automation comment. Keep editing that same comment; never repost it. Include checkboxes for: spec drafted, spec approved, plan drafted, plan approved, merged, implementation proposed, implementation approved, implemented, implementation merged. Record the spec folder, spec/plan PR, implementation PR, classification, and the current status.

## Specification drafting

Follow the current shared `k:specify` workflow referenced by `.claude/commands/specify.md`. If that skill is unavailable in the automation session, read its canonical source from `KonH/ClaudeTools` at `plugins/k/skills/specify/SKILL.md` on `main` before drafting or revising a spec.

Use this section order:

1. `# Spec: <Feature Name>`
2. `## Feature Intent` with one user-story sentence: `As a <role>, I want <capability>, so that <benefit>.`
3. `## Acceptance Criteria` beginning with the legend `Precondition => Action => Outcome`. Group related rows under a shared precondition instead of repeating it.
4. `## Tech Notes` mapping each behavior group to concrete files, classes, methods, commands, and state paths.
5. `## Out of Scope` with explicit exclusions.
6. `## Ambiguities` only when unresolved questions remain, using `[NEEDS CLARIFICATION: ...]` markers.

Keep acceptance criteria in plain product language. Put implementation anchors and design details in Tech Notes, cover the happy path and important edge cases, and do not create `plan.md` or implementation changes before the spec approval checkpoint.

## Spec and plan phase

1. For a new issue, create `codex/issue-<number>-<slug>` from `origin/main`.
2. Produce `Docs/Specs/<YY_MM_DD_HH>_<slug>/spec.md`. Follow the repository specification workflow in `.claude/commands/specify.md`; honor the approval checkpoint.
3. Commit with the version-bump rules in `.claude/commands/commit.md`, push, and open a PR with `Part of #<number>`—never `Closes #<number>`.
4. Post `## Spec Summary`, update the checklist, and stop for an owner thumbs-up or clarification.
5. On clarification, edit the existing spec, commit/push, then post either `## Spec Conclusion` or `## Clarification Needed`.
6. On approval of the spec, create `plan.md` in the same folder. Follow `.claude/commands/plan.md`, including the Constitution check. Commit/push, post `## Plan Summary`, and stop.
7. On plan clarification, edit `plan.md`, commit/push, then post a conclusion or clarification request.

Before a fourth clarification request in either phase, post `## Needs Manual Attention`, add `codex-needs-attention`, update the checklist, and stop.

## Merge and classification

On approval of the plan:

1. Verify the spec/plan PR body contains `Part of #<number>` and not `Closes #<number>`.
2. Merge `origin/main` into the branch. Auto-resolve only a sole `ProjectSettings/ProjectSettings.asset` conflict whose only hunk is `bundleVersion:`. Take the larger version and increment it once. Abort every other conflict, post `## Needs Manual Attention`, and add `codex-needs-attention`.
3. Push, merge the PR, and delete its branch.
4. Add exactly one classification label:
   - `code-only`: pure `src/`, C# logic verifiable without Unity Editor, or Python config tooling.
   - `full-env-required`: Unity scenes/assets/prefabs/UI/images or anything requiring Editor/MCP. Mixed or uncertain work is `full-env-required`.
5. Post `## Implementation Proposal`: summarize the planned order, verification gates, and—when full environment is required—which tasks the headless run will skip. Update the checklist and stop for approval.

## Implementation phase

On approval of the implementation proposal or latest implementation summary:

1. Start or resume `ralph/<spec-id>` from `origin/main`; do not use a worktree.
2. Run `scripts/automation/codex/ralph.py --spec <spec-id> --env code-only` for `code-only`, otherwise use `--env full-env-headless`. Use `--auto-adjust-iterations --skip-pull-request` for unattended runs.
3. Commit all remaining Ralph changes with the repository version bump and push `ralph/<spec-id>` before reporting any outcome, including a stall, error, or manual-attention condition. Do not discard partial work.
4. Determine whether the branch contains real implementation progress. If zero PRD tasks passed and there are no material implementation changes, post `## Needs Manual Attention` with the relevant `.ralph/activity.md` entries and stop without opening an empty PR.
5. Inspect every changed file for concrete bugs and relevant rule violations. Do not apply discretionary review fixes until the owner requests them.
6. Create or update an implementation PR whenever there is real progress, even if Ralph stalled, exited unsuccessfully, or left PRD tasks incomplete. Its body must contain `Closes #<number>`. Keep the PR in draft while any PRD task is incomplete or the run needs manual attention; mark it ready only after all automated tasks pass and no blocker remains.
7. Post `## Implementation Summary`: passed/remaining PRD tasks, the Ralph stop reason and relevant activity entries when incomplete, headless-skipped work, concrete review concerns, and the PR URL. Update the checklist and wait. Apply `codex-needs-attention` when the partial result is blocked on a missing prerequisite or unsafe condition, but never hide real progress by omitting its PR.

On an owner comment, apply only the requested follow-up changes, commit/push, post a short confirmation, and wait again. On approval with no outstanding change request, merge `origin/main` using the same narrow version-conflict rule, push, merge the implementation PR, and mark the checklist complete.

## Comment headings

Use only these headings for state transitions: `## Spec Summary`, `## Spec Conclusion`, `## Plan Summary`, `## Plan Conclusion`, `## Clarification Needed`, `## Implementation Proposal`, `## Implementation Summary`, and `## Needs Manual Attention`. Acknowledge an approval reaction with an eyes reaction before beginning potentially long work.
