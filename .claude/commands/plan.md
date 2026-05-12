Create a plan for the requested task and save it to `Docs/Plans/<index>_<short-name>.md`.

## Orchestration

Spawn an **architect sub-agent** (general-purpose) to design and write the plan. Brief it with:
- The user's task description
- Relevant project rules (from `CLAUDE.md` and `.claude/rules/`)
- Existing plan files in `Docs/Plans/` (list them so it picks the next index)
- The output path and format rules below

The architect writes the plan file directly. You (orchestrator) then:
1. Present the plan contents to the user
2. Collect feedback and re-brief the architect if changes are needed (iterate until approved)
3. Run `/plan-review` as a final check — present any concerns one by one and ask the user to approve each fix
4. Stop and wait for the user to run `/implement`

## Plan File Rules

- Filename prefix is a zero-padded two-digit index reflecting creation order: `00_`, `01_`, `02_`, etc.
  - `00_` is reserved for reference/context documents
  - Feature plans start at `01_` and increment for each new plan
  - Check existing files in `Docs/Plans/` to determine the next index
- Filename body is a short kebab-case description (e.g. `01_map-prototype.md`)
- Structure: goal, approach, steps — keep it concise
- If the plan touches any code under `src/`, include a **Tests** section covering what unit/integration tests should be added or updated
- Do NOT make any code, asset, or file changes — only write the plan document
- End every plan with the line: `Use /implement to start working on the plan or request changes.`
