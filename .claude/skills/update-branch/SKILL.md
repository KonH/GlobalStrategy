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
   - **`Assets/UI/Fonts/*.asset`** — auto-resolve per "Font asset conflict resolution"
     below unless the user explicitly asked to keep a font-asset change.
   - **Any other conflicted file** — leave it as-is. Do not attempt to resolve
     ordinary code/content conflicts automatically; list every remaining conflicted
     file for the user and stop with the merge still in progress (do not commit a
     partial resolution).
6. Once every conflicted file has been resolved (auto or by the user) and none remain
   in `git status --porcelain`, `git add` the resolved files and `git commit --no-edit`
   to complete the merge.
7. If `git diff HEAD^2 HEAD^1...HEAD --name-only` (i.e. what `main` brought in) touches
   `src/`, remind the user to run `/dotnet-build Release` (or the `unity-plugins` skill)
   so local `Assets/Plugins/Core/` DLLs match the merged sources. Do not commit those
   DLLs.

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

## Font asset conflict resolution

Unity dirties `Assets/UI/Fonts/*.asset` (dynamic SDF atlas data). Unless the user
explicitly asked to keep a font-asset change on this branch:

1. Take the incoming default-branch copy: `git checkout origin/<default-branch> -- "Assets/UI/Fonts/*.asset"`.
2. `git add` those files.
3. If they are merely dirty and not conflicted, `git restore --worktree --staged -- "Assets/UI/Fonts/*.asset"` instead.

## Notes

- This only ever auto-resolves the version-bump lines and font `.asset` atlas noise —
  it never touches a conflict in gameplay/config/script content, even a trivial-looking
  one. When in doubt, leave it for the user.
- `Assets/Plugins/Core/*.dll` are gitignored. A merge will not conflict on them. After
  merging `src/` changes, regenerate locally with `/dotnet-build Release` or the
  `unity-plugins` skill — do not commit the binaries. If an old merge still lists
  those DLLs as untracked/modified, leave them unstaged.
