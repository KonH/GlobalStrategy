# Plan: Spec Kit Adoption

## Goal

Introduce the Spec Kit methodology: a `/specify` command that captures feature intent and acceptance criteria before planning, a `Docs/Constitution.md` that encodes the project's non-negotiable architectural principles, and updates to `/plan` and `/implement` to wire everything together. No code changes — only documentation and command files.

## Approach

Create or update seven files in sequence:
1. `Docs/Constitution.md` — architectural ground rules inferred from existing project rules
2. `.claude/commands/specify.md` — new slash command for the spec phase
3. `.claude/commands/plan.md` — updated to support spec-linked plans and a constitution-check gate
4. `.claude/commands/implement.md` — updated to look in `Docs/Specs/` first, then `Docs/Plans/`
5. `.claude/commands/plan-review.md` — updated with dual-location discovery
6. `.claude/rules/workflow.md` — add `/specify` and constitution-check as explicit approval checkpoints
7. `CLAUDE.md` — add `/specify`, `Docs/Constitution.md`, and `Docs/Specs/` to the Configuration Index

## Agent Steps

- [x] **Write `Docs/Constitution.md`** — list the project's non-negotiable architectural principles as a short bullet-first document. Cover: Unity 6 + URP (no Built-in RP), ECS for all game logic in `src/` (no game logic in Unity MonoBehaviours), VContainer for DI (no manual `new` or singletons outside the container), UI Toolkit only (no Canvas/UGUI), plan-before-implement discipline (no code without an approved plan), spec-before-plan for feature work (purely technical tasks may skip the spec), `Docs/Plans/` for legacy/technical tasks and `Docs/Specs/<index>_<name>/` for feature spec+plan pairs, single `.asmdef` per feature folder, C# code-style rules (tabs, `_` prefix, braces always). Keep each principle to one sentence with a one-line rationale.

- [x] **Write `.claude/commands/specify.md`** — new slash command. Structure mirrors `/plan`: spawn an architect sub-agent briefed with the user's feature description and the project rules; the sub-agent writes `Docs/Specs/<index>_<name>/spec.md`. Index is zero-padded two-digit, derived from a **shared counter** across both `Docs/Specs/` and `Docs/Plans/`: find the highest numeric prefix already used in either directory and add 1. The first spec created after this plan will be `32_` (since `Docs/Plans/` currently goes up to `31_`). Spec format sections: **Feature Intent** (user story: "As a … I want … so that …"), **Acceptance Criteria** (Given/When/Then bullets), **Out of Scope** (explicit exclusions), **Ambiguities** (`[NEEDS CLARIFICATION: …]` markers for anything the architect cannot resolve from context). After writing, the orchestrator presents the spec to the user and stops — the user must approve (or request changes) before `/plan` is run. Do not write any plan or code.

- [x] **Rewrite `.claude/commands/plan.md`** — update the opening summary sentence to read: `Create a plan for the requested task and save it to Docs/Specs/<index>_<name>/plan.md (if a spec exists) or Docs/Plans/<index>_<short-name>.md (otherwise).` Then keep all existing content (orchestration pattern, step-block structure, format rules) and add:
  - **Spec detection:** if `$ARGUMENTS` contains a spec folder name or index, or if a `Docs/Specs/` folder with a matching index exists and has no `plan.md` yet, read `Docs/Specs/<index>_<name>/spec.md` and include a **Spec** section at the top of the plan (verbatim summary of intent + acceptance criteria). Output the plan to `Docs/Specs/<index>_<name>/plan.md` instead of `Docs/Plans/`.
  - **No-spec path:** if no spec folder is found, write to `Docs/Plans/` as before (for migrations, refactors, and other purely technical tasks).
  - **Constitution-check gate:** the architect sub-agent reads `Docs/Constitution.md` before finalising the plan and appends a **Constitution Check** section: either "No conflicts found — plan aligns with all principles." or a list of principles the plan would violate, each with a proposed resolution. The orchestrator surfaces any violations to the user before presenting the final plan; if there are violations the user must confirm the resolution before the plan is written.
  - Keep the closing line: `Use /implement to start working on the plan or request changes.`

- [x] **Rewrite `.claude/commands/implement.md`** — update the opening summary sentence to read: `Implement the plan at Docs/Specs/<index>_<name>/plan.md or Docs/Plans/<index>_<name>.md (whichever is found).` Then change the plan-discovery logic to: (1) if `$ARGUMENTS` is provided, resolve it against `Docs/Specs/` first (look for `Docs/Specs/<arg>/plan.md` or a folder whose name starts with `<arg>`), then fall back to `Docs/Plans/<arg>`; (2) if no argument is provided, search both `Docs/Specs/` (any `*/plan.md`) and `Docs/Plans/` for the file with the highest numeric prefix and use that one. Remove no other content — all orchestration, pre-flight, and completion rules stay unchanged.

- [x] **Update `.claude/commands/plan-review.md`** — change the opening line and discovery logic to search both `Docs/Specs/` (any `*/plan.md`) and `Docs/Plans/` when no argument is provided; highest numeric prefix wins. When `$ARGUMENTS` is given, resolve it against `Docs/Specs/` first, then `Docs/Plans/`.

- [x] **Update `.claude/rules/workflow.md`** — in the "Explicit approval checkpoints" list add: (a) `After /specify writes a spec — present it to the user and stop; do not run /plan until the user approves` and (b) `After /plan surfaces constitution violations — present each violation and wait for user to confirm resolution before finalising the plan`.

- [x] **Update `CLAUDE.md`** — in the Configuration Index section, add three entries: `- **Specify command:** .claude/commands/specify.md — creates Docs/Specs/<index>_<name>/spec.md before planning`, `- **Constitution:** Docs/Constitution.md — non-negotiable architectural principles; checked by the plan command`. Update the existing Plan command entry to note that plans for feature work go to `Docs/Specs/<index>_<name>/plan.md` and technical tasks go to `Docs/Plans/`. Also note `Docs/Specs/` as the home for spec+plan pairs.

Use /implement to start working on the plan or request changes.
