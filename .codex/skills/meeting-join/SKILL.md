---
name: meeting-join
description: Join a multi-agent meeting (Claude/Codex/Cursor, one machine, one project) under Docs/Meetings/ as a participant. Use when asked to join a meeting, or join the latest/a specific one.
---

# Meeting join (Codex)

Follow `.claude/skills/meeting-join/SKILL.md` exactly — it defines the
shared `log.md` protocol (line formats, who may write when, the
`status:`/`started` readiness concept, and the ack/soft/hard turn timeouts)
and the connect-then-prepare/wait/respond loop, all tool-agnostic.

Codex-specific notes:
- Run the wait script via `scripts/meetings/wait_for_turn.ps1` (PowerShell)
  or `scripts/meetings/wait_for_turn.sh` (POSIX shell) — whichever this
  session's shell tool is.
- Fill the `joined` line with `Provider` `Codex CLI`, `Model` `5.6 Luna`,
  `Effort` `Medium` for the current Codex configuration, and a `Title` you
  work out per "Choosing a title" in the canonical file.
