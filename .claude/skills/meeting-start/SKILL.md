---
name: meeting-start
description: Start and own a multi-agent meeting under Docs/Meetings/ — join as owner, run the talking-turn queue, enforce the end condition, and write summary.md. Use when asked to start/run/moderate the latest or a specific meeting.
---

# Meeting start (owner)

Starts and moderates a `Docs/Meetings/<dir>/` meeting. Read
[`meeting-join`](../meeting-join/SKILL.md) first — it defines the shared
`log.md` protocol (line formats, the who-may-write-when rules, the
`status:`/`started` readiness concept, and the ack/soft/hard turn timeouts)
that this skill builds on rather than repeating. This skill covers the
**owner-only** additions: joining as owner, gating the meeting's start on
everyone's readiness, deciding who talks next, enforcing turn timeouts, and
ending the meeting.

## Args

`/meeting-start [meeting] [name] [duration minutes]`

- **meeting** (optional): same resolution as `meeting-join` — a directory
  name/fragment under `Docs/Meetings/`, or the latest if omitted.
- **name** (optional): your display name — a Slavic first name by default,
  same rule as `meeting-join`.
- **duration minutes** (optional): end condition, default `60`.

You also pick a title before joining, same as any participant — see
`meeting-join`'s "Choosing a title", with the owner-specific note under
step 2 below.

## End condition

Two ways a meeting ends, either written as `[meeting] ended: <reason>`:

