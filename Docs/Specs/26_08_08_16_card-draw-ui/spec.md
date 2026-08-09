# Spec: Card Draw Rework — UI

## Feature Intent

As a player building my organisation's country-action hand, I want a clear Draw control on the deck and a quick, readable choose-one-of-three animation, so that drawing a card feels intentional without slowing down normal play.

This spec covers issue #153's **part B** (deck controls, hand-size presentation, draw-choice interaction, and animation). Part A is already implemented: the authoritative `DrawCardsCommand` / `ReceiveCardCommand`, up-to-three persistent offer, eight-card country-hand cap, paid-discard trigger, bot behavior, and `CountryActionsState` projection are available for this UI to consume. Their gameplay contract remains documented in `Docs/Specs/26_08_08_16_card-draw-logic/spec.md`.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

### Draw control and hand-size label

- The player opens the selected country's Actions panel.
  - The country-card deck pile is visible => a localized Draw button appears over the face-down deck, with a localized `Hand: N/M` label directly below that button; `N` is the number of cards actually in hand, offered cards are not counted, and `M` is the projected authoritative cap (8).
  - A card is received, played, or paid-discarded => the label updates to the new hand count without waiting for the panel to be closed/reopened.
- Projected state reports `CanStartDraw == false`, including when the hand contains 8 cards, an offer is pending, or no drawable card exists => Draw is visibly unavailable and cannot emit a command.
- A card draw/play/discard animation owns the card surface => Draw is also non-interactive, even if the last projected gameplay state still reports it available.
- Projected state reports `CanStartDraw == true` and no conflicting card animation is active.
  - The player releases the primary pointer over Draw => exactly one draw command is emitted.
  - The pointer is released outside Draw => nothing happens, following the project's `PointerUpEvent` + manual `ContainsPoint` convention.
  - Draw is accepted => the command is pushed before any pause command in that frame, then the UI waits for the authoritative offer rather than inventing local choices.

### Deal offered cards face-down, then reveal

- A draw command produces three choices.
  - The authoritative choices appear => the game is paused if it was running, `ModalState` locks other UI/world interactions, the choice overlay has no close/cancel action, and the originating deck/hand cannot be used until one choice resolves.
  - Three card-back copies move from the deck to three stable positions centered on screen, one by one; each travel lasts 0.25 seconds, faster than today's 0.5-second replacement draw.
  - All three card backs are in position => they flip face-up one by one, each over 0.2 seconds; a card cannot be selected until every offered card is face-up and the reveal sequence finishes.
- The authoritative offer contains only one or two cards => the same sequence uses one or two centered positions, with no blank placeholders and no attempt to fabricate a third choice.
- A face-up offered card is rendered => it uses the existing country-card face, art, name/target formatting, cost, requirements, cooldown, war chance, and playable-country data supplied by state; the draw view does not maintain a second card-content implementation.

### Hover and selection

- All offered cards are face-up and selection is enabled.
  - The pointer enters one card => that card scales from 100% to 130% over exactly 0.2 seconds without moving the other cards.
  - The pointer exits => it scales back to 100% over exactly 0.2 seconds.
  - The scaled card renders above its siblings, remains fully inside the safe screen area, and does not obscure enough of a neighboring choice to make the target ambiguous.
- The player releases the primary pointer within one offered card.
  - Selection locks immediately => further hover/click input cannot submit another receive command.
  - The unselected cards move back to the originating deck one by one, each over 0.25 seconds, in their offer order.
  - After the unselected-card return completes => the UI emits one receive-card command using the authoritative `ChoiceIndex` and waits for projected state to confirm which hand slot received it.
  - The selected card moves into that rendered hand slot over 0.3 seconds; the temporary copy is then removed and the real hand card becomes visible.
  - The hand-size label updates and Draw becomes available again only if the hand is still below 8 and no other blocker exists.
- The pointer releases outside every offered card => no selection occurs and the offer remains open.

### Flow ownership, failure, and restoration

