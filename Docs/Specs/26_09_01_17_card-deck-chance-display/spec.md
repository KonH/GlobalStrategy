# Spec: Card Deck Chance Display

## Feature Intent

As a developer using the debug card-availability view (`DebugCardAvailabilityView`), I want to see each card group's draw chance as a percentage instead of its raw deck copy count (`x{count}`), so that I can understand how likely a card is to be drawn rather than only how many copies of it exist in the deck. This is an internal/debug-only concern — there is no player-facing surface in scope.

As part of the same change, the two draw-weight multipliers that `DrawCardSystem.AdjustWeight` currently hardcodes by matching literal action IDs (`"stop_rivalry"` → ×0.5, `"declare_war"` → ×1.7 under conditions) move into `action_config.json` as an optional per-action config value, so the debug view's displayed chance can be computed from the same weight the real draw system uses, and so the hardcoded action-id branches can be removed.

## Acceptance Criteria

- **Given** the debug card-availability view is open and showing the current deck **When** a card group row is rendered **Then** the row displays the card's draw chance as a percentage instead of the raw `x{count}` copy count — the `x{count}` segment is removed, not merely supplemented
- **Given** a card group's draw weight is 0 (e.g. its target country no longer exists) **When** its row is rendered **Then** its displayed chance is `0%`, not blank or misleading
- **Given** `ActionDefinition` gets a new optional weight-modifier field (e.g. `DrawWeightMultiplier` / JSON `drawWeightMultiplier`, nullable or defaulting to `1.0` when absent) **When** `action_config.json` is updated **Then** `stop_rivalry` is configured with the modifier value that reproduces today's ×0.5 behavior and `declare_war` is configured with the modifier value that reproduces today's ×1.7 behavior, with no other action needing the field
- **Given** the new config field exists **When** `DrawCardSystem.AdjustWeight` runs **Then** it applies the configured multiplier from `ActionDefinition` (looked up via `ActionConfig.Find`) generically, and the hardcoded `actionId == "stop_rivalry"` / `actionId == "declare_war"` branches (including the `declare_war`-specific player-org/rival-control gating in `HasControlInRivalOf`) are removed or reduced to only what config cannot express — see Ambiguity 0 below on how the `declare_war` conditional gating is handled
- **Given** the config-driven multiplier exists **When** the debug view computes a card group's draw weight (`DebugCardAvailabilityView.GetDrawWeight`) **Then** it multiplies `ActionDefinition.DeckCopies` by the configured `DrawWeightMultiplier` (defaulting to `1.0` when absent), so the debug view's percentages reflect the same weighting the real draw system uses instead of ignoring it as today
- **Given** a card group's chance is computed **When** it is displayed **Then** the underlying value is a `double` (matching `CalculateChancePercent`'s existing `double`/`Math.Round(..., 2)` behavior), and the debug view renders it at 2-decimal precision via the existing `FormatNumber` helper
- **Given** this applies to both org decks (`OrgActionsView`'s underlying debug data) and country/relation-targeted decks (`CountryActionsView`'s underlying debug data) **When** either deck type is shown in the debug view **Then** both `BuildDeckCard` (untargeted/single-card rows) and `BuildTargetedDeckGroup` (targeted-group rows) drop `x{count}` in favor of the chance percentage
- **Given** multiple card groups exist in the same deck **When** their chances are computed **Then** the displayed percentages are internally consistent (each proportional to its share of the deck's total draw weight) and, for a deck with only nonzero-weight groups, sum to approximately 100%
- **Given** the same deck is viewed twice without changing game state **When** it is rendered both times **Then** the displayed chance value is stable (deterministic) between views
- **Given** this is a debug-only, English-only, dev-menu-gated view **When** the feature is implemented **Then** no localization key is added and no unlocalized-literal precedent question applies — the existing unlocalized `%` text in this view is retained as-is

## Out of Scope

- Any player-facing UI surface for draw chance — this issue is explicitly internal/debug-only (owner: *"it is internal number for config"*, *"internals, so debug-only"*)
- Introducing a new concept of "friendly" vs "non-friendly" cards/decks — no such distinction exists in the codebase (`CardOwnerKind` only distinguishes `Org` vs `Country` ownership), and the "non-friendly deckCount" wording in the original request referred to the developer-facing debug view, not a new player concept
- Reworking the deck-pile visual stacking logic in `OrgActionsView.BuildDeckPile` / `CountryActionsView.BuildDeckPile` (the shadow-layer-count use of a local `deckCount` variable) — that code is unrelated to `DebugCardAvailabilityView` and is not touched by this feature
- Changing the actual draw *probabilities* — `PickWeightedIndex`'s roulette-selection algorithm is unchanged; only how its inputs are configured (moving two hardcoded multipliers into config) and how the debug view displays the result
- Adding config-driven weight modifiers for any action beyond `stop_rivalry` and `declare_war` — the new field is introduced generically, but only these two existing behaviors are migrated to use it

## Resolved Decisions

Answered by the repo owner on the issue (2026-09-01); replaces the prior Ambiguities list:

0. **Scope is debug-view only** — "non-friendly deckCount" referred to the developer-facing debug view's existing `x{count}` text, not a request for a new player-facing surface. No player-facing UI is added.
1. No player-facing surface — the chance is "an internal number for config," confirming decision 0.
2. Display format is **percent** — the debug row shows chance as a percentage, replacing (not supplementing) the raw count.
3. **The `stop_rivalry` (×0.5) and `declare_war` (×1.7) multipliers move from hardcoded `DrawCardSystem.AdjustWeight` branches into `action_config.json` as an optional per-action config value.** The custom code implementing these two special cases is removed in favor of reading the configured value generically. Implementation note for `/plan`: `declare_war`'s multiplier today is conditionally gated (player-org-only, requires rival control) — the config value should represent the multiplier magnitude (×1.7); whether the conditional gating itself is also expressed in config or remains as narrower supporting code (since it depends on runtime world state, not a static per-action value) is a `/plan`-time implementation decision, not a further open question for the owner.
4. Applies to **both** org decks and country/relation-targeted decks.
5. The chance is stored/computed as a **`double`**; the debug view displays it at **2-decimal precision** (matching the existing `CalculateChancePercent`/`FormatNumber` behavior).
6. No localization — this is **debug-only** ("internals"), so the existing unlocalized debug-view text convention applies unchanged.
