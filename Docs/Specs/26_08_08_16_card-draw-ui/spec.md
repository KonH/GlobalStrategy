# Spec: Card Draw Rework — UI

## Feature Intent

As a player building my organisation's country-action hand, I want a clear Draw control on the deck and a quick, readable choose-one-of-three animation, so that drawing a card feels intentional without slowing down normal play.

This spec covers issue #153's **spec B** (deck controls, hand-size presentation, draw-choice interaction, and animation). The authoritative draw/receive commands, eight-card cap, bot behavior, and unresolved owner-kind scope are specified in `Docs/Specs/26_08_08_16_card-draw-logic/spec.md`.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

### Draw control and hand-size label

- The player opens the selected country's Actions panel.
  - The deck pile is visible => a localized Draw button appears on the face-down deck, with a localized `Hand: N/M` label immediately below the button; `N` is the current country-card count and `M` is the authoritative cap (8 under the sibling logic spec).
  - A card is received, played, or paid-discarded => the label updates to the new hand count without waiting for the panel to be closed/reopened.
- The hand contains 8 cards => Draw is visibly unavailable and cannot emit a command.
- An offer is already pending, a card draw/play/discard animation owns the card surface, or no drawable card exists => Draw is also non-interactive, preventing duplicate or impossible requests.
- The hand has a free slot, no offer is pending, no conflicting card animation is active, and at least one card is drawable.
  - The player releases the primary pointer over Draw => exactly one draw command is emitted.
  - The pointer is released outside Draw => nothing happens, following the project's `PointerUpEvent` + manual `ContainsPoint` convention.

### Deal offered cards face-down, then reveal

- A draw command produces three choices.
  - The draw flow starts => the game is paused if it was running, `ModalState` locks other UI/world interactions, and the originating deck/hand cannot be used until the flow resolves.
  - Three card-back copies move from the deck to three stable positions centered on screen, one by one; each travel lasts 0.25 seconds, faster than today's 0.5-second replacement draw.
  - All three card backs are in position => they flip face-up one by one, each over 0.2 seconds; a card cannot be selected until every offered card is face-up and the reveal sequence finishes.
- The authoritative offer contains only one or two cards => the same sequence uses one or two centered positions, with no blank placeholders and no attempt to fabricate a third choice.
- A face-up offered card is rendered => it uses the existing country-card face, art, name/target formatting, cost, requirements, cooldown, war chance, and playable-country data supplied by state; the draw view does not maintain a second card-content implementation.

### Hover and selection

- All offered cards are face-up and selection is enabled.
  - The pointer enters one card => that card scales from 100% to 130% over exactly 0.2 seconds without moving the other cards.
  - The pointer exits => it scales back to 100% over exactly 0.2 seconds.
  - The scaled card remains fully visible and does not overlap a neighboring choice enough to obscure which card will be selected.
- The player releases the primary pointer within one offered card.
  - Selection locks immediately => further hover/click input cannot submit another receive command.
  - The unselected cards move back to the originating deck one by one, each over 0.25 seconds, in their offer order.
  - The UI emits one receive-card command for the selected choice and waits for projected state to confirm which hand slot received it.
  - The selected card moves into that rendered hand slot over 0.3 seconds; the temporary copy is then removed and the real hand card becomes visible.
  - The hand-size label updates and Draw becomes available again only if the hand is still below 8 and no other blocker exists.
- The pointer releases outside every offered card => no selection occurs and the offer remains open.

### Flow ownership, failure, and restoration

- The game was running before Draw => the draw command is pushed before Pause in the same frame; after a successful selection animation, only the pause introduced by this flow is removed.
- The game was already paused => the draw flow leaves it paused when complete.
- A receive command is rejected or projected state does not confirm it within the bounded wait => temporary copies stay/reconstruct as a selectable offer when authoritative offer state still exists; the UI never pretends a card entered the hand.
- The HUD/view is disabled, rebuilt, or interrupted during animation.
  - Cleanup runs => all temporary copies are removed, any hidden real card/deck element is restored, scheduled hover/animation work stops, and the modal lock/pause ownership is released safely.
  - Authoritative offer state still exists when the view returns => the choices are reconstructed face-up and selectable without rerolling or replaying the initial deal.
- A save is loaded with a pending offer => the draw overlay opens with those saved choices face-up, interaction remains modal, and the player can finish the existing receive command.

### Existing card-play and paid-discard presentation

