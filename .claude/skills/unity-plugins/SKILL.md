---
name: unity-plugins
description: >-
  Regenerates gitignored Unity plugin DLLs under Assets/Plugins/Core from src/.
  MUST be loaded and followed before any Unity Editor, Unity MCP, Assets/Scripts,
  scene, prefab, play-mode, or E2E work starts — including when the Editor is
  already open.
---

# Unity plugin DLLs

`Assets/Plugins/Core/*.dll` (and `*.deps.json`) are **not committed**. Unity
consumes them as local build output. Before touching the Editor, MCP, or any
Unity-side asset/script, make sure those DLLs exist and are current.

## When (mandatory)

Run this skill **before the first Unity action in the session**, whenever the
task will:

- use Unity MCP (`refresh_unity`, `manage_scene`, `manage_gameobject`,
  `read_console`, `execute_code`, …)
- edit `Assets/Scripts/`, scenes, prefabs, UXML/USS, or ScriptableObjects
- run the `unity-e2e-run` skill
- otherwise need the Editor to compile against `src/` types

Skip only when the work is purely `src/` / docs / scripts and will not open or
drive Unity.

Unattended issue automation with no Editor still runs this if the run will
execute `/dotnet-build Release` for Unity-consumed types; it is cheap when
up to date.

## Steps

1. From the project root (no `cd`), run `python scripts/unity/ensure_plugin_dlls.py`
   (prefer `.venv\Scripts\python.exe` when that venv is present).

2. Read the command output:
   - `UP_TO_DATE:` — continue.
   - `REBUILDING:` / `REBUILT:` — continue. If Unity MCP is connected, follow
     with `refresh_unity` and `read_console(types=["error"])`.
   - non-zero exit — read `.tmp/dotnet-build.log` (the script writes it) and
     **stop**. Do not start Unity work on missing/stale plugins.

3. Do **not** `git add` anything under `Assets/Plugins/Core/` except newly
   authored `*.dll.meta` files for a brand-new plugin assembly. Never commit
   `*.dll`, `*.pdb`, `*.deps.json`, or `*.xml` from that folder.

## After `src/` edits

This skill does not replace the standing `/dotnet-build Release` requirement
in `.claude/rules/workflow.md`. After changing `src/`, still Release-build
(the ensure script will no-op if it already did). Still do not commit the DLLs.

## Editor-side counterpart

Opening Unity also regenerates missing/stale DLLs via
`Assets/Scripts/Editor/PluginDlls/PluginDllRegenerator.cs`. Do not wait on
that instead of this skill — agents must ensure plugins **before** they start
Editor work. Manual force: `GS/Plugins/Regenerate Core DLLs`.
