---
name: update-branch
description: Merge the repo's default branch (main) into the current feature branch, auto-resolving the two known version-bump conflict spots (bundleVersion in ProjectSettings.asset, "version" in game_settings.json) and stopping to report anything else that needs manual resolution. Load when the user asks to update/sync/merge main into the current branch, or resolve a merge conflict caused by the version bump.
---

# Update Branch

Keeps a feature branch current with `main` without the two version-bump lines
turning into a manual chore every time — `/commit` bumps
`ProjectSettings/ProjectSettings.asset`'s `bundleVersion` and
`Assets/Configs/game_settings.json`'s `"version"` on every commit (see
`.claude/commands/commit.md`), so merging in `main` after any other branch has also
committed almost always conflicts on exactly those two lines. This skill resolves
that specific, well-understood conflict automatically and leaves everything else for
a human (or a follow-up review) to look at.

## Preconditions

1. `git status --porcelain` must be empty. If there are uncommitted changes, stop and
   ask the user to commit or stash first — do not merge on top of a dirty tree.
2. Don't run this on the default branch itself. `git branch --show-current` — if it
   equals the default branch (see below), tell the user there's nothing to update
   into and stop.

## Steps

1. `git fetch origin`
2. Resolve the default branch name: `git symbolic-ref --short refs/remotes/origin/HEAD`
   (strip the `origin/` prefix), falling back to whichever of `main`/`master` exists
   locally if that ref isn't set.
3. `git merge origin/<default-branch> --no-edit`
4. **If the merge succeeds with no conflicts**, report the resulting commit (or "already
   up to date") and stop — nothing else to do.
5. **If the merge conflicts**, inspect `git status --porcelain` for `UU` (and `AA`/`DU`
   as applicable) entries:
   - **`ProjectSettings/ProjectSettings.asset` conflicting only on the `bundleVersion:`
     line, and/or `Assets/Configs/game_settings.json` conflicting only on the
     `"version":` line** — auto-resolve per "Version conflict resolution" below.
   - **Any other conflicted file** — leave it as-is. Do not attempt to resolve
     ordinary code/content conflicts automatically; list every remaining conflicted
     file for the user and stop with the merge still in progress (do not commit a
     partial resolution).
6. Once every conflicted file has been resolved (auto or by the user) and none remain
   in `git status --porcelain`, `git add` the resolved files and `git commit --no-edit`
   to complete the merge.
7. If `git diff HEAD^2 HEAD^1...HEAD --name-only` (i.e. what `main` brought in) touches
   `src/`, remind the user to run `/dotnet-build Release` — `main`'s own DLLs already
   reflect its `src/` changes, so this is precautionary, not required to complete the
   merge itself.

## Version conflict resolution

Both files use the same `X.YYY` scheme (`X` = human-set milestone, `YYY` = plain
counter — see `.claude/commands/commit.md` for the full rule: never touch `X`,
never treat `X.YYY` as a decimal).

1. Read both conflicting versions out of the conflict markers:
   `bundleVersion: X.YYY_ours` / `X.YYY_theirs`, and `"version": "X.YYY_ours"` /
   `"X.YYY_theirs"`.
2. **If `X` differs between the two sides**, stop and ask the user — that means a
   human bumped the milestone on one side, and picking automatically would be wrong.
3. Otherwise the merge is combining two independent `YYY + 1` bumps (ours from this
   branch's last `/commit`, theirs from whatever landed on `main` since). Resolve to
   `X.{max(YYY_ours, YYY_theirs) + 1}` — this preserves both increments instead of
   silently dropping one side's bump.
4. Replace the conflict block (`<<<<<<< HEAD` ... `=======` ... `>>>>>>> origin/<default-branch>`)
   with the single resolved line in both files, keeping existing formatting (two-space
   indent and no quotes for `bundleVersion:`; quoted for `"version":`).
5. `git add` both files once resolved.

## Notes

- This only ever auto-resolves the version-bump lines — it never touches a conflict in
  gameplay/config/script content, even a trivial-looking one. When in doubt, leave it
  for the user.
- If `Assets/Plugins/Core/*.dll` shows up as a binary conflict, that means both sides
  changed `src/` and the built DLL bytes differ. Resolve the underlying `.cs` conflicts
  under `src/` first, then run `/dotnet-build Release` (which overwrites
  `Assets/Plugins/Core/`) and `git add` the regenerated DLLs — don't try to pick a side
  on the binary directly.
