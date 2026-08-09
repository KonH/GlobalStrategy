Implement the plan, using the shared `k:implement` skill. The project-specific addition is that interactive implementation of a plan touching Unity assets/scenes needs a live Unity Editor MCP connection.

## Synchronize with main

Before doing any implementation work, synchronize the current branch with the actual remote main branch:

1. Confirm the working tree is clean with `git status --short`; if it is not, stop and report the existing changes.
2. Run `git fetch origin main`, then `git merge origin/main`.
3. Resolve every merge conflict, stage the resolutions, and complete the merge commit before continuing. If a conflict cannot be resolved confidently, stop and ask the user; never discard either side of a conflict.

Do not change code, assets, or tests until the branch contains the fetched `origin/main` and the working tree is clean.

## Issue automation stage label

When invoked while processing a GitHub issue/PR automation item (known item number `N`): **before** starting implement work, set `ai-implement` and remove `ai-specify` / `ai-plan`:

```
gh api repos/KonH/GlobalStrategy/issues/<N>/labels -f "labels[]=ai-implement"
gh api -X DELETE repos/KonH/GlobalStrategy/issues/<N>/labels/ai-specify
gh api -X DELETE repos/KonH/GlobalStrategy/issues/<N>/labels/ai-plan
```

(Ignore 404 on deletes if a sibling stage label was not present.) Skip this block for interactive non-issue work.

When that automation run **finishes** `/implement`, the parent issue/PR handoff uses `ai-complete` only — do **not** apply `ai-need-attention` for implement completion (note any skipped Editor-only verification in the summary comment instead).

## Unity MCP pre-flight override

- In an interactive session, if the plan touches Unity assets or scenes: verify Unity Editor is connected via MCP (`mcpforunity://instances`) before starting. If not available, stop and ask the user to open Unity Editor and reconnect MCP.
- In an unattended automation run (including issue automation and Ralph runs carrying an automation environment marker): skip the Unity MCP connection check and never block the implementation stage waiting for it. Follow that automation's existing headless rules for excluding, skipping, or reporting Editor-only work.
- If the plan only touches `src/` (plain C# project): skip the MCP check entirely.
- Brief each developer sub-agent on Unity MCP usage and `asmdef` format alongside the general code-style rules the skill already asks for.
- For steps touching `src/`: write the test for the new behavior first so it fails against the current code, then implement until it passes — never disable or weaken an existing test to force a pass, fix the underlying code instead.
- After any change under `src/`, finish by running `/dotnet-build Release` before handoff (see `.claude/rules/workflow.md` and `.cursor/rules/src-dotnet-build-release.mdc`).

## Delegate

Invoke the `k:implement` skill (from the `k` plugin) with the overrides above. It handles plan discovery within `Docs/Specs/`, phase sizing, sub-agent orchestration, and the final `/code-review` pass.
