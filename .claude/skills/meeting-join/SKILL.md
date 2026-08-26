---
name: meeting-join
description: Join a multi-agent meeting (Claude/Codex/Cursor, one machine, one project) under Docs/Meetings/ as a participant — wait for a turn grant from the meeting owner, then respond. Use when asked to join a meeting, or join the latest/a specific one.
---

# Meeting join (participant)

Joins an existing `Docs/Meetings/<dir>/` meeting as a **participant**. The
meeting owner (running `meeting-start`) decides who talks when — this skill
never writes a message without being granted the turn.

This document also defines the shared log.md protocol used by `meeting-start`
and by the other tools' meeting skills (`.codex/skills/meeting-join/`,
`.cursor/skills/cursor-meeting-join/`); read it fully even when only the owner
role is needed, since `meeting-start` builds on it rather than repeating it.

## Args

`/meeting-join [meeting] [name]`

- **meeting** (optional): a `Docs/Meetings/` directory name, or a fragment of
  one (matched with `Glob Docs/Meetings/*<meeting>*/*.md`). Omit to join the
  latest meeting — directory names sort chronologically because they start
  with `YY_MM_DD_HH`, so the latest is the lexicographically greatest.
- **name** (optional): the display name to join under. If omitted, pick a
  human first name (e.g. "Alex", "Sam", "Jordan") — not your tool/provider
  name — that doesn't collide with any name already present in a `joined`
  line in this meeting's `log.md`.

## The log.md protocol

`log.md` is the single source of truth: transcript and machine coordination
state both live in it as plain lines. There is no separate state file.

### Line formats

**Join** (any participant, including the owner, writes its own once):
```
HH:MM Name (Provider, Model, Effort, Role) joined
```
- `HH:MM` — 24-hour **local machine clock time**. Get it by actually running
  a shell command against the OS clock right before writing the line — never
  estimate, infer, or round it from conversation context: `date +%H:%M`
  (POSIX shell) or `Get-Date -Format HH:mm` (PowerShell). This applies to
  every timestamped line in this file (`joined`, messages, `turn:`,
  `ended:`), not just the join line.
- `Provider` — the tool, e.g. `Claude Code`, `Codex CLI`, `Cursor`.
- `Model` — your model id.
- `Effort` — your reasoning-effort/thinking setting.
- `Role` — exactly `owner` or `participant`.

`Model` and `Effort` must come from an actual check, not a guess or a
plausible-looking default — the same rule as the timestamp above. Check via
whatever your tool actually exposes before writing the line:
  - **Claude Code**: read the `CLAUDE_EFFORT` environment variable
    (`$env:CLAUDE_EFFORT` in PowerShell, `$CLAUDE_EFFORT` in POSIX shells)
    for Effort; Model is normally stated directly in your own system
    prompt/environment info (e.g. "You are powered by the model named...").
    `/status` is a REPL-only slash command, not something an agent can
    invoke as a tool call — don't rely on it.
  - **Codex CLI**: read `model` / `model_reasoning_effort` from
    `~/.codex/config.toml`.
  - **Cursor**: read the active model from the session/UI state.

Only write `unknown` (Model) or `default` (Effort) if you actually checked
and the tool genuinely doesn't expose it — never as a shortcut to skip
checking.

**Message**:
```
HH:MM Name: message text
  optional continuation line, indented exactly two spaces
  another continuation line
```
Keep it to a few lines — this is a live conversation, not a report.

**Turn grant** (owner only):
```
HH:MM [meeting] turn: Name
```

**Ended** (owner only):
```
HH:MM [meeting] ended: time_limit
HH:MM [meeting] ended: owner_decision
```

Entries are separated by exactly one blank line. **Always append via the
shared script, never via an Edit/Write-style file tool:**

```
scripts/meetings/append_entry.ps1 --log "Docs/Meetings/<dir>/log.md" --text "HH:MM Name: message text"
```
(POSIX: `scripts/meetings/append_entry.sh --log ... --text ...`)

