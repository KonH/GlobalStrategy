# Spec: Card Deck Chance Display

## Feature Intent

As a player (or, at minimum, a developer using the debug card-availability view), I want to see each card's draw chance (weight-derived percentage) instead of, or in addition to, its raw deck copy count, so that I can understand how likely a card is to be drawn rather than only how many copies of it exist in the deck.

## Acceptance Criteria

- **Given** the debug card-availability view (`DebugCardAvailabilityView`) is open and showing the current deck **When** a card group row is rendered **Then** the row displays the card's draw chance as a percentage, computed as `100 * drawWeight / totalDrawWeight` where `drawWeight` comes from the group's `ActionDefinition.DeckCopies` (0 for cards whose target country no longer exists)
- **Given** a card group's `drawWeight` is 0 (e.g. its target country no longer exists) **When** its row is rendered **Then** its displayed chance is 0% (or an equivalent explicit "unavailable" indicator), not a blank or misleading value
- **Given** the raw deck copy count (`x{count}`) is currently shown alongside the computed chance percentage in the debug view **When** this feature is implemented **Then** the row's primary displayed number is the chance percentage — whether the raw copy count is removed entirely or retained as secondary/tooltip detail is resolved by the clarification below
- **Given** multiple card groups exist in the same deck **When** their chances are computed **Then** the displayed percentages are internally consistent (each group's percentage is proportional to its `drawWeight` relative to the sum of all groups' `drawWeight` in that deck) and, for a deck with only nonzero-weight groups, sum to approximately 100%
- **Given** the chosen chance-display surface(s) are updated **When** the same deck is viewed twice without changing game state **Then** the displayed chance value is stable (deterministic) between views
- **Given** the feature scope includes a player-facing surface (not just the debug view) **When** a player views that surface **Then** the chance text uses a real localization key per `.claude/rules/unity/localization.md` (not an unlocalized literal), unless the resolved scope explicitly follows the existing unlocalized `%`-literal precedent in `ActionCardBuilder.BuildWarWinChanceBadge`
- **Given** the feature scope is limited to the debug view only **When** the debug view is used **Then** no new production/localized UI element is required, since the debug view is English-only and gated behind the dev debug menu

## Out of Scope

- Changing the underlying draw-weight calculation or sampling logic in `src/Game.Systems/DrawCardSystem.cs` (`PickWeightedIndex`, `AdjustWeight`) — this feature is about *display* of chance, not changing actual draw probabilities
- Introducing a new concept of "friendly" vs "non-friendly" cards/decks — no such distinction exists in the codebase today (`CardOwnerKind` only distinguishes `Org` vs `Country` ownership), and this feature does not add one
- Reworking the deck-pile visual stacking logic in `OrgActionsView.BuildDeckPile` / `CountryActionsView.BuildDeckPile` (the shadow-layer-count use of `deckCount`) unless the resolved scope specifically calls for a new tooltip/panel on that element
- Any change to how many stacked card-back layers are drawn behind the deck pile visual

## Ambiguities

- [NEEDS CLARIFICATION: Does "non-friendly deckCount" in the request refer to the debug view's existing `x{count}` raw-copy-count text (i.e. replace/augment that text with the chance percentage that the debug view already computes), or does it mean the requester wants a brand-new player-facing chance display added somewhere players currently see nothing (e.g. a tooltip on the deck pile, or a new "deck contents" panel)? These are very different-sized features. No existing player-facing UI surfaces per-card draw odds today.]
- [NEEDS CLARIFICATION: If a player-facing surface is wanted, where exactly should it live — a tooltip on hover over the deck pile (`OrgActionsView`/`CountryActionsView`), or a dedicated expandable panel similar to the debug view but production-styled and localized?]
- [NEEDS CLARIFICATION: In the debug view, should the existing `"{CardName} x{count} ({percent}%)"` format simply drop the `x{count}` segment (pure "instead of" reading, becoming `"{CardName} ({percent}%)"`), or should both pieces of information remain and the real ask is about surfacing chance somewhere new?]
- [NEEDS CLARIFICATION: Should the displayed chance match production draw odds exactly — i.e. apply `DrawCardSystem.AdjustWeight`'s runtime multipliers (`stop_rivalry` ×0.5 always, `declare_war` ×1.7 for the player org under certain conditions) — fixing the current parity gap where `DebugCardAvailabilityView.GetDrawWeight` ignores these multipliers? Or should it continue using base `DeckCopies` only, matching the debug view's current simpler behaviour?]
- [NEEDS CLARIFICATION: Does this apply to both org decks (`OrgActionsView`) and country/relation-targeted decks (`CountryActionsView`), or just one of the two?]
- [NEEDS CLARIFICATION: What precision/format should the chance use — whole-number percent (matching the existing war-win-chance badge precedent in `ActionCardBuilder.BuildWarWinChanceBadge`), or fractional/2-decimal precision (matching the debug view's current `CalculateChancePercent` behaviour, which matters for low-weight targeted cards where whole-number rounding could hide a near-zero-but-nonzero chance)?]
- [NEEDS CLARIFICATION: Should any new or changed chance text use a proper localization key per `.claude/rules/unity/localization.md`, or is an unlocalized literal `%` acceptable, following the existing precedent in `ActionCardBuilder`? (This only matters if the resolved scope includes a player-facing surface — the debug view itself is already English-only and does not need localization.)]
