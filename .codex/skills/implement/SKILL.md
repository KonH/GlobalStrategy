---
name: implement
description: Implement a plan via /implement for GlobalStrategy. When processing a GitHub issue/PR automation item, set ai-implement before starting; finish with ai-complete only.
---

# Implement (Codex)

Follow `.claude/commands/implement.md` (including the issue-automation stage-label block and Unity MCP pre-flight override). That command delegates to the shared `k:implement` skill.

When finishing `/implement` under issue/PR automation, the parent handoff uses `ai-complete` only — do not apply `ai-need-attention` for implement completion.
