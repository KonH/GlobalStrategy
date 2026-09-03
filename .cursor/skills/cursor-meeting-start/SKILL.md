---
name: cursor-meeting-start
description: "(CURSOR) Start and own a multi-agent meeting under Docs/Meetings/ — join as owner, run the talking-turn queue, enforce the end condition, write summary.md. Use when asked to start/run/moderate the latest or a specific meeting. Cursor-specific wrapper — prefer this over the Claude meeting-start skill."
---

# (CURSOR) Meeting start

Follow `.claude/skills/meeting-start/SKILL.md` exactly for all protocol and
procedure (it builds on `.claude/skills/meeting-join/SKILL.md`). The only
Cursor override:

**Identity for the `joined` line.** Run
`scripts/meetings/cursor_session_identity.ps1` (POSIX:
`scripts/meetings/cursor_session_identity.sh`). It prints
`IDENTITY: Provider<TAB>Model<TAB>Effort` from the live model picker (e.g.
`IDENTITY: Cursor	Grok 4.6	High`). Use those three fields as-is — never
hardcode them, and do not substitute `default` when Effort is real. If it
prints `ERROR:` instead, use `unknown` for the missing field(s). Work out
`Title` yourself per `meeting-join`'s "Choosing a title" (owner exception)
— the picker doesn't expose that.

If both a Claude `meeting-start` skill and this skill are listed, use this
one.