It opens the file in append mode and adds the blank-line separator itself —
a true filesystem append, not a read-then-rewrite of the whole file. This
matters because multiple agent processes append to the same `log.md`
concurrently (e.g. several participants joining around the same time): an
Edit/Write-style tool reads the whole file and writes the whole file back,
so two overlapping read-modify-write cycles can silently drop one agent's
entry entirely. A pure append can only ever add bytes at the end, so
concurrent writers can interleave in a different order than their own
timestamps (harmless — see "Who may write when" below for why that's never
a problem for messages specifically) but can never clobber each other.

`log.html` (the live viewer) intentionally hides `[meeting] turn:` lines from
the rendered view — they're coordination plumbing, not conversation — but
they always stay in `log.md` itself since the wait script and the owner
depend on them.

### Who may write when

- **Non-owner participants** may append a `Name: message` line **only** when
  the most recent `[meeting] turn:` line in the log names them. Never write
  a message otherwise.
- Any participant (owner or not) may append its own `joined` line at any
  time, without needing a turn grant — registering presence isn't "talking".
- The **owner** is unrestricted: it may write messages, turn grants, and the
  `ended` line at any time (see `meeting-join`'s sibling, `meeting-start`).

### Turn timeout — 10 minutes

The owner allows at most **10 minutes** per granted turn. If a participant
doesn't respond in that window, the owner logs a timeout and moves to the
next speaker without waiting further — the missed turn is not queued for
later. Reply well within 10 minutes of seeing your turn grant.

## Steps

1. **Resolve the meeting directory.** Latest, or the given fragment — see
   Args above. **Read `agenda.md` in full, carefully** — not just the
   `## Agenda` line. Pay particular attention to any `## Constraints`
   section: it may set rules that override this skill's defaults for this
   specific meeting (e.g. a stricter no-peeking rule, a required prep step,
   consensus expectations, who owns timekeeping). Treat those constraints as
   binding for the rest of your participation.
2. **Prepare before joining, if the agenda calls for it.** If `agenda.md`
   asks participants to prepare independently beforehand (own notes,
   context, review of relevant history) do that preparation now, using only
   your own knowledge/history/work — never another participant's notes,
   draft input, or `log.md` entries, even if they already exist from an
   earlier prep pass. Keep your prep notes to yourself; they inform what you
   say later, they are not something to paste in verbatim or share on
   request from another participant. Do not skip this step or treat it as
   optional busywork — the quality of your turns depends on having actually
   done it.
3. **Pick your name** (see Args) and append your `joined` line (role
   `participant`) via `scripts/meetings/append_entry.ps1`/`.sh` — see "The
   log.md protocol" above.
4. **Read the full log** so far for context before doing anything else.
5. **Wait for your turn.** Run, repeatedly until it reports a match:

   ```
   scripts/meetings/wait_for_turn.ps1 --log "Docs/Meetings/<dir>/log.md" --pattern "\[meeting\] (turn: <YourName>$|ended:)" --timeout 300 --poll 5
   ```
   (POSIX shells: `scripts/meetings/wait_for_turn.sh --log ... --pattern ... --timeout 300 --poll 5`, same flags)

   - Exit code `0`: the printed line matched. If it's `[meeting] ended:`,
     stop — go to step 7. If it's your turn grant, go to step 6.
   - Exit code `2`: no match in this chunk — just re-run the same command
     again (this is normal; a meeting can run far longer than one chunk).
6. **Respond.** Re-read the log since you last read it (other turns may have
   happened), compose a short, substantive reply grounded in the agenda,
   your own prep notes from step 2, any constraints from `agenda.md` (e.g.
   working toward consensus), and what's actually been said, then append it
   as a `Name: message` line (with continuation lines if needed) via
   `append_entry`. Go back to step 5.
7. **Exit.** Once `ended:` appears, stop waiting. Optionally read
   `summary.md` if the owner has written one by then.
