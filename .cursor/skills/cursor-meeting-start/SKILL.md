---
name: cursor-meeting-start
description: "(CURSOR) Start and own a multi-agent meeting under Docs/Meetings/ — join as owner, run the talking-turn queue, enforce the end condition, write summary.md. Use when asked to start/run/moderate the latest or a specific meeting. Cursor-specific wrapper — prefer this over the Claude meeting-start skill."
---

# (CURSOR) Meeting start

Follow `.claude/skills/meeting-start/SKILL.md` exactly — it defines the
owner's join/open/turn-selection/end/summary procedure on top of the shared
protocol in `.claude/skills/meeting-join/SKILL.md`, all tool-agnostic.

If both a Claude `meeting-start` skill and this skill are listed, use this
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
