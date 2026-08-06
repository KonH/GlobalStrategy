Capture feature intent and acceptance criteria before planning begins, using the shared `k:specify` skill.

## Synchronize with main

Before doing any specify work, synchronize the current branch with the actual remote main branch:

1. Confirm the working tree is clean with `git status --short`; if it is not, stop and report the existing changes.
2. Run `git fetch origin main`, then `git merge origin/main`.
3. Resolve every merge conflict, stage the resolutions, and complete the merge commit before continuing. If a conflict cannot be resolved confidently, stop and ask the user; never discard either side of a conflict.

Do not begin research or write a spec until the branch contains the fetched `origin/main` and the working tree is clean.

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
