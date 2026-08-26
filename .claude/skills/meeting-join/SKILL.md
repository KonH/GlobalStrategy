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
  Slavic first name (e.g. "Ivan", "Yaroslav", "Miroslava", "Bogdan", "Zlata",
  "Nikolai") — not your tool/provider name — that doesn't collide with any
  name already present in a `joined` line in this meeting's `log.md`.

You also pick a **title** before joining — see "Choosing a title" below.

## The log.md protocol

`log.md` is the single source of truth: transcript and machine coordination
state both live in it as plain lines. There is no separate state file.

### Line formats

**Join** (any participant, including the owner, writes its own once,
immediately on connecting — see "Connect, then prepare" below):
```
HH:MM Name (Provider, Model, Effort, Role, Title) joined
```
- `HH:MM` — 24-hour **local machine clock time**. Get it by actually running
  a shell command against the OS clock right before writing the line — never
  estimate, infer, or round it from conversation context: `date +%H:%M`
  (POSIX shell) or `Get-Date -Format HH:mm` (PowerShell). This applies to
  every timestamped line in this file (`joined`, `status:`, messages,
  `ack:`, `turn:`, `started`, `kicked:`, `ended:`), not just the join line.
- `Provider` — the tool, e.g. `Claude Code`, `Codex CLI`, `Cursor`.
- `Model` — your model id.
- `Effort` — your reasoning-effort/thinking setting.
- `Role` — exactly `owner` or `participant`.
- `Title` — your in-character role for this meeting, worked out from the
  agenda before you join — see "Choosing a title" below. No commas or
  parentheses in the text (it has to fit inside this tuple); a `|` is fine.
  Examples: `Senior Engineer | ECS Core`, `Product Manager`.

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

**Status** (any participant, including the owner, writes about itself, any
time after its own `joined` line):
```
HH:MM [meeting] status: Name Preparing...
HH:MM [meeting] status: Name Ready
```
Every participant goes through exactly this sequence once, right after
joining: `Preparing...` then `Ready` — see "Connect, then prepare" below.
There's no third state; don't invent others.

**Started** (owner only, written once, when every joined member has reached
`Ready` — see `meeting-start`):
```
HH:MM [meeting] started
```
This is the timer anchor for the meeting's end condition — not the owner's
own `joined` time.

**Message**:
```
HH:MM Name: message text
  optional continuation line, indented exactly two spaces
  another continuation line
```
Keep it to a few lines — this is a live conversation, not a report.

**Ack** (the granted participant only, immediately on seeing its own turn
grant, before composing the real message — see "Turn timeouts" below):
```
HH:MM [meeting] ack: Name
```

**Turn grant** (owner only):
```
HH:MM [meeting] turn: Name
```

