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

1. **Resolve the meeting directory** and **read `agenda.md` in full,
   carefully** — the theme, the definition-of-done, and especially any
   `## Constraints` section (consensus expectations, timekeeping duty,
   preparation/no-peeking rules, per-theme time budgets). These constraints
   govern how you run the meeting, not just what participants do — e.g. if
   the agenda assigns you timekeeping, you are expected to actually track
   elapsed time against the checklist's per-theme budget (see "Deciding who
   talks next" and the end condition below), not just the overall 60-minute
   cap.
2. **Prepare before opening, same as any participant.** If `agenda.md` asks
   participants to prepare independently beforehand, do that prep now from
   your own knowledge/history/work only — never another participant's notes
   or draft input. As owner you still hold your own substantive view on the
   agenda; don't rely solely on participants to surface content.
3. **Join as owner**: append your `joined` line (role `owner`) via
   `scripts/meetings/append_entry.ps1`/`.sh` (see `meeting-join`'s log.md
   protocol — always append via that script, never Edit/Write). Record your
   join time — it's the deadline anchor.
4. **Open the meeting**: append the opening `Name: message` restating the
   agenda/definition-of-done from `agenda.md`, calling out the checklist's
   theme order and time budget if the agenda defines one, and inviting
   discussion. As owner you can write messages at any time without a turn
   grant.
5. **Grant the first turn**: pick a participant (see "Deciding who talks
   next") and append `[meeting] turn: Name`.
6. **Wait for that agent's response**:

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
7. **Check the current theme's time budget** (if `agenda.md`'s checklist
   gives one) against elapsed time since that theme started. If the theme is
   over budget and has reached a reasonable stopping point (consensus, or a
   clearly noted disagreement), post a short message moving the group to the
   next theme before granting the next turn — don't let one theme silently
   consume time budgeted for later ones. Note the theme transition in your
   turn-grant message or the message immediately before it, so participants
   know what's being discussed next.
8. **Check the overall end condition** (elapsed time against the 60-minute
   cap, or your own judgment that the definition-of-done is met). If it's
   time to end, go to step 10.
9. **Decide who talks next** (see below), append the next `turn:` grant, go
   back to step 6.
10. **Close the meeting**: append a brief closing message if you haven't
    already, then append `[meeting] ended: time_limit` or
    `[meeting] ended: owner_decision`.
11. **Write `summary.md`**: a free-form summary of `log.md` — what was
    discussed, decisions (or explicitly unresolved disagreements) reached per
    theme, open questions, and how the definition-of-done from `agenda.md`
    was or wasn't met. Note if any theme was cut short by the time budget.

## Deciding who talks next

Not written to the log — this happens in your own reasoning each round:

1. **Direct address wins.** If the latest message clearly asks a specific
   other participant a question or calls them out by name (e.g. "Alex, why
   did you...?"), grant them the next turn.
2. **Otherwise, least talked on the current theme.** Count `Name:` message
   lines per participant since the current theme started (not the whole
   meeting) and grant the turn to whoever has spoken least on it — new
   joiners who haven't spoken yet outrank everyone. This keeps one theme from
   being dominated by whoever happened to speak most on an earlier one.
3. If only one non-owner participant has joined, it's always their turn
   after yours.
4. **Steer toward consensus, not just coverage.** If `agenda.md` asks for
   consensus per theme, don't move to the next theme purely because the time
   budget expired if participants are one exchange away from agreeing —
   but don't let a theme run indefinitely chasing full agreement either; a
   clearly stated disagreement is an acceptable close for a theme too.
