---
name: implement
description: Implement a GlobalStrategy plan with synchronization, automation labels, Unity MCP policy, and required Release validation.
---

# Implement

Follow `.claude/commands/implement.md` for GlobalStrategy synchronization, stage labels, Unity MCP policy, repository rules, and completion labeling. When it reaches its Claude delegate, invoke `cd:implement` instead of `cc:implement`.

When changes touch `src/`, finish with the project `dotnet-build` skill in Release mode as required by `.claude/rules/workflow.md`.