- The game was running before Draw => the draw command is pushed before Pause in the same frame; after a successful selection animation, only the pause introduced by this flow is removed.
- The game was already paused => the draw flow leaves it paused when complete.
- A successful paid-discard animation creates an offer => its card-to-deck animation hands pause/modal ownership directly to the draw flow, without an interactive or unpaused frame between the two sequences.
- An accepted Draw command does not produce an authoritative offer within the bounded wait => the overlay is not populated with guessed choices, flow-owned pause/modal state is released, and the refreshed Draw availability remains authoritative.
- A receive command is rejected or projected state does not confirm it within the bounded wait => temporary copies stay/reconstruct as a selectable offer when authoritative offer state still exists; the UI never pretends a card entered the hand.
- The HUD/view is disabled, rebuilt, or interrupted during animation.
  - Cleanup runs => all temporary copies are removed, any hidden real card/deck element is restored, scheduled hover/animation work stops, and the modal lock/pause ownership is released safely.
  - Authoritative offer state still exists when the view returns => the choices are reconstructed face-up and selectable without rerolling or replaying the initial deal.
- A save is loaded with a pending offer => the draw overlay opens with those saved choices face-up, interaction remains modal, and the player can finish the existing receive command.

### Existing card-play and paid-discard presentation

- A country card is played under the sibling logic spec.
  - Its existing hand-to-test/deck animation still completes => no automatic deck-to-hand replacement animation follows, because the vacancy now remains for the next explicit Draw.
- A country card is successfully paid-discarded.
  - Its existing hand-to-deck animation completes => the authoritative offer triggered by that discard starts the same face-down/reveal/select flow without requiring a separate Draw click or briefly restoring gameplay interaction.
  - The discarded card is not one of those choices; the paid-discard hold hint, gold affordability result, and fly text are otherwise unchanged.
- A country card is successfully paid-discarded and no other card is drawable => the discard and hand-size update still complete, no choice cards are fabricated, the modal flow closes after authoritative success is confirmed, and the vacancy remains for a later explicit Draw.
- The debug force-draw command is used => no choose-one animation is required; it remains a debug-only direct mutation.

## Tech Notes

### Deck controls

- `Assets/Scripts/Unity/UI/CountryActionsView.cs` currently builds the deck entirely in `BuildDeckPile` and exposes `DeckPileElement`. Add the draw button/hand label there (or as a small dedicated deck control view) and expose an `OnDrawRequested` callback. Refresh it from `CountryActionsState.Hand.Count`, `HandSize`, and `CanStartDraw`; combine that authoritative availability with presentation-only animation ownership.
- Use `PointerUpEvent` with `ContainsPoint` for the draw control and offered-card selection. Do not rely on `Button.clicked` or `ClickEvent`, which are unreliable in Unity 6000.4.1f1 per `.claude/rules/unity/uitoolkit.md`.
- Add layout-only classes to `Assets/UI/Overlay/OrgInfo/OrgActions.uss`. Keep shared button/text/color styling in `Assets/UI/Shared/SharedStyles.uss` and apply existing `gs-btn`, `gs-btn--small`, `gs-label`, and unavailable-state conventions rather than duplicating colors/fonts.
- Add `action.draw.button` / `action.draw.hand_size` as `Draw` / `Hand: {0}/{1}` in English and `Взять карту` / `Рука: {0}/{1}` in Russian. The optional `localization` skill may review these values when available, but implementation does not depend on that helper.

### Dedicated draw overlay and animator

- The existing `card-transition-overlay` in `Assets/UI/HUD/HUD.uxml` has `picking-mode="Ignore"`, and `CardTransitionView` recursively makes copies non-pickable; it is unsuitable as the interactive choice surface without changing its play-animation contract.
- Add a dedicated initially-hidden, full-screen draw overlay (for example `card-draw-overlay`) to `HUD.uxml`/`HUD.uss`, with modal background and a centered row that has enough spacing for 1.3× hover scale. Its interactive children use normal picking while the overlay blocks input beneath it.
- Prefer a dedicated presentation-only `CardDrawAnimator`/`CardDrawView` under `Assets/Scripts/Unity/UI` instead of adding a fourth responsibility to the already large `CardPlayAnimator`. Wire it from `HUDDocument` to:
  - the originating `CountryActionsView.DeckPileElement`;
  - projected `CountryActionsState.DrawChoices`;
  - `IWriteOnlyCommandAccessor`, `ModalState`, and localization/visual configs;
  - the newly rendered destination hand card after receipt.