**Kicked** (owner only, when a participant misses an ack or answer
timeout — see "Turn timeouts" below):
```
HH:MM [meeting] kicked: Name (no ack within 10s)
HH:MM [meeting] kicked: Name (no answer within 7m)
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

`log.html` (the live viewer) intentionally hides `[meeting] turn:` lines
from the rendered view — they're coordination plumbing, not conversation.
Every other `[meeting] ...` line (`status:`, `started`, `ack:`, `kicked:`,
`ended:`) **is** shown, as its own visible service message — readiness and
timeouts should be legible to whoever's watching the log, not just to the
agents. Your title isn't a `[meeting] ...` line at all — it's part of your
`joined` line, so it shows up there directly and is then tagged onto every
message and status line you write afterward, with your name itself
highlighted.

### Who may write when

- **Non-owner participants** may append a `Name: message` line **only** when
  the most recent `[meeting] turn:` line in the log names them. Never write
  a message otherwise.
- Any participant (owner or not) may append its own `joined` and `status:`
  lines at any time, without needing a turn grant — registering presence or
  readiness isn't "talking".
- A participant may append its own `ack:` line only right after its own
  `[meeting] turn:` grant appears — see "Turn timeouts" below.
- The **owner** is unrestricted: it may write messages, turn grants,
  `status:`/`started`/`kicked:`/`ended:` lines at any time (see
  `meeting-join`'s sibling, `meeting-start`).

### Turn timeouts — ack / soft / hard

Once the owner grants you the turn (`[meeting] turn: Name`), three clocks
matter, all counted from that grant:

1. **Ack — 10 seconds.** The instant you see your own turn grant, before
   composing your actual response, append the one-line `[meeting] ack: Name`
   service message via `append_entry`. This just proves you're alive and
   have seen the grant. The owner waits **10 seconds** for it; if it doesn't
   show up, the owner kicks you (`[meeting] kicked: Name (no ack within
   10s)`) and moves on without waiting further — poll tightly enough while
   waiting for your turn (see step 8 below) that you can actually make this
   window.
2. **Soft — 5 minutes.** After acking, you should aim to post your real
   `Name: message` reply within **5 minutes** of the turn grant. This is
   pacing guidance for you, not something the owner enforces at exactly 5
   minutes — but treat it as the point where you should be wrapping up, not
   still gathering thoughts.
3. **Hard — 7 minutes.** If your real message hasn't landed within **7
   minutes** of the turn grant, the owner kicks you (`[meeting] kicked: Name
   (no answer within 7m)`) and moves to the next speaker — the missed turn
   is not queued for later.

A kicked participant is done for the rest of the meeting: the owner will not
grant it another turn, even if it later posts a message anyway.

## Connect, then prepare

Preparation happens **after** joining, not before — the owner can't know
who's actually coming (and can't start the timer) until it sees everyone
transition through this sequence:

1. **Connect immediately.** Append your `joined` line — name and title
   already decided (see Args / "Choosing a title" below) — as soon as you
   resolve which meeting you're joining, before doing any prep work.
2. **Set status to `Preparing...`.** Append the status line right after
   joining.
3. **Do the actual preparation**, if `agenda.md` calls for it (see step 4 in
   "Steps" below for what that means). If the agenda asks for no particular
   prep, this step is effectively instant — still go through it, don't skip
   straight to `Ready`.
4. **Set status to `Ready`.** Append the status line once prep is done.

The meeting — including its timer — actually starts only once **every**
joined member (all participants plus the owner) has reached `Ready`. Until
then you may see other participants' `joined`/`status:` lines accumulate in
the log; that's expected, just keep waiting.

### Choosing a title

Your title is a visible, in-character role for this meeting — distinct from
the `Role` field (`owner`/`participant`) in your `joined` line, which is
just coordination plumbing. You decide it **before** joining, at the same
moment you pick your name (both need a quick look at `agenda.md`, already
read in step 1 below, and at the log's existing `joined` lines, to avoid
colliding with someone else — one read covers both checks):

1. **Judge the meeting's domain from `agenda.md`** — engineering/technical,
   design/product, narrative, production/planning, or something else. Don't
   force an engineering title onto a non-technical meeting.
2. **Pick a title scheme that fits that domain.** A couple of concrete
   examples (invent a similarly-shaped ladder for a domain not listed here —
   don't leave everyone with a bare, unspecialized title):
   - **Technical/engineering**: `Junior Engineer` → `Middle Engineer` →
     `Senior Engineer` → `Principal Engineer`/`Architect` (top tier — pick
     `Architect` for a broad, cross-cutting specialization, `Principal` for
     a single deep one).
   - **Design/product**: `Junior Designer` → `Designer` → `Senior Designer`
     → `Principal Designer`/`Design Lead`.
   - The meeting **owner** typically takes the domain's convening role
     instead of a seniority tier — `Product Manager` for a technical
     meeting, or whatever fits (`Producer`, `Facilitator`, ...) for another
     domain.
3. **Map your own `Effort`** to a seniority tier in that scheme, for
   individual-contributor participants only (skip this for the owner — see
   above): `low` → the junior tier, `medium` → the middle tier, `high` →
   the senior tier, anything above `high` (e.g. `max`/`xhigh`) → the top
   tier. If you'll write `default` for Effort because your tool doesn't
   expose one, use the middle tier.
4. **Pick a project specialization**, and keep the group diverse. Read the
   log so far for other members' `joined` lines and their `Title` field, and
   pick a specialization **none of them already claimed** — the goal is a
   believable, varied team, not five identical titles. For a GlobalStrategy
   technical meeting, reasonable specializations include ECS Core, Unity/
   rendering, UI Toolkit, map/province systems, localization, VContainer/DI,
   tooling & automation, and performance — pick whichever fits the agenda's
   themes; for another domain, pick specializations relevant to that
   domain's part of the project instead. If every obviously-relevant
   specialization is already taken, pick the next-best fit rather than
   duplicating one.
5. **Compose it**: `<Seniority or role> | <Specialization>` for an IC (e.g.
   `Senior Engineer | Unity`), or a bare role with no `|` when a seniority
   ladder doesn't apply (e.g. the owner's `Product Manager`). This is what
   goes in your `joined` line's `Title` field.

## Steps

1. **Resolve the meeting directory.** Latest, or the given fragment — see
   Args above. **Read `agenda.md` in full, carefully** — not just the
   `## Agenda` line. Pay particular attention to any `## Constraints`
   section: it may set rules that override this skill's defaults for this
   specific meeting (e.g. a stricter no-peeking rule, a required prep step,
   consensus expectations, who owns timekeeping). Treat those constraints as
   binding for the rest of your participation.
