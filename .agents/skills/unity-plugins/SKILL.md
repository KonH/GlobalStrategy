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
what to do on `UP_TO_DATE` / `REBUILT` / failure, and the rule that plugin
DLLs are never committed.
