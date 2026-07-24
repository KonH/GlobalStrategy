---
name: localization
description: Add new locale keys with real Russian translations instead of English placeholders — spawns a lightweight Haiku subagent to translate new en.asset entries into proper ru.asset entries. Load whenever adding, renaming, or changing user-facing text keys in Assets/Localization/en.asset and ru.asset.
---

# localization

## The old pattern (deprecated)

Older specs (e.g. `Docs/Specs/26_06_20_00_country-action-cards/plan.md`) added new
keys to `ru.asset` with the same English text as a "placeholder Russian
translation," deferring the real translation to later. **Do not do this
anymore.** Every new key gets a real Russian translation in the same change
that adds it.

## Workflow

1. Decide the new key(s) and their English text; add them to
   `Assets/Localization/en.asset` as `Key`/`Value` pairs under `Entries` (see
   `.claude/rules/unity/localization.md` for the namespacing convention —
   `menu.*`, `hud.*`, `country_name.*`, etc.).
2. Collect the full list of new `Key: Value` pairs (English) just added.
3. Spawn a translation subagent — `Agent` tool, `model: "haiku"` — with a
   self-contained prompt that:
   - Includes every new key and its English value.
   - Asks for natural, game-appropriate Russian translations (not
     literal/machine-style), preserving any `string.Format` placeholders
     (`{0}`, `{1}`, …) and any markup/formatting tokens unchanged, positioned
     wherever they'd naturally fall in Russian phrasing.
   - Asks it to return only the `Key: Value` pairs (Russian value) in the
     same order, no commentary, so the output can be pasted straight into
     `ru.asset`.
4. Add the returned Russian values to `Assets/Localization/ru.asset` under
   the matching keys, keeping the same key order as `en.asset` where
   practical (not load-bearing, just keeps the two files diffable).
5. Refresh Unity and check the console for errors from the `LocaleConfig`
   assets.

## Why Haiku

Translating short UI strings is a low-reasoning, high-volume task — a
full-capability model is overkill. Route it to a `model: "haiku"` subagent
to keep cost and latency down; the parent conversation stays on the main
model and should not delegate anything else through this call.

## Batching

Batch all new keys from a single feature into one subagent call rather than
one call per key — the subagent has no memory between calls, so a single
prompt with the full list is both cheaper and gives the translator
surrounding context (sibling strings in the same feature) for consistent
tone and terminology.

## Existing placeholder debt

Don't proactively hunt down and fix old keys where `ru.asset` still equals
`en.asset` — that's separate cleanup work, not something this skill
triggers automatically. This skill only applies going forward, whenever a
spec/plan step or ad-hoc change adds new locale keys.