2. **Pick your name and title** (see Args and "Choosing a title" above,
   using the agenda you just read plus a look at the log's existing `joined`
   lines to avoid colliding with anyone) and append your `joined` line
   (role `participant`) via `scripts/meetings/append_entry.ps1`/`.sh` — see
   "The log.md protocol" above. Do this immediately, before any prep.
3. **Set status to `Preparing...`** (append the status line — see "Connect,
   then prepare" above).
4. **Prepare, if the agenda calls for it.** If `agenda.md` asks participants
   to prepare independently beforehand (own notes, context, review of
   relevant history) do that preparation now, using only your own
   knowledge/history/work — never another participant's notes, draft input,
   or `log.md` entries, even if they already exist from an earlier prep
   pass. Keep your prep notes to yourself; they inform what you say later,
   they are not something to paste in verbatim or share on request from
   another participant. Do not skip this step or treat it as optional
   busywork — the quality of your turns depends on having actually done it.
5. **Set status to `Ready`** (append the status line).
6. **Read the full log** so far for context before doing anything else.
7. **Wait for the meeting to start.** Nothing to do here but hold — the
   owner is waiting for every joined member to reach `Ready` (see
   `meeting-start`). You'll see `[meeting] started` appear once it does.
8. **Wait for your turn.** Run, repeatedly until it reports a match:

   ```
   scripts/meetings/wait_for_turn.ps1 --log "Docs/Meetings/<dir>/log.md" --pattern "\[meeting\] (turn: <YourName>$|ended:)" --timeout 300 --poll 3
   ```
   (POSIX shells: `scripts/meetings/wait_for_turn.sh --log ... --pattern ... --timeout 300 --poll 3`, same flags)

   Poll at `3` seconds, not more — the 10-second ack timeout (see "Turn
   timeouts" above) leaves little room for a coarser poll to still let you
   react in time.

   - Exit code `0`: the printed line matched. If it's `[meeting] ended:`,
     stop — go to step 10. If it's your turn grant, go to step 9.
   - Exit code `2`: no match in this chunk — just re-run the same command
     again (this is normal; a meeting can run far longer than one chunk).
9. **Ack, then respond.** The moment you see your own turn grant, append
   `[meeting] ack: Name` immediately — before composing anything else. Then
   re-read the log since you last read it (other turns may have happened),
   compose a short, substantive reply grounded in the agenda, your own prep
   notes from step 4, any constraints from `agenda.md` (e.g. working toward
   consensus), and what's actually been said, then append it as a `Name:
   message` line (with continuation lines if needed) via `append_entry`,
   aiming to land it within the 5-minute soft timeout. Go back to step 8.
10. **Exit.** Once `ended:` appears, stop waiting. Optionally read
    `summary.md` if the owner has written one by then.
