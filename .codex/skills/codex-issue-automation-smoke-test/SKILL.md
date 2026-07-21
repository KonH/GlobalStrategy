---
name: codex-issue-automation-smoke-test
description: Validate the unattended Codex GitHub-issue automation on issue #41 by applying its in-progress label and eyes reaction, creating a contained test commit, pushing it, and opening a draft PR. Use only when deliberately smoke-testing the dedicated automation checkout.
---

# Codex Issue Automation Smoke Test

Use only shell `gh` and `git` commands. Do not use GitHub connector tools or browser tools.

1. Require a clean checkout before changing anything. If `git status --porcelain` is non-empty, stop with `AUTOMATION_RESULT: BLOCKED` and identify the paths; do not delete or reset them.
2. Verify `GH_TOKEN` is present and `gh api user --jq .login` succeeds. Stop blocked if authentication fails.
3. Add the `codex-in-progress` label to issue #41 and add an `eyes` reaction to issue #41 itself:

   ```powershell
   gh issue edit 41 --repo KonH/GlobalStrategy --add-label codex-in-progress
   gh api --method POST -H "Accept: application/vnd.github+json" repos/KonH/GlobalStrategy/issues/41/reactions -f content=eyes
   ```

4. Abort if `codex/smoke-issue-41` already exists locally or on `origin`; this test must not reuse or overwrite a prior branch. Otherwise fetch `origin/main` and create `codex/smoke-issue-41` from it.
5. Create only `Docs/AutomationSmokeTests/issue-41-codex-smoke-test.md`. State that it was produced by the Codex issue-automation smoke test and include the current UTC timestamp.
6. Commit that file with `Add Codex automation smoke-test marker`, push `codex/smoke-issue-41`, and create a draft PR to `main` titled `Smoke test: Codex issue automation`. Its body must say `Part of #41` and identify the PR as temporary test output.
7. End the final agent message with `AUTOMATION_RESULT: COMPLETED`. If any prerequisite or command fails, make no further changes and end with `AUTOMATION_RESULT: BLOCKED`.

<!-- Initializer template kept inert below.

## Structuring This Skill

[TODO: Choose the structure that best fits this skill's purpose. Common patterns:

**1. Workflow-Based** (best for sequential processes)
- Works well when there are clear step-by-step procedures
- Example: DOCX skill with "Workflow Decision Tree" -> "Reading" -> "Creating" -> "Editing"
- Structure: ## Overview -> ## Workflow Decision Tree -> ## Step 1 -> ## Step 2...

**2. Task-Based** (best for tool collections)
- Works well when the skill offers different operations/capabilities
- Example: PDF skill with "Quick Start" -> "Merge PDFs" -> "Split PDFs" -> "Extract Text"
- Structure: ## Overview -> ## Quick Start -> ## Task Category 1 -> ## Task Category 2...

**3. Reference/Guidelines** (best for standards or specifications)
- Works well for brand guidelines, coding standards, or requirements
- Example: Brand styling with "Brand Guidelines" -> "Colors" -> "Typography" -> "Features"
- Structure: ## Overview -> ## Guidelines -> ## Specifications -> ## Usage...

**4. Capabilities-Based** (best for integrated systems)
- Works well when the skill provides multiple interrelated features
- Example: Product Management with "Core Capabilities" -> numbered capability list
- Structure: ## Overview -> ## Core Capabilities -> ### 1. Feature -> ### 2. Feature...

Patterns can be mixed and matched as needed. Most skills combine patterns (e.g., start with task-based, add workflow for complex operations).

Delete this entire "Structuring This Skill" section when done - it's just guidance.]

## [TODO: Replace with the first main section based on chosen structure]

[TODO: Add content here. See examples in existing skills:
- Code samples for technical skills
- Decision trees for complex workflows
- Concrete examples with realistic user requests
- References to scripts/templates/references as needed]

## Resources (optional)

Create only the resource directories this skill actually needs. Delete this section if no resources are required.

### scripts/
Executable code (Python/Bash/etc.) that can be run directly to perform specific operations.

**Examples from other skills:**
- PDF skill: `fill_fillable_fields.py`, `extract_form_field_info.py` - utilities for PDF manipulation
- DOCX skill: `document.py`, `utilities.py` - Python modules for document processing

**Appropriate for:** Python scripts, shell scripts, or any executable code that performs automation, data processing, or specific operations.

**Note:** Scripts may be executed without loading into context, but can still be read by Codex for patching or environment adjustments.

### references/
Documentation and reference material intended to be loaded into context to inform Codex's process and thinking.

**Examples from other skills:**
- Product management: `communication.md`, `context_building.md` - detailed workflow guides
- BigQuery: API reference documentation and query examples
- Finance: Schema documentation, company policies

**Appropriate for:** In-depth documentation, API references, database schemas, comprehensive guides, or any detailed information that Codex should reference while working.

### assets/
Files not intended to be loaded into context, but rather used within the output Codex produces.

**Examples from other skills:**
- Brand styling: PowerPoint template files (.pptx), logo files
- Frontend builder: HTML/React boilerplate project directories
- Typography: Font files (.ttf, .woff2)

**Appropriate for:** Templates, boilerplate code, document templates, images, icons, fonts, or any files meant to be copied or used in the final output.

---

**Not every skill requires all three types of resources.**
-->