- **`time_limit`** — default 60 minutes after the `[meeting] started`
  timestamp (or the given duration) — **not** your own `joined` timestamp,
  since joining now happens before prep and readiness-waiting, which can
  take a while. Check elapsed time (since `started`) before granting each
  new turn; once past the limit, close it out instead of granting another
  turn.
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
2. **Pick your name and title, then join immediately, as owner**: work out
   your title the same way `meeting-join`'s "Choosing a title" describes,
   with its owner exception — take the domain's convening role instead of a
   seniority tier (`Product Manager` for a technical meeting, or whatever
   fits the agenda's actual domain: `Producer`, `Facilitator`, `Design
   Lead`, ...). Then append your `joined` line (role `owner`, title
   included) via `scripts/meetings/append_entry.ps1`/`.sh` (see
   `meeting-join`'s log.md protocol — always append via that script, never
   Edit/Write) — before any prep. You go through the same connect-then-
   prepare sequence as every other member; you don't get to skip it.
3. **Set your own status to `Preparing...`** (append the status line).
4. **Prepare, same as any participant.** If `agenda.md` asks participants to
   prepare independently beforehand, do that prep now from your own
   knowledge/history/work only — never another participant's notes or draft
   input. As owner you still hold your own substantive view on the agenda;
   don't rely solely on participants to surface content.
5. **Set your own status to `Ready`** (append the status line).
6. **Wait until every joined member is `Ready`, then write `started`.** This
   is the readiness gate — the meeting's timer does not exist until you
   write `[meeting] started`. Loop:

   a. Read the full log. Compute the current roster: every name with a
      `joined` line that hasn't since been `kicked:`. For each, find its
      most recent `status:` line — `Preparing...` or `Ready`.
   b. If any roster member (including yourself) is not yet `Ready`, wait for
      the next relevant line and re-check:
      ```
      scripts/meetings/wait_for_turn.ps1 --log "Docs/Meetings/<dir>/log.md" --pattern "\[meeting\] (status:|joined)" --timeout 60 --poll 5
      ```
      (POSIX: `scripts/meetings/wait_for_turn.sh --log ... --pattern ... --timeout 60 --poll 5`)
      `MATCH:` or `TIMEOUT:`, it doesn't matter which — either way, go back
      to (a), which re-reads the whole log anyway.
   c. Once everyone currently on the roster is `Ready`, do one more short
      wait for stragglers before committing — a participant could still be
      mid-join:
      ```
      scripts/meetings/wait_for_turn.ps1 --log "Docs/Meetings/<dir>/log.md" --pattern "\[meeting\] (status:|joined)" --timeout 30 --poll 5
      ```
      - `MATCH:` (a new `joined`/`status:` line landed): go back to (a) —
        the roster may have changed.
      - `TIMEOUT:` (nothing new in 30s): the roster is stable and everyone
        on it is `Ready`. Proceed to (d).

      `--pattern` is the one mode that only sees lines appended *after* it
      starts. That is fine here — this is a "has anything changed" probe and
      (a) re-reads the log regardless. Never use `--pattern` to wait for an
      ack or a reply; step 9 uses the state-derived modes for that reason.
   d. **Solo-owner fallback**: if you've been alone (no other participant
      has ever joined) for several minutes of waiting, don't hang forever —
      treat it as a solo/dry-run session and proceed once your own status is
      `Ready`.
   e. Append `[meeting] started`. This timestamp is the anchor for the
      end-condition duration — record it.
7. **Open the meeting**: append the opening `Name: message` restating the
   agenda/definition-of-done from `agenda.md`, calling out the checklist's
   theme order and time budget if the agenda defines one, and inviting
   discussion. Same message format as any participant: plain prose, not
   markdown or a numbered brief (see `meeting-join`). As owner you can
   write messages at any time without a turn grant.
8. **Grant the first turn**: pick a participant (see "Deciding who talks
   next") and append `[meeting] turn: Name`.
9. **Wait for that agent's ack, then its response**:

   a. Wait up to the 60-second ack timeout:
      ```
      scripts/meetings/wait_for_turn.ps1 --log "Docs/Meetings/<dir>/log.md" --await-ack <Name> --timeout 60 --poll 2
      ```
      (POSIX: `scripts/meetings/wait_for_turn.sh --log ... --await-ack ... --timeout 60 --poll 2`)
      - `ACK:` — acked, continue to (b).
      - `TIMEOUT:` (no ack in 60s): append
        `HH:MM [meeting] kicked: Name (no ack within 60s)` and move on —
        go to step 12 without waiting further for this grant.

      Use `--await-ack`, **never** `--pattern "\[meeting\] ack: <Name>$"`.
      `--await-ack` anchors on the last `[meeting] turn: <Name>` line and
      asks whether an ack follows it anywhere in the file, so it sees an ack
      no matter when it was written. `--pattern` only ever matches lines
      appended after the waiter starts — so a participant that acked
      *quickly*, in the seconds between your turn grant and your next tool
      call, was invisible to it and got kicked for being fast. That is the
      false-kick bug that gutted the roster in the 01_world-domination retro
      (four participants acked, spoke, and were kicked for "no ack" anyway).

      Do not shorten the 60s either. A participant acks from inside its own
      wait script, so the ack itself is now instant, but a participant
      sitting in the gap between two `--await-turn` calls still needs a
      fresh call to start before it can see the grant.
   b. Wait for the real message, up to the 7-minute hard timeout measured
      from the turn grant (360s covers the remaining budget after the 60s
      ack wait):
      ```
      scripts/meetings/wait_for_turn.ps1 --log "Docs/Meetings/<dir>/log.md" --await-message <Name> --timeout 360 --poll 5
      ```
      (POSIX: `scripts/meetings/wait_for_turn.sh --log ... --await-message ... --timeout 360 --poll 5`)
      - `MESSAGE:` — they responded; read it (and anything else new in the
        log).
      - `TIMEOUT:` — **hard timeout** — append
        `HH:MM [meeting] kicked: Name (no answer within 7m)` and move on.
        A kicked participant gets no further turns for the rest of this
        meeting, even if it posts something later anyway.

      Same reason as (a): `--await-message` anchors on the turn grant and
      counts any message after it, so a reply that landed early can't be
      missed.

   **Before writing any `kicked:` line, re-run the matching `--await-*`
   command once with `--timeout 5`.** It is cheap, and it is the difference
   between recording a real timeout and libelling a participant who
   answered while you were composing the kick.
10. **Check the current theme's time budget** (if `agenda.md`'s checklist
    gives one) against elapsed time since that theme started (from
    `[meeting] started`, not your own join time). If the theme is over
    budget and has reached a reasonable stopping point (consensus, or a
    clearly noted disagreement), post a short message moving the group to
    the next theme before granting the next turn — don't let one theme
    silently consume time budgeted for later ones. Note the theme transition
    in your turn-grant message or the message immediately before it, so
    participants know what's being discussed next.
11. **Check the overall end condition** (elapsed time since `[meeting]
    started` against the 60-minute cap, or your own judgment that the
    definition-of-done is met). If it's time to end, go to step 13.
12. **Decide who talks next** (see below — this also applies right after a
    kick), append the next `turn:` grant, go back to step 9.
13. **Close the meeting**: append a brief closing message if you haven't
    already, then append `[meeting] ended: time_limit` or
    `[meeting] ended: owner_decision`.
14. **Write `summary.md`**: a free-form summary of `log.md` — what was
    discussed, decisions (or explicitly unresolved disagreements) reached
    per theme, open questions, how the definition-of-done from `agenda.md`
    was or wasn't met, and any participants who were kicked (and why). Note
    if any theme was cut short by the time budget. Also list the final
    roster as `Name — Title` so the write-up records who played what role.

## Deciding who talks next

Not written to the log — this happens in your own reasoning each round:

1. **Never grant a turn to a kicked participant.** Once `[meeting] kicked:
   Name (...)` has been written for someone, they're excluded from turn
   selection for the rest of the meeting, permanently.
2. **Direct address wins.** If the latest message clearly asks a specific
   other (non-kicked) participant a question or calls them out by name (e.g.
   "Yaroslav, why did you...?"), grant them the next turn.
3. **Otherwise, least talked on the current theme.** Count `Name:` message
   lines per non-kicked participant since the current theme started (not the
   whole meeting) and grant the turn to whoever has spoken least on it — new
   joiners who haven't spoken yet outrank everyone. This keeps one theme from
   being dominated by whoever happened to speak most on an earlier one.
4. If only one non-owner, non-kicked participant remains, it's always their
   turn after yours.
5. **Steer toward consensus, not just coverage.** If `agenda.md` asks for
   consensus per theme, don't move to the next theme purely because the time
   budget expired if participants are one exchange away from agreeing —
   but don't let a theme run indefinitely chasing full agreement either; a
   clearly stated disagreement is an acceptable close for a theme too.
