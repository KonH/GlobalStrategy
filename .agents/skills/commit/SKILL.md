---
name: commit
description: Apply GlobalStrategy's version, local plugin rebuild, font-asset restore, and usage-stat requirements, then create a commit with the shared Codex workflow.
---

# Commit Changes

Read and follow `.claude/commands/commit.md` through its project-specific version bump, font-asset restore, conditional local Release plugin rebuild (do not commit DLLs), and best-effort usage scan. When that command reaches its Claude delegate, invoke `cd:commit` instead of `cc:commit`.

The project steps are authoritative for what must be staged. The shared `cd:commit` skill is authoritative for branch selection and commit-message rules.
