Define a new Action Log line type for `Docs/Specs/26_07_18_07_action-log-ui/` (or its successor spec) to implement later.

## Arguments

`$ARGUMENTS` may be free-form. Examples:
- `Province ownership changes` — propose a log line for a province changing hands
- `Country score milestones` — propose a log line for a country crossing a score threshold

If `$ARGUMENTS` is empty, ask the user what game-logic event should get a log line before proceeding.

## Steps

1. **Identify the exact point in game logic where this event happens** — the system/method that applies the underlying effect (e.g. a `CreateActionEffectSystem` branch, a `GameLogic` command handler). This feature's established convention (see `Docs/Specs/26_07_18_07_action-log-ui/plan.md`) is a **one-shot, non-`[Savable]` ECS event component created at that exact site** (mirroring `ControlEffectApplied`/`OpinionEffectApplied`/`DiscoveryApplied`/`RoleChangeApplied`), cleaned up the following tick by `CleanupActionEffectsSystem`, and read by `VisualStateConverter.UpdateGameLog` the same tick it's created. Prefer this over diffing observed state over time. If no clear application site exists yet, say so explicitly; this skill does not design new game-logic systems.

2. **Define the trigger condition** — exactly when the new event component should be created (e.g. "only when an effect was actually applied, not attempted"), matching the "appearance of a fresh event, not inferred from a value comparison" convention already established.

3. **Write the line format** — the exact `string.Format` template with `{n}` placeholders, plus which segments (if any) are bold+colored (name-class entities only, via an existing per-entity color source) vs bold-white (role-class labels) vs default (everything else).

4. **List the locale keys needed** — the new `game_log.*_format` key (English + Russian text) plus any already-existing keys it reuses (`country_name.*`, `organization_name.*`, `character.role.*.name`, etc.).

5. **Note the data DTO fields** the new `GameLogEntry`-equivalent needs, and the new event component's fields (should carry any delta/total/identity data directly — no downstream recomputation) — or confirm the existing `GameLogEntry` shape already covers it.

6. **Write the definition** to a short section the user can hand to `/plan` — do not edit `src/` or `Assets/` code.
