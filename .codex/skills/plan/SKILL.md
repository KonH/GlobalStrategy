---
name: plan
description: Create a plan via /plan for GlobalStrategy. When processing a GitHub issue/PR automation item, set ai-plan before starting.
---

# Plan (Codex)

Before starting, follow the **Synchronize with main** section in `.claude/commands/plan.md`: fetch `origin/main`, merge it into the current branch, and resolve every conflict before doing planning work.

Follow `.claude/commands/plan.md` (including the issue-automation stage-label block and Unity User Steps override). That command delegates to the shared `k:plan` skill.
