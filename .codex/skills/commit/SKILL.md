---
name: commit
description: Create a repository commit using GlobalStrategy's required workflow. Use when the user asks to commit staged or current changes in the GlobalStrategy repository.
---

# Commit Changes

1. Read `ProjectSettings/ProjectSettings.asset` and locate `bundleVersion`.
2. Split its `X.YYY` value at the period. Keep `X` unchanged and increment only the unbounded integer `YYY`; do not treat it as a decimal.
3. Edit the version line, then stage `ProjectSettings/ProjectSettings.asset` with `git add`.
4. Bump the top-level `"version"` field in `Assets/Configs/game_settings.json` to the same value and stage it. That field is what the main-menu version label shows — `MainMenuDocument` reads `GameSettings.Version`, not `Application.version`, because the Web build profile embeds its own PlayerSettings snapshot and could ship a stale `bundleVersion`.
5. Check `git diff --cached --name-only` for any staged path under `src/`. If there is one, the `Assets/Plugins/Core/*.dll` files are stale: run `dotnet build src/GlobalStrategy.Core.sln -c Release > .tmp/dotnet-build.log 2>&1` (no `cd`), read the log, stop and report on failure, otherwise `git add Assets/Plugins/Core/*.dll` and delete the log. Skip this step if no staged path is under `src/`.
6. Run `python scripts/stats/collect_usage.py --scan` once. Treat failures as non-blocking: report them, then continue.
7. Follow the available `k:commit` skill to select a branch if necessary, review the staged scope, choose an intentional commit message, and create the commit.

Do not create a commit before the version bump is staged. Do not change the major bundle-version marker. Do not create a commit before rebuilt DLLs are staged when `src/` changed.