- A country card is played or paid-discarded under the sibling logic spec.
  - Its existing hand-to-test/deck animation still completes => no automatic deck-to-hand replacement animation follows, because the vacancy now remains for the next explicit Draw.
  - The paid-discard hold hint, gold affordability result, and fly text are otherwise unchanged.
- The debug force-draw command is used => no choose-one animation is required; it remains a debug-only direct mutation.

## Tech Notes

### Deck controls

- `Assets/Scripts/Unity/UI/CountryActionsView.cs` currently builds the deck entirely in `BuildDeckPile`. Add the draw button/hand label there (or as a small dedicated deck control view) and expose an `OnDrawRequested` callback. Refresh it from `CountryActionsState.Hand.Count`, `HandSize`, `CanStartDraw`, and pending/animation state.
- Use `PointerUpEvent` with `ContainsPoint` for the draw control and offered-card selection. Do not rely on `Button.clicked` or `ClickEvent`, which are unreliable in Unity 6000.4.1f1 per `.claude/rules/unity/uitoolkit.md`.
- Add layout-only classes to `Assets/UI/Overlay/OrgInfo/OrgActions.uss`. Keep shared button/text/color styling in `Assets/UI/Shared/SharedStyles.uss` and apply existing `gs-btn`, `gs-btn--small`, `gs-label`, and unavailable-state conventions rather than duplicating colors/fonts.
- Add locale keys such as `action.draw.button` (`Draw`) and `action.draw.hand_size` (`Hand: {0}/{1}`) to both English and Russian localization assets. At implementation time use the `localization` skill for a real Russian translation.

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
- Acquire/release `ModalState` in `try/finally`; restore only a pause introduced by this sequence. The draw overlay, rather than raw EventSystem detection, blocks map/world input.

### State-to-animation identity and refresh

- The sibling logic spec projects ordered `CardDrawChoiceEntry` objects separately from `Hand`/`Deck`. `CountryActionsView.Refresh` must exclude those cards from its deck pile count while the offer is active and must not tear down interactive temporary copies mid-gesture.
- `ReceiveCardCommand.ChoiceIndex` is authoritative. To find the selected card after it becomes `CardInHand`, retain a stable domain identity in the projected entry (action id + primary country context + relation/revenge target) or expose an equivalent runtime correlation; `ActionId` + target alone is not unique for all relation cards.
- Update `CountryActionsState.Set`, `StateEquality`, action-entry equality tests, and list-state benchmarks so offer changes and hand-cap changes notify the HUD reliably.
- If state refreshes while the animator owns the card surface, follow the existing `SuppressRefresh` pattern. On receipt, briefly allow one refresh to build the authoritative hand card, hide it, animate the selected copy to its bounds, then reveal it.

### Remove obsolete replacement animation

- `Assets/Scripts/Unity/UI/CardPlayAnimator.cs` currently animates automatic replacements in:
  - `PlayCountryDiscardSequence` after the discard command;
  - `PlayCountrySequence` after a played card returns to the deck;
  - `PlaySequence` for org cards (only remove this branch if owner-kind scope expands).
- Under the country-only assumption, remove the country replacement lookup/travel blocks but keep the rest of the play/discard sequences, barriers, pause ownership, and cleanup intact.

### Verification surface

- Pure logic/state tests should cover Draw availability and `Hand: N/M` inputs; Unity UI Toolkit behavior requires Editor verification at implementation time.
- Editor verification matrix: 3/2/1 choice layouts, full-hand disabled state, 1.3× hover in/out timing, single-click lockout, sequential return order, selected-to-gapped-slot travel, already-paused flow, locale changes, panel/HUD disable cleanup, and reload with a pending offer.

## Out of Scope

- Draw probability, command validation, hand capacity, persistence, and bot priority rules, which belong to the sibling logic spec.
- Org-card UI changes or re-enabling the hidden org-card surface unless Ambiguities questions 0–1 in the sibling logic spec expand scope.
- New card art, sound, haptics, particle effects, or changes to card face dimensions/content.
- Redesigning requirements, playable-country badges, cooldown overlays, or paid-discard pricing/hold interaction.
- Changing normal card-play effect/barrier animations beyond removing the now-obsolete automatic replacement draw.

## Ambiguities

The UI depends on Ambiguities questions 0–4 in the sibling logic spec. No additional UI-only decision blocks planning: the travel/flip timings above are concrete defaults that satisfy "faster than current" and preserve the issue's exact 0.2-second hover requirement; they can be tuned during Editor review without changing gameplay semantics.
