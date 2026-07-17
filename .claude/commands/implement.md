Implement the plan, using the shared `k:implement` skill. The project-specific addition is that any plan touching Unity assets/scenes needs a live Unity Editor MCP connection.

## Unity MCP pre-flight override

- If the plan touches Unity assets or scenes: verify Unity Editor is connected via MCP (`mcpforunity://instances`) before starting. If not available, stop and ask the user to open Unity Editor and reconnect MCP.
- If the plan only touches `src/` (plain C# project): skip the MCP check entirely.
- Brief each developer sub-agent on Unity MCP usage and `asmdef` format alongside the general code-style rules the skill already asks for.
- For steps touching `src/`: write the test for the new behavior first so it fails against the current code, then implement until it passes — never disable or weaken an existing test to force a pass, fix the underlying code instead.

## Delegate

Invoke the `k:implement` skill (from the `k` plugin) with the overrides above. It handles plan discovery within `Docs/Specs/`, phase sizing, sub-agent orchestration, and the final `/code-review` pass.
