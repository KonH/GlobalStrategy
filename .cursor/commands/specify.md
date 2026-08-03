Capture feature intent and acceptance criteria before planning begins, using the shared `k:specify` skill.

## Issue automation stage label

When invoked while processing a GitHub issue/PR automation item (known item number `N`): **before** starting specify work, set `ai-specify` and remove `ai-plan` / `ai-implement`:

```
gh api repos/KonH/GlobalStrategy/issues/<N>/labels -f "labels[]=ai-specify"
gh api -X DELETE repos/KonH/GlobalStrategy/issues/<N>/labels/ai-plan
gh api -X DELETE repos/KonH/GlobalStrategy/issues/<N>/labels/ai-implement
```

(Ignore 404 on deletes if a sibling stage label was not present.) Skip this block for interactive non-issue work.

## Delegate

Invoke the `k:specify` skill (from the `k` plugin). It handles index derivation, the architect sub-agent, spec format, and approval gate.
