---
name: meeting-schedule
description: Create a new multi-agent meeting under Docs/Meetings/ — agenda.md, empty log.md, and a live-viewer log.html — from a short agenda description. Use when asked to schedule/set up a meeting for Claude/Codex/Cursor agents.
---

# Meeting schedule

Creates the meeting directory and its artifacts. Does not join or start the
meeting — that's `meeting-join` / `meeting-start`.

## Args

`/meeting-schedule <short agenda description> [slug]`

- **short agenda description** (required): a sentence or two describing the
  meeting's theme.
- **slug** (optional): kebab-case directory suffix. If omitted, derive a
  short (3-5 word) kebab-case slug from the description.

## Steps

1. Compute the timestamp prefix `YY_MM_DD_HH` from the current date/hour
   (same convention as `Docs/Specs/<YY_MM_DD_HH>_<name>`).
2. Create `Docs/Meetings/<YY_MM_DD_HH>_<slug>/`.
3. Write `agenda.md`, free-form but at minimum covering:

   ```markdown
   # <Meeting theme, restated briefly>

   ## Agenda
   <the short agenda description as given>

   ## Definition of done
   - [ ] <inferred from the description — what "done" looks like>

   ## Checklist
   - [ ] <inferred discussion/action points>
   ```

   Flesh out the definition-of-done and checklist reasonably from the
   description rather than leaving placeholders — the owner and participants
   read this to know what the meeting is actually for.

4. Create an empty `log.md` (zero bytes).
5. Copy `scripts/meetings/log_template.html` verbatim to `log.html`. It's a
   generic viewer — nothing in it is meeting-specific; it prompts the viewer
   to pick the `log.md` file to watch when opened.
6. Report the created path, and that whoever will run `meeting-start` on it
   becomes the owner (first-come — there's no separate reservation step).
