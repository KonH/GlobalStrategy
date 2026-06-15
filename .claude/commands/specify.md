Capture feature intent and acceptance criteria before planning begins. Writes `Docs/Specs/<index>_<name>/spec.md` and stops — the user must approve the spec before running `/plan`.

## Index Derivation

The spec index is shared with `Docs/Plans/`. To find the next index:
1. Use `Glob` with pattern `Docs/Plans/*.md` to list plan files
2. Use **Bash** (`ls Docs/Specs`) to list spec folders — do NOT use Glob with a trailing slash (`Docs/Specs/*/`), it silently returns nothing on Windows
3. Extract the leading numeric prefix from each name/folder
4. Take the highest number found across both directories and add 1
5. Zero-pad to two digits (e.g. `35`)

## Orchestration

Spawn an **architect sub-agent** (general-purpose) briefed with:
- The user's feature description
- Relevant project rules from `CLAUDE.md` and `.claude/rules/`
- The output path: `Docs/Specs/<index>_<name>/spec.md`
- The spec format below

The architect writes the spec file directly. You (orchestrator) then:
1. Present the spec contents to the user
2. Collect feedback and re-brief the architect if changes are needed (iterate until the user approves)
3. **Stop.** Do not run `/plan` or write any code. The user must explicitly request the next step.

## Spec Format

```markdown
# Spec: <Feature Name>

## Feature Intent

As a <role>, I want <capability>, so that <benefit>.

## Acceptance Criteria

- **Given** <precondition> **When** <action> **Then** <outcome>
- (one bullet per observable behaviour; cover the happy path and the most important edge cases)

## Out of Scope

- (explicit exclusions — things the feature deliberately will not do)

## Ambiguities

- [NEEDS CLARIFICATION: <question the architect cannot resolve from context alone>]
- (omit this section entirely if there are no ambiguities)
```

## Rules

- Do NOT write any plan, code, or assets — only the spec document.
- Use `[NEEDS CLARIFICATION: …]` markers freely — surfacing unknowns early is the point.
- The spec folder name uses kebab-case: `Docs/Specs/32_my-feature/spec.md`.
- Do not create `plan.md` in the spec folder — that is `/plan`'s job.
