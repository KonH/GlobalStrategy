Create `.ralph/prd.md` (the Ralph loop task list) from an approved spec's plan.

Arguments: `$ARGUMENTS` — the spec index (e.g. `45`), optionally followed by an environment marker: `code-only` or `full-env-headless` (e.g. `45 full-env-headless`). Omit the marker for normal interactive use, where a Unity Editor with MCP connected is expected to be available.

## Steps

1. Resolve the spec folder `Docs/Specs/<index>_<name>/` from the index. If no folder matches the index, or the folder has no `plan.md`, stop and report the problem — do not guess.
2. Read `plan.md` (implementation steps) and `spec.md` (acceptance criteria context).
3. Overwrite `.ralph/prd.md` with:
   - Header: spec name, one-paragraph goal, link to the spec folder
   - The standard "How this file works" section (keep it from the existing template)
   - A `## Tasks` JSON array derived from the plan's steps — see the environment-marker rules below when `$ARGUMENTS` includes one
4. Reset `.ralph/activity.md` to its header only (`# Ralph Activity Journal` + intro line + `---`).
5. Report: number of tasks created, and list any plan steps that could NOT be given a headless gate (see below) so the user knows what stays manual — plus, for `full-env-headless` runs, the separate skipped-steps list from the environment-marker rules.

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

## Environment marker (headless automation only)

When `$ARGUMENTS` includes `code-only` or `full-env-headless`, this run has no interactive Unity Editor/MCP connection — it's driven by unattended automation (see `.claude/commands/handle-feature-issue.md`), not a developer session with the Editor open. Adjust task planning accordingly; a run with no marker at all keeps the normal rules above unchanged.

- **`code-only`** — the plan is already confined to `src/` and/or pure C# `Assets/Scripts/` changes with no Unity asset/scene edits (this is exactly what the `code-only` issue label promises — see `.claude/rules/github_issue_automation.md`). Plan tasks exactly as usual; the existing gates (`dotnet build`/`dotnet test`/python validation) never needed Unity MCP in the first place, so nothing changes.
- **`full-env-headless`** — `plan.md` may include Unity asset/scene/image work, but this run cannot use Unity MCP or the image-generation pipeline to create or verify anything. The dividing line is **not** "is this Unity-related" but "does this task have a gate that can actually run without the Editor" — a task with no such gate must not be planned at all, no exceptions:
  - **A plan step is only ever turned into a Ralph task if it has a real headless gate**: `src/` changes verified by `dotnet build`/`dotnet test src/GlobalStrategy.Core.sln`, or config/JSON/pipeline changes verified by a `.venv\Scripts\python.exe` validation script. Plan these exactly as usual.
  - **Every other step is excluded from `.ralph/prd.md` entirely** — do not create a task for it, do not attempt it, do not leave it half-planned with a note in `steps` that it's "unverified." This includes:
    - Unity asset/scene/prefab/ScriptableObject work or image generation (`manage_asset`/`manage_prefabs`/`manage_scene`/`manage_gameobject`/`manage_scriptable_object`/`manage_texture`, `.claude/rules/unity/mcp_usage.md`, `.claude/rules/image_generation.md`)
    - **Any C# script change under `Assets/Scripts/` that has no `src/` counterpart** — Unity-side script compilation cannot be checked by `dotnet build`/`dotnet test` at all (those solutions don't include the Unity project), so the *only* real verification is `refresh_unity` + `read_console`, which this run doesn't have. Do not plan these as a "best-effort, gate on whatever's available" task — a task with no real gate is exactly the failure mode that stalled a headless run on GlobalStrategy#41 (a `unity-headless` task kept getting picked and re-attempted every iteration because nothing could ever make it pass). Treat it the same as asset/scene work: leave it out.
  - There is no `unity-headless` category for `full-env-headless` runs anymore — a task either has a real headless gate (`src/` or config/python) or it is not a task, only a note.
  - Append a `## Automation Notes` section to `plan.md` itself (create it if absent, at the end of the file) listing every skipped step verbatim from the plan, so a human picking this up later sees exactly what the automated pass left undone without having to diff `.ralph/prd.md` against `plan.md` by hand.
  - In the step-5 report, list all skipped steps together under `## Skipped (needs Unity Editor)` — these were never attempted at all, unlike `unity-manual` tasks (see the Task rules above), which were implemented and gated as far as possible and only need a final human visual check.
