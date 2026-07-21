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

- **`code-only`** — this is an unattended environment with no Unity Editor/MCP or image-generation capability. Plan only `src/`, Python tooling, and pure C# `Assets/Scripts/` work that has a headless `dotnet build`/`dotnet test`/Python gate. Never plan a Unity asset, scene, prefab, ScriptableObject, texture, visual-import, or image-generation step; leave every such plan step out of `.ralph/prd.md` and record it verbatim in `plan.md`'s `## Automation Notes`. The Ralph session must leave those steps untouched: it must not probe for, invoke, or retry Unity or image-generation tools.
- **`full-env-headless`** — `plan.md` may include Unity asset/scene/image work, but this run cannot use Unity MCP or the image-generation pipeline to create or verify anything. Split each plan step accordingly:
  - **C# script changes inside the Unity project** (`Assets/Scripts/`) — still plan the task (write the code), but set `"category": "unity-headless"` and never invent a `refresh_unity`/`read_console` gate that can't actually run in this environment. `gate` is `dotnet test src/GlobalStrategy.Core.sln`/`dotnet build ...` only if the same task also touches `src/`; otherwise state directly in `steps` that Unity-side compilation is unverified and must be checked by a human with the Editor open.
  - **Unity asset/scene/prefab/ScriptableObject work, or image generation** — anything that would need `manage_asset`/`manage_prefabs`/`manage_scene`/`manage_gameobject`/`manage_scriptable_object`/`manage_texture` (`.claude/rules/unity/mcp_usage.md`) or `.claude/rules/image_generation.md` — do **not** create a task for it at all; leave it out of `.ralph/prd.md` entirely.
  - Append a `## Automation Notes` section to `plan.md` itself (create it if absent, at the end of the file) listing every skipped step verbatim from the plan, so a human picking this up later sees exactly what the automated pass left undone without having to diff `.ralph/prd.md` against `plan.md` by hand.
  - In the step-5 report, list the skipped steps under their own heading (e.g. `## Skipped (needs Unity Editor)`), separate from the normal `unity-manual` list — the two mean different things: `unity-manual` steps were attempted and gated as far as possible, skipped steps were never attempted at all.

For either automation marker, a task that has already failed the same gate three times is a blocker, not another loop task. Record the three attempts and stop the Ralph run for manual attention; do not create a fourth variation of the same attempt.
