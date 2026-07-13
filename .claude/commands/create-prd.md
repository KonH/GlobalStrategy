Create `.ralph/prd.md` (the Ralph loop task list) from an approved spec's plan.

Arguments: `$ARGUMENTS` — the spec index (e.g. `45`).

## Steps

1. Resolve the spec folder `Docs/Specs/<index>_<name>/` from the index. If no folder matches the index, or the folder has no `plan.md`, stop and report the problem — do not guess.
2. Read `plan.md` (implementation steps) and `spec.md` (acceptance criteria context).
3. Overwrite `.ralph/prd.md` with:
   - Header: spec name, one-paragraph goal, link to the spec folder
   - The standard "How this file works" section (keep it from the existing template)
   - A `## Tasks` JSON array derived from the plan's steps
4. Reset `.ralph/activity.md` to its header only (`# Ralph Activity Journal` + intro line + `---`).
5. Report: number of tasks created, and list any plan steps that could NOT be given a headless gate (see below) so the user knows what stays manual.

## Task rules

- **Atomic** — one logical change per task, small enough for a single loop iteration; split large plan steps.
- **Ordered** — respect the plan's dependency order; do not reorder phases.
- **No invented scope** — tasks come from the plan only; if the plan is ambiguous, keep the task's `steps` conservative.
- Every task: `{ "category", "description", "steps": [...], "gate": "<command>", "passes": false }`.
- `gate` is the **strongest verification** available for that task:
  - `src/` C# changes → `dotnet build src/GlobalStrategy.Core.sln -c Release` or `dotnet test src/GlobalStrategy.Core.sln`
  - config/JSON/pipeline changes → a `.venv\Scripts\python.exe` validation script (cross-check country IDs etc., per `.claude/rules/config_validation.md`)
  - Unity-side scripts/assets → Unity MCP: `refresh_unity`, then `read_console(types=["error"])` must report no errors (combine with `dotnet build` if `src/` was also touched)
  - Purely visual/UX outcomes (layout looks right, animation feels right) → gate on the compile/console check above, set `"category": "unity-manual"`, and add a final step "needs manual visual check" — the user finalizes these after the run; never fabricate a meaningless always-green gate and present it as full verification.
