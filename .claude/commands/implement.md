Implement the plan at `Docs/Plans/$ARGUMENTS`. If no argument is provided, find the latest plan in `Docs/Plans/` by sorting file names and picking the last one (files are prefixed with a numeric index, e.g. `02_map-improvements.md`).

Rules:
- Read the plan file before starting; follow its steps in order
- Follow all project standards and rules defined in `CLAUDE.md` and `.claude/rules/`
- Before starting: verify Unity Editor is connected via MCP by checking `mcpforunity://instances`; if not available, stop and ask the user to open Unity Editor and reconnect MCP
- Use MCP tools (see `.claude/rules/unity/mcp_usage.md`)
- After each step, verify it works before moving to the next (check console errors, compilation)
- Do not add features, refactor unrelated code, or deviate from the plan scope
