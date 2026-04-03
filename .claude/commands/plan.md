Create a plan for the requested task and save it to `Docs/Plans/<index>_<short-name>.md`.

Rules:
- Filename prefix is a zero-padded two-digit index reflecting creation order: `00_`, `01_`, `02_`, etc.
  - `00_` is reserved for reference/context documents
  - Feature plans start at `01_` and increment for each new plan
  - Check existing files in `Docs/Plans/` to determine the next index
- Filename body is a short kebab-case description (e.g. `01_map-prototype.md`)
- Structure: goal, approach, steps — keep it concise
- Write the file immediately without asking for approval first; then iterate based on user feedback
