# Ralph Iteration Instructions

You are one iteration of an autonomous Ralph loop. Every iteration starts with fresh context —
everything you need to know about prior progress lives in the files below, not in memory.

## Steps

1. Read `.ralph/prd.md` (the task list) and `.ralph/activity.md` (journal of previous iterations).
2. Pick exactly **ONE** task: the first entry with `"passes": false` whose prerequisites are already
   done and that is not marked `ENV-BLOCKED` in `.ralph/activity.md` (see the blocked-task rule below).
3. Implement it. Follow all project rules (`CLAUDE.md` and `.claude/rules/`).
4. Run the task's verification gate (the `gate` field on the task). Typical gates:
   - `dotnet build src/GlobalStrategy.Core.sln -c Release`
   - `dotnet test src/GlobalStrategy.Core.sln`
   - a validation script via `.venv\Scripts\python.exe`
   - Unity-side changes: Unity MCP - `refresh_unity`, then `read_console(types=["error"])` must report no errors
5. Only if the gate passes: set that task's `"passes"` to `true` in `.ralph/prd.md`.
6. Append an entry to `.ralph/activity.md`: date, task description, what you changed (files),
   gate command + result, and anything the NEXT iteration must know (decisions, gotchas, blockers).
7. Commit everything: `git add` the changed files (including `.ralph/prd.md` and `.ralph/activity.md`),
   commit message `ralph: <short task description>`.

## Rules

- One task per iteration. Never start a second task, even if the first was quick.
- Never set `"passes": true` without its gate actually passing — paste the gate output evidence into `activity.md`.
- If blocked: write the blocker and what you attempted into `.ralph/activity.md`, leave `"passes": false`, and end your turn normally.
- If the block is because the task's gate needs a tool that is structurally unavailable in this run
  (e.g. Unity MCP in a `full-env-headless` run), do not just repeat the same attempt every iteration —
  journal it as `ENV-BLOCKED: <task description> - <reason>` in `.ralph/activity.md` and pick a
  different eligible task instead. Check `.ralph/activity.md` for an existing `ENV-BLOCKED` entry for
  a task before picking it again in step 2; if this task's own gate previously failed in this same run
  because of an unavailable tool (not because you made a mistake worth retrying), it stays `ENV-BLOCKED`
  and skipped for the rest of this run — do not spend further iterations re-attempting it. This should
  be rare: `/create-prd` should not have planned a task with an unavailable gate in the first place
  (see the environment-marker rules in `.claude/commands/create-prd.md`); this is a fallback for a task
  that slips through anyway.
- Respect the environment notice supplied by the runner. In `code-only` and `full-env-headless`
  automation, Unity Editor/MCP and image-generation tools are unavailable: do not probe for or invoke
  them, and leave excluded asset, scene, and image tasks untouched. With no environment notice, Unity
  MCP is expected to be available; if it is unreachable, treat the task as blocked and never mark it
  passed without its gate.
- Purely visual/UX outcomes cannot be fully verified in this loop: implement them, pass the
  compile/console gate, and note "needs manual visual check" in `.ralph/activity.md`.
- If ALL tasks in `.ralph/prd.md` have `"passes": true`, make no changes and output exactly:
  `<promise>COMPLETE</promise>`
