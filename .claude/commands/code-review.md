Review the code changes made during the current implementation session against project guidelines and common sense.

Rules:
- Collect the list of files changed (via `git diff --name-only HEAD` or from context)
- Spawn a sub-agent to review those files independently — it should read each changed file and the relevant `.claude/rules/` files that apply
- The sub-agent must NOT modify any files — review only
- Present all concerns at once, then ask the user to approve all or indicate which to skip
- Apply all approved fixes, then confirm when done
- Keep each point short: state the problem, why it matters, and a concise fix — no nitpicking on style or naming
- Format proposed fixes as `diff` blocks (lines removed prefixed with `-`, lines added prefixed with `+`) alongside the plain-text explanation
- Focus on: guideline violations (code style, asmdef, VContainer, UI Toolkit patterns), logic bugs, missing null checks at system boundaries, inconsistency with existing patterns in touched files
- Skip: refactoring suggestions unrelated to the plan, test coverage gaps unless the plan required tests, cosmetic issues

Sub-agent prompt template:
> Review the following changed files: [file list]. Read each file in full, then read the relevant `.claude/rules/` files for the scope (Unity, ECS, UI, C#, etc.). Identify concerns — guideline violations, logic bugs, missing boundary checks, inconsistency with project patterns. Return a numbered list of concerns, each with: file and location, problem, why it matters, proposed fix. For each fix, include a `diff` block showing the before/after lines (lines removed prefixed with `-`, lines added with `+`). Skip style/naming nitpicks and refactoring suggestions outside the plan scope. If there are no real concerns, say so.
