---
name: meeting-start
description: Start and own a multi-agent meeting under Docs/Meetings/ — join as owner, run the talking-turn queue, enforce the end condition, and write summary.md. Use when asked to start/run/moderate the latest or a specific meeting.
---

# Meeting start (owner)

Starts and moderates a `Docs/Meetings/<dir>/` meeting. Read
[`meeting-join`](../meeting-join/SKILL.md) first — it defines the shared
`log.md` protocol (line formats, the who-may-write-when rules, and the
10-minute turn timeout) that this skill builds on rather than repeating.
This skill covers the **owner-only** additions: joining as owner, deciding
who talks next, and ending the meeting.

## Args

`/meeting-start [meeting] [name] [duration minutes]`

- **meeting** (optional): same resolution as `meeting-join` — a directory
  name/fragment under `Docs/Meetings/`, or the latest if omitted.
- **name** (optional): your display name — a human first name by default,
  same rule as `meeting-join`.
- **duration minutes** (optional): end condition, default `60`.

## End condition

Two ways a meeting ends, either written as `[meeting] ended: <reason>`:

- **`time_limit`** — default 60 minutes after your own `joined` timestamp
  (or the given duration). Check elapsed time before granting each new turn;
  once past the limit, close it out instead of granting another turn.
- **`owner_decision`** — you decide to end early, e.g. because the agenda's
  definition-of-done is satisfied. Write a closing message stating why, then
  the `ended` line.

## Steps

1. **Resolve the meeting directory** and read `agenda.md`.
2. **Join as owner**: append your `joined` line (role `owner`) via
   `scripts/meetings/append_entry.ps1`/`.sh` (see `meeting-join`'s log.md
   protocol — always append via that script, never Edit/Write). Record your
   join time — it's the deadline anchor.
3. **Open the meeting**: append the opening `Name: message` restating the
   agenda/definition-of-done from `agenda.md` and inviting discussion. As
   owner you can write messages at any time without a turn grant.
4. **Grant the first turn**: pick a participant (see "Deciding who talks
   next") and append `[meeting] turn: Name`.
5. **Wait for that agent's response**:

   ```
   scripts/meetings/wait_for_turn.ps1 --log "Docs/Meetings/<dir>/log.md" --pattern "^\d{2}:\d{2} <Name>:" --timeout 590 --poll 5
   ```
   (POSIX: `scripts/meetings/wait_for_turn.sh --log ... --pattern ... --timeout 590 --poll 5`)

   - Exit `0`: they responded — read it (and anything else new in the log).
   - Exit `2`: **timed out** (590s is the largest chunk that stays safely
     under the shell tool's own 600s command-timeout cap, so in practice
     this is the 10-minute turn timeout) — append
     `HH:MM [meeting] Name timed out, skipping` and move on. Don't wait
     again for the same grant.
6. **Check the end condition** (elapsed time, or your own judgment that the
   definition-of-done is met). If it's time to end, go to step 8.
7. **Decide who talks next** (see below), append the next `turn:` grant, go
   back to step 5.
8. **Close the meeting**: append a brief closing message if you haven't
   already, then append `[meeting] ended: time_limit` or
   `[meeting] ended: owner_decision`.
9. **Write `summary.md`**: a free-form summary of `log.md` — what was
   discussed, decisions made, open questions, and how the definition-of-done
   from `agenda.md` was or wasn't met.

## Deciding who talks next

Not written to the log — this happens in your own reasoning each round:

1. **Direct address wins.** If the latest message clearly asks a specific
   other participant a question or calls them out by name (e.g. "Alex, why
   did you...?"), grant them the next turn.
2. **Otherwise, least talked.** Count `Name:` message lines per participant
   so far and grant the turn to whoever has spoken the fewest times (new
   joiners who haven't spoken yet outrank everyone).
3. If only one non-owner participant has joined, it's always their turn
   after yours.
