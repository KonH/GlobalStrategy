Create a plan for the requested task and save it to `Docs/Specs/<index>_<name>/plan.md` (if a spec exists) or `Docs/Plans/<index>_<short-name>.md` (otherwise).

## Orchestration

Spawn an **architect sub-agent** (general-purpose) to design and write the plan. Brief it with:
- The user's task description
- Relevant project rules (from `CLAUDE.md` and `.claude/rules/`)
- The contents of `Docs/Constitution.md`
- Existing plan files in `Docs/Plans/` and spec folders in `Docs/Specs/` (list them so it picks the next index)
- The output path and format rules below

The architect writes the plan file directly. You (orchestrator) then:
1. Present the plan contents to the user
2. Collect feedback and re-brief the architect if changes are needed (iterate until approved)
3. Run `/plan-review` as a final check — present any concerns one by one and ask the user to approve each fix
4. Stop and wait for the user to run `/implement`

## Spec Detection

If `$ARGUMENTS` contains a spec folder name or index, or if a `Docs/Specs/` folder with a matching index exists and has no `plan.md` yet:
- Read `Docs/Specs/<index>_<name>/spec.md`
- Brief the architect with the spec contents
- Include a **Spec** section at the top of the plan (verbatim summary of intent and acceptance criteria from the spec)
- Output the plan to `Docs/Specs/<index>_<name>/plan.md`

If no spec folder is found, write to `Docs/Plans/` as usual — this path is for migrations, refactors, and other purely technical tasks.

## Constitution-Check Gate

The architect sub-agent reads `Docs/Constitution.md` before finalising the plan and appends a **Constitution Check** section to the plan:
- Either: `No conflicts found — plan aligns with all principles.`
- Or: a numbered list of principles the plan would violate, each with a one-sentence proposed resolution

The orchestrator surfaces any violations to the user before presenting the final plan. If there are violations, the user must confirm each resolution before the plan is written. Do not write the plan file if unresolved violations exist.

## Plan File Rules

- Filename prefix is a zero-padded two-digit index reflecting creation order: `00_`, `01_`, `02_`, etc.
  - `00_` is reserved for reference/context documents
  - Feature plans start at `01_` and increment for each new plan
  - The index is shared across `Docs/Plans/` and `Docs/Specs/` — check both to determine the next index
- Filename body is a short kebab-case description (e.g. `01_map-prototype.md`)
- Structure: goal, approach, steps — keep it concise
- If the plan touches any code under `src/`, include a **Tests** section covering what unit/integration tests should be added or updated
- Do NOT make any code, asset, or file changes — only write the plan document
- End every plan with the line: `Use /implement to start working on the plan or request changes.`

## Step Block Structure

Split steps into two sections:

**Section 1 — Agent Steps** (Claude performs autonomously via file edits and MCP tools):
- Use markdown checkboxes: `- [ ] **Step title** — concise description`

**Section 2 — User Steps** (requires manual Unity Editor interaction, visual inspection, or external tools):
- Use numbered headings (`### N. Title`) with body text — no checkboxes
