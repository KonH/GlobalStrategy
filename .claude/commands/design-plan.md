Create a Unity implementation plan from an approved design scope. Reads the scope's prototype file and produces a step-by-step plan in `Docs/Plans/` covering all Unity UXML/USS/C# changes needed to match the design.

## Input

The user provides a design scope path, e.g. `@Design/01_prototype` or `@Design/02_icons_and_cards`.

Scope → prototype file mapping (same as `/design`):

| Scope | Prototype file |
|---|---|
| `Design/01_prototype` | `design-final.html` |
| `Design/02_icons_and_cards` | `design.html` |

## Orchestration

Spawn an **architect sub-agent** (general-purpose) to read the design and write the plan. Brief it with:

- The target prototype file path and scope name
- The full list of existing `Docs/Plans/` files (so it picks the next index)
- The relevant rule files listed below
- The output path and plan format rules (from `/plan`)

The architect must:

1. **Read the prototype HTML file** in full
2. **Identify each visual element or screen** that requires a Unity counterpart — UXML structure, USS classes, C# binding changes, asset imports (fonts, SVG icons, textures)
3. **Cross-reference existing Unity files** under `Assets/UI/`, `Assets/Scripts/Unity/UI/`, and `Assets/UI/Shared/SharedStyles.uss` to understand current state vs. target design
4. **Write the plan** grouped by layer (shared styles → fonts → icons → per-screen UXML/USS → C# binding fixes), with a concrete step for each delta

## Rules to include in the architect brief

- `.claude/rules/unity/uitoolkit.md` — USS scope, component pattern, known Unity 6 limitations (no `border-style:dashed`, no CSS grid, `Button.clicked` bug, etc.)
- `.claude/rules/unity/ui_implementation.md` — prefab structure, icon import workflow
- `.claude/rules/unity/vcontainer.md` — injection patterns for binding MonoBehaviours
- `.claude/rules/unity/localization.md` — locale key conventions
- `.claude/commands/design.md` — palette tokens, HTML→USS class mapping table, component class definitions

## Plan output rules

Follow the same rules as `/plan`:

- Next zero-padded index (check `Docs/Plans/` before writing)
- Filename: `<index>_<short-kebab-description>.md`
- Structure: **Goal → Approach → Steps** (grouped as above)
- Each step names the exact file to edit and the concrete change — no vague instructions like "update colours"
- Call out Unity 6 limitations where the HTML design uses unsupported CSS (dashed borders → workaround note; CSS grid → flex approximation note)
- Do **not** make any code or asset changes — plan document only
- End with: `Use /implement to start working on the plan or request changes.`

## After the architect writes the plan

1. Present the plan contents to the user
2. Collect feedback and re-brief the architect if changes are needed
3. Run `/plan-review` as a final check — present any concerns and ask the user to approve each fix
4. Stop and wait for the user to run `/implement`
