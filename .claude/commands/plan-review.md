Review the latest plan in `Docs/Plans/` (or the plan file specified in $ARGUMENTS) for potential problems before implementation begins.

Rules:
- Read the plan file first, then read relevant project rules from `.claude/rules/` that apply to the plan's scope
- Spawn a sub-agent to perform the review independently (it should re-read the plan and rules itself)
- The sub-agent must NOT modify any files — review only
- Present concerns one by one to the user and ask for approval before suggesting any fix
- Keep each point short: state the problem, why it matters, and a concise fix — no nitpicking on style or naming
- Focus on: missing steps, wrong assumptions, inconsistency with project guidelines, architectural mismatches, and anything likely to cause rework during implementation
- If no real concerns are found, say so briefly and stop

Sub-agent prompt template:
> Review the plan at `[plan path]`. Read it in full, then read the relevant `.claude/rules/` files for the plan's scope (Unity, ECS, UI, C#, etc.). Identify concerns — missing steps, wrong assumptions, guideline violations, architectural mismatches — that would cause problems during implementation. Return a numbered list of concerns, each with: problem, why it matters, proposed fix. Skip style/naming nitpicks. If there are no real concerns, say so.
