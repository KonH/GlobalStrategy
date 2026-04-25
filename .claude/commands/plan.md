Create a plan for the requested task and save it to `Docs/Plans/<index>_<short-name>.md`.

Rules:
- Filename prefix is a zero-padded two-digit index reflecting creation order: `00_`, `01_`, `02_`, etc.
  - `00_` is reserved for reference/context documents
  - Feature plans start at `01_` and increment for each new plan
  - Check existing files in `Docs/Plans/` to determine the next index
- Filename body is a short kebab-case description (e.g. `01_map-prototype.md`)
- Structure: goal, approach, steps — keep it concise
- If the plan touches any code under `src/`, include a **Tests** section covering what unit/integration tests should be added or updated
- Write the file immediately without asking for approval first; then iterate based on user feedback
- Do NOT make any code, asset, or file changes during planning — only write the plan document
- End every plan with the line: `Use /implement to start working on the plan or request changes.`
- After writing the plan, run `/plan-review` on it before stopping — present any concerns one by one and ask the user to approve each fix; then stop and wait for the user to run /implement
