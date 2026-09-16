---
name: unity-plugins
description: >-
  Regenerates gitignored Unity plugin DLLs under Assets/Plugins/Core from src/.
  MUST be loaded and followed before any Unity Editor, Unity MCP, Assets/Scripts,
  scene, prefab, play-mode, or E2E work starts — including when the Editor is
  already open.
---

# Unity plugin DLLs

Read and follow `.claude/skills/unity-plugins/SKILL.md`. That file is the
source of truth for when to run `python scripts/unity/ensure_plugin_dlls.py`,
what to do on `UP_TO_DATE` / `REBUILT` / failure, the once-per-session Unity
MCP project pin (the connected Editor must be this checkout — other clones
and worktrees share the instance name `GlobalStrategy` and are not safe to
drive), and the rule that plugin DLLs are never committed.
