Implement the plan at `Docs/Plans/$ARGUMENTS`. If no argument is provided, find the latest plan in `Docs/Plans/` by sorting file names and picking the last one (files are prefixed with a numeric index, e.g. `02_map-improvements.md`).

## Orchestration

Read the plan file first. Then decide based on plan size:

- **1–2 steps or trivial changes:** implement inline (no sub-agent needed)
- **3+ steps or Unity scene/asset work:** spawn a **developer sub-agent** per major phase

When spawning a developer sub-agent, brief it with:
- The full plan text
- Which step(s) it is responsible for
- Relevant project rules (`CLAUDE.md`, `.claude/rules/`) — especially Unity MCP usage, code style, asmdef format
- Current file state / context it needs (read key files first and include excerpts)
- Whether Unity Editor MCP is available (check `mcpforunity://instances` if the plan touches Unity assets/scenes)

After each sub-agent phase:
1. Relay its results and any compilation errors to the user
2. Verify the work (check console errors, read changed files) before moving to the next phase
3. If a phase fails, diagnose and re-brief the sub-agent with the fix context — do not skip ahead

## Pre-flight Checks

- Follow all project standards in `CLAUDE.md` and `.claude/rules/`
- If the plan touches Unity assets or scenes: verify Unity Editor is connected via MCP (`mcpforunity://instances`). If not available, stop and ask the user to open Unity Editor and reconnect MCP.
- If the plan only touches `src/` (plain C# project): skip the MCP check entirely.

## Completion

After all steps are done:
- If any changes touch `src/`: write or update tests for the affected logic
- Run `/code-review` on the changed files — present any concerns one by one and ask the user to approve each fix before applying it
