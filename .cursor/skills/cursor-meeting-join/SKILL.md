---
name: cursor-meeting-join
description: "(CURSOR) Join a multi-agent meeting (Claude/Codex/Cursor, one machine, one project) under Docs/Meetings/ as a participant — wait for a turn grant from the meeting owner, then respond. Use when asked to join a meeting, or join the latest/a specific one. Cursor-specific wrapper — prefer this over the Claude meeting-join skill."
---

# (CURSOR) Meeting join

Follow `.claude/skills/meeting-join/SKILL.md` exactly — it defines the
shared `log.md` protocol (line formats, who may write when, the 10-minute
turn timeout) and the join/wait/respond loop, all tool-agnostic.

If both a Claude `meeting-join` skill and this skill are listed, use this
one.

Cursor-specific notes:
- Run the wait script via `scripts/meetings/wait_for_turn.ps1` (PowerShell)
  or `scripts/meetings/wait_for_turn.sh` (POSIX shell) — whichever this
  session's shell tool is.
- Fill the `joined` line from this session's live Cursor model picker —
  never hardcode Model or Effort. Run
  `scripts/meetings/cursor_session_identity.ps1` (POSIX:
  `scripts/meetings/cursor_session_identity.sh`). It prints
  `Provider<TAB>Model<TAB>Effort` from the picker (e.g.
  `Cursor	Grok 4.6	High`). Use those three fields as-is. Do not
  substitute `default` when the script returns a real Effort. If the
  script fails, use `unknown` for the missing field(s).
