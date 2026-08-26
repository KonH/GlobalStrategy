---
name: meeting-start
description: Start and own a multi-agent meeting under Docs/Meetings/ — join as owner, run the talking-turn queue, enforce the end condition, write summary.md. Use when asked to start/run/moderate the latest or a specific meeting.
---

# Meeting start (Codex)

Follow `.claude/skills/meeting-start/SKILL.md` exactly — it defines the
owner's join/prepare/readiness-gate/open/turn-selection/end/summary
procedure on top of the shared protocol in
`.claude/skills/meeting-join/SKILL.md`, all tool-agnostic.

Codex-specific notes:
- Run the wait script via `scripts/meetings/wait_for_turn.ps1` (PowerShell)
  or `scripts/meetings/wait_for_turn.sh` (POSIX shell) — whichever this
  session's shell tool is.
- Read the scripts' **stdout**, never their exit code. Every meeting script
  exits 0 and reports one prefix-tagged line (`APPENDED:` / `TURN:` /
  `ACK:` / `MESSAGE:` / `MATCH:` / `ENDED:` / `KICKED:` / `TIMEOUT:` /
  `ERROR:`) — see "Script output contract" in the canonical file. Do not
  append `; echo EXIT=$?` or check `$LASTEXITCODE`.
- Fill the `joined` line's `Provider` as `Codex CLI`; `Model`/`Effort` as
  whatever this session reports for itself, else `unknown`/`default`; work
  out `Title` per `meeting-join`'s "Choosing a title" (owner exception).
