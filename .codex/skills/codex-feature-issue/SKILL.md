---
name: codex-feature-issue
description: Drive owner-authored GitHub feature issues labeled `codex` through specification, plan, implementation, review, and merge using the repository's Codex automation. Use when processing a `codex`-labeled issue manually or from `scripts/handle_codex_feature_issues.py`.
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

## Checklist

Create the checklist as the first automation comment. Keep editing that same comment; never repost it. Include checkboxes for: spec drafted, spec approved, plan drafted, plan approved, merged, implementation proposed, implementation approved, implemented, implementation merged. Record the spec folder, spec/plan PR, implementation PR, classification, and the current status.

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
2. Run `scripts/codex_ralph.py --spec <spec-id> --env code-only` for `code-only`, otherwise use `--env full-env-headless`. Use `--auto-adjust-iterations --skip-pull-request` for unattended runs.
3. If it stalls, exits unsuccessfully, or completes zero tasks, post `## Needs Manual Attention` with the relevant `.ralph/activity.md` entries and stop.
4. Otherwise inspect the changed files for concrete bugs and relevant rule violations. Do not apply discretionary review fixes until the owner requests them.
5. Commit remaining changes with the repository version bump, push `ralph/<spec-id>`, and create or update an implementation PR whose body contains `Closes #<number>`.
6. Post `## Implementation Summary`: passed/remaining PRD tasks, headless-skipped work, concrete review concerns, and the PR URL. Update the checklist and wait.

On an owner comment, apply only the requested follow-up changes, commit/push, post a short confirmation, and wait again. On approval with no outstanding change request, merge `origin/main` using the same narrow version-conflict rule, push, merge the implementation PR, and mark the checklist complete.

## Comment headings

Use only these headings for state transitions: `## Spec Summary`, `## Spec Conclusion`, `## Plan Summary`, `## Plan Conclusion`, `## Clarification Needed`, `## Implementation Proposal`, `## Implementation Summary`, and `## Needs Manual Attention`. Acknowledge an approval reaction with an eyes reaction before beginning potentially long work.
