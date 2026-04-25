Implement the plan at `Docs/Plans/$ARGUMENTS`. If no argument is provided, find the latest plan in `Docs/Plans/` by sorting file names and picking the last one (files are prefixed with a numeric index, e.g. `02_map-improvements.md`).

Rules:
- Read the plan file before starting; follow its steps in order
- Follow all project standards and rules defined in `CLAUDE.md` and `.claude/rules/`
- Before starting: check whether the plan touches Unity assets or scenes. If yes, verify Unity Editor is connected via MCP by checking `mcpforunity://instances`; if not available, stop and ask the user to open Unity Editor and reconnect MCP. If the plan only touches `src/` (plain C# project), skip the MCP check entirely.
- Use MCP tools for Unity work (see `.claude/rules/unity/mcp_usage.md`); `src/` work uses only file tools and `dotnet` CLI
- After each step, verify it works before moving to the next (check console errors, compilation)
- Do not add features, refactor unrelated code, or deviate from the plan scope
- If any changes touch `src/` (core or game non-Unity layer), write or update tests for the affected logic before finishing
- After all steps are complete, run `/code-review` on the changed files — present any concerns one by one and ask the user to approve each fix before applying it