- Reuse `ActionCardBuilder.Build(CountryCardFace, includeDiscardHint: false)` for every face-up offered copy and `ActionVisualConfig.defaultBackImage` for its back. A horizontal scale-to-zero/swap/scale-to-one transition is an acceptable UI Toolkit flip if a true 3D flip is unavailable.
- Use named constants for the timings (`0.25s` deal/return, `0.2s` flip/hover, `0.3s` selected travel) and await each deal/flip/return sequentially. Card selection uses `PointerUpEvent` and is registered only after reveal completes.
- Follow `.claude/rules/unity/game_loop_integration.md`: push `DrawCardsCommand` before `PauseCommand` in the same frame, include a bounded wait for the projected offer, and likewise bound the wait for receipt state.
- Acquire/release `ModalState` in `try/finally`; restore only a pause introduced by this sequence. Coordinate with `CardPlayAnimator` so paid discard transfers ownership continuously rather than independently unlocking/unpausing before the draw animator starts. The draw overlay, rather than raw EventSystem detection, blocks map/world input.

### State-to-animation identity and refresh

- Part A already projects ordered `CardDrawChoiceEntry` objects separately from `Hand` and `Deck`; offered cards are excluded from `CountryActionsState.Deck`. The UI must not subtract them a second time and must not tear down interactive temporary copies mid-gesture.
- `ReceiveCardCommand.ChoiceIndex` is authoritative. Match the selected offer to the received `ActionCardEntry` with its implemented stable domain identity (`ActionId`, `CountryContextId`, and relation/revenge target where present), because action id + target alone is not unique for all relation cards.
- `CountryActionsState.Set` and equality already notify the HUD for offer, hand-cap, and availability changes. Part B consumes those notifications; it does not introduce duplicate gameplay state in a MonoBehaviour.
- If state refreshes while the animator owns the card surface, follow the existing `SuppressRefresh` pattern. On receipt, briefly allow one refresh to build the authoritative hand card, hide it, animate the selected copy to its bounds, then reveal it.

### Remove obsolete replacement animation

- `Assets/Scripts/Unity/UI/CardPlayAnimator.cs` currently animates automatic replacements in:
  - `PlayCountryDiscardSequence` after the discard command;
  - `PlayCountrySequence` after a played card returns to the deck;
  - `PlaySequence` for org cards, which is outside this country-only change.
- Remove only the two country replacement lookup/travel blocks. Keep the org-card replacement branch, country play/discard effects, barriers, and defensive cleanup. The paid-discard path additionally hands an authoritative pending offer to the new draw sequence before releasing presentation ownership.

### Verification surface

- Pure logic/state tests should cover Draw availability and `Hand: N/M` inputs; Unity UI Toolkit behavior requires Editor verification at implementation time.
- Editor verification matrix: 3/2/1 choice layouts, full-hand disabled state, 1.3× hover in/out timing, single-click lockout, sequential return order, selected-to-gapped-slot travel, already-paused flow, locale changes, panel/HUD disable cleanup, and reload with a pending offer.

## Out of Scope

- Draw probability, command validation, hand capacity, persistence, and bot priority rules, which belong to the sibling logic spec.
- Org-card UI changes or re-enabling the hidden org-card surface; the owner confirmed that only country cards are in scope.
- New card art, sound, haptics, particle effects, or changes to card face dimensions/content.
- Redesigning requirements, playable-country badges, cooldown overlays, or paid-discard pricing/hold interaction.
- Changing normal card-play effect/barrier animations beyond removing the now-obsolete automatic replacement draw.

## Resolved Decisions

The owner resolved Part A's questions: country cards only, cap 8, up to three choices, paid discard triggers the shared offer flow, and control cards have bot priority.

The owner also approved all Part B presentation defaults before planning:

0. The choice flow is a mandatory modal. It pauses a running game, blocks all other UI/world interaction, and has no cancel or close action until one offered card is selected.
1. Deal and unselected-return travel take 0.25 seconds per card, each flip takes 0.2 seconds, selected-to-hand travel takes 0.3 seconds, and hover-in/hover-out each take exactly 0.2 seconds.
2. A paid discard remains one continuous paused/modal presentation. After the discarded card reaches the deck, the offer begins without an interactive or unpaused frame between the two sequences.
