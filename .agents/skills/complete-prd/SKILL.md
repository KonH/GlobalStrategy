---
name: complete-prd
description: Finish a GlobalStrategy Ralph run, publish its pull request, and clear run artifacts in a separate commit.
---

# Complete Ralph PRD

Follow `.claude/commands/complete-prd.md` exactly for GlobalStrategy's spec, bot-evaluation, performance-benchmark, and Ralph artifact rules.

Where that command names project commands, use the local `commit` adapter and the shared `cd:pr` skill. Keep the mandatory cleanup as a separate follow-up commit on the same pull-request branch.
