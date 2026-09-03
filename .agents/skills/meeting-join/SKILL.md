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
- Read the scripts' **stdout**, never their exit code. Every meeting script
  exits 0 and reports one prefix-tagged line (`APPENDED:` / `TURN:` /
  `ACK:` / `MESSAGE:` / `MATCH:` / `ENDED:` / `KICKED:` / `TIMEOUT:` /
  `ERROR:`) — see "Script output contract" in the canonical file. Do not
  append `; echo EXIT=$?` or check `$LASTEXITCODE`.
- Fill the `joined` line with `Provider` `Codex`; use the model and effort
  reported by the current session, falling back to `unknown` and `default`.
  Work out the `Title` per "Choosing a title" in the canonical file.
