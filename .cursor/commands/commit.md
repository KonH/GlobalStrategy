Create a git commit for the staged changes, following GlobalStrategy's required workflow.

## 1. Version bump

Before creating the commit, increment `bundleVersion` in `ProjectSettings/ProjectSettings.asset` and stage the file.

The version format is `X.YYY` — `X` is a human-set project milestone marker, `YYY` is a plain unbounded integer counter (no zero-padding, no cap). Increment only `YYY` by 1. **Never change the major version `X`** — that is a human decision only; do not reinterpret `X.YYY` as a single decimal number.

Steps:
1. Read `ProjectSettings/ProjectSettings.asset` to find the current `bundleVersion` line.
2. Parse `X` and `YYY` as separate integers (split on the `.`).
3. Edit the file to replace `  bundleVersion: X.YYY` with `X.{YYY+1}` (keep the two leading spaces, keep `X` unchanged, no padding on the new `YYY+1`).
4. `git add ProjectSettings/ProjectSettings.asset`.
5. Bump the displayed version to the same value. `Assets/Configs/game_settings.json` has a top-level `"version"` field — that is what the main-menu version label shows (`MainMenuDocument` reads `GameSettings.Version`, not `Application.version`, because the Web build profile embeds its own PlayerSettings snapshot and could ship a stale `bundleVersion`). Edit it to the new `X.YYY` value and `git add` it.

## 2. Discard font-asset atlas noise

Unless the user explicitly asked to keep or commit a font-asset change, restore the Unity SDF font assets so atlas dirt does not land in the commit:

```
git restore --worktree --staged -- "Assets/UI/Fonts/*.asset"
```

Skip this restore only when the font `.asset` edits are the requested work (new fallback, atlas settings, adding a font).

## 3. Rebuild plugin DLLs locally (if src/ changed)

Run `git diff --cached --name-only` (or use the already-known staged file list). If any staged path is under `src/` (e.g. `.cs`/`.csproj` changes), regenerate `Assets/Plugins/Core/` for the local Editor:

1. Run `dotnet build src/GlobalStrategy.Core.sln -c Release > .tmp/dotnet-build.log 2>&1` (never prefix with `cd` — the shell already starts in the project root). Release output goes straight into `Assets/Plugins/Core/` by convention (see `.claude/rules/unity/plugins.md`).
2. Read `.tmp/dotnet-build.log`. If the build failed, stop and report the errors instead of committing.
3. Do **not** `git add` `Assets/Plugins/Core/*.dll` (or `*.deps.json` / `*.pdb` / `*.xml`) — those binaries are gitignored. Only stage a new `*.dll.meta` if this change introduces a new plugin assembly.
4. Delete `.tmp/dotnet-build.log` as a separate step.

Skip this step entirely if no staged file is under `src/`.

## 4. Usage stats catch-all scan (best-effort)

Run `python scripts/stats/collect_usage.py --scan` (or `scripts/stats/collect_usage.ps1 -Scan` / `collect_usage.sh --scan`) once. Never block or fail the commit on this step — if it errors, log the error and continue straight to the commit.

## 5. Branch selection

1. `git branch --show-current`.
2. Determine the repository's default branch (`main` or `master` — e.g. via `git symbolic-ref --short refs/remotes/origin/HEAD`, falling back to whichever of `main`/`master` exists locally).
3. If the current branch is the default branch: create and switch to a new branch via `git checkout -b feature/<short-kebab-description>`, where `<short-kebab-description>` is a meaningful slug derived from the change being committed. Do this before staging/committing anything else.
4. If already on a non-default branch, leave it as-is.

## 6. Commit

- Subject line: short, imperative, no period.
- Explain *why*, not *what* — the diff already shows what changed.
- No bullet-point summaries of changed files.
- Always add a `Co-Authored-By` trailer for the model in use.
- Never bump the major/milestone version segment automatically (see step 1).
