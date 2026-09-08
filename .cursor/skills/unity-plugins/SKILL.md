---
name: unity-plugins
description: >-
  Regenerates gitignored Unity plugin DLLs under Assets/Plugins/Core from src/.
  MUST be loaded and followed before any Unity Editor, Unity MCP, Assets/Scripts,
  scene, prefab, play-mode, or E2E work starts — including when the Editor is
  already open.
---

# (CURSOR) Unity plugin DLLs

Follow `.claude/skills/unity-plugins/SKILL.md` exactly. Run
`python scripts/unity/ensure_plugin_dlls.py` from the project root before the
first Unity action in the session. Do not commit plugin DLLs.
