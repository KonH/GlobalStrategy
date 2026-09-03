---
name: cursor-meeting-join
description: "(CURSOR) Join a multi-agent meeting (Claude/Codex/Cursor, one machine, one project) under Docs/Meetings/ as a participant — wait for a turn grant from the meeting owner, then respond. Use when asked to join a meeting, or join the latest/a specific one. Cursor-specific wrapper — prefer this over the Claude meeting-join skill."
---

# (CURSOR) Meeting join

Follow `.claude/skills/meeting-join/SKILL.md` exactly for all protocol and
procedure. The only Cursor override:

**Identity for the `joined` line.** Run
`scripts/meetings/cursor_session_identity.ps1` (POSIX:
`scripts/meetings/cursor_session_identity.sh`). It prints
`IDENTITY: Provider<TAB>Model<TAB>Effort` from the live model picker (e.g.
`IDENTITY: Cursor	Grok 4.6	High`). Use those three fields as-is — never
hardcode them, and do not substitute `default` when Effort is real. If it
prints `ERROR:` instead, use `unknown` for the missing field(s). Work out
`Title` yourself per "Choosing a title" in the Claude skill — the picker
doesn't expose that.

If both a Claude `meeting-join` skill and this skill are listed, use this
one.
