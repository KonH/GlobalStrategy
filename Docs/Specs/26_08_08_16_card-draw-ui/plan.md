# Plan: Card Draw Rework — UI

## Spec faithful summary

Add an intentional country-card acquisition surface to the selected country's Actions panel. The face-down deck gains a localized Draw control and an authoritative localized `Hand: N/M` label. Draw is available only when `CountryActionsState.CanStartDraw` permits it and no card animation owns the surface. An accepted draw opens a mandatory modal flow: deal the authoritative one-to-three offered cards face-down from the deck, reveal them sequentially, allow one 130% hover/select interaction, return the unselected cards to the deck in offer order, submit the selected authoritative `ChoiceIndex`, and animate the confirmed received card into its actual hand slot. Preserve and restore pending offers, bound every command wait, clean up safely when the HUD is interrupted, and keep a paid discard as one continuous paused/modal transition into the same offer flow. Remove only the obsolete country-card automatic replacement animations; org-card replacement remains unchanged.

## Goal

Implement Part B as presentation and input glue over Part A's existing ECS commands and projected state, with one reusable draw-flow coordinator for explicit draws, paid-discard offers, and restored pending offers.

## Approach

### Keep gameplay authority in Part A

Do not add or infer deck, capacity, eligibility, choice, receipt, or persistence rules in Unity. `CountryActionsState.Hand`, `HandSize`, `DrawChoices`, `HasPendingDraw`, and `CanStartDraw` remain the only presentation inputs. The UI emits only the existing `DrawCardsCommand`, `DiscardCardCommand`, `ReceiveCardCommand`, `PauseCommand`, and `UnpauseCommand`; it never chooses or manufactures a card locally.

Part A currently omits `PlayableCountryIds` on offered entries because `VisualStateConverter.BuildEntry` populates that presentation field only for an unplayable card already in hand. Make a narrow `src/Game.Main/VisualStateConverter.cs` projection correction: separate “include playable-country presentation data” from `isInHand`, pass it for both hand and draw-choice entries, and keep deck entries lightweight. This does not change playability or gameplay state; it supplies the existing face builder with the data the approved UI spec requires. Extend `VisualStateConverterCardDrawTests` and draw-choice state-equality/notification coverage for this projection.

Use `CardDrawChoiceEntry.ChoiceIndex` for submission. Before receipt, snapshot the occupied hand slots. After the offer clears, identify the received physical card as an entry in a newly occupied slot and verify that its projected `ActionId`, `CountryContextId`, and `TargetCountryId` match the selected offer. The before/after slot delta disambiguates identical configured deck copies; the identity check prevents an unrelated concurrent state change from being mistaken for receipt. Do not treat list position or a preselected slot as identity.

### Extend the existing deck view

Update `CountryActionsView` so `BuildDeckPile` receives the full `CountryActionsState` and builds the controls into the front face of the existing 240×360 deck pile:

- a `Button` using `gs-btn` / `gs-btn--small` and a layout-only class;
- a `Label` using `gs-label` and a layout-only class directly below it;
- localized text from `action.draw.button` and `action.draw.hand_size`;
- `SetEnabled(state.CanStartDraw && !PresentationBusy)` plus a primary `PointerUpEvent` and manual `ContainsPoint` check before raising `OnDrawRequested`.

Keep `DeckPileElement` pointing at the front card so all animations retain a stable deck origin. Add a presentation-busy setter that disables the Draw control immediately even while normal state refresh is suppressed, cancels any active click/hold-discard gesture, and prevents new hand-card gestures until ownership is released. Continue deriving `N` from `state.Hand.Count` and `M` from `state.HandSize`; pending choices are already excluded from `Hand` and must not be subtracted from `Deck` again.

Put only the control positioning, size, and spacing in `Assets/UI/Overlay/OrgInfo/OrgActions.uss`. Reuse the existing shared button, label, disabled, and card styles without duplicating colors or fonts. Add `action.draw.button` / `action.draw.hand_size` as `Draw` / `Hand: {0}/{1}` in English and `Взять карту` / `Рука: {0}/{1}` in Russian. These explicit translations keep implementation executable when the repository's optional localization helper is unavailable.

### Add a dedicated draw overlay and view

Add an initially hidden `card-draw-overlay` and centered `card-draw-row` to `Assets/UI/HUD/HUD.uxml`, before the existing non-interactive transition overlay. The draw overlay uses the shared `gs-modal-root`, has no close/cancel control, and keeps normal picking enabled so it blocks the HUD and world beneath it. Add layout-only overlay, row, and temporary-card classes to `Assets/UI/HUD/HUD.uss`. Space cards with per-card margins because this Unity version does not support `gap`; reserve enough room for a 1.3× hovered card and use centered one-, two-, and three-card layouts without placeholders.

The draw overlay is a child of the existing HUD `UIDocument`, whose normal sorting order is 0, while nonterminal windows/fly text reach 990/1000 and the terminal End Game document uses 1100. On flow acquisition, snapshot the HUD document's current sorting order and raise it to a named draw-modal order of 1050 before showing the shield; restore the exact prior value in generation-owned cleanup. This keeps an unresolved offer above every current nonterminal UI document without adding a second document or scene object. End Game intentionally remains above it: after terminal completion, `GameLogic` no longer processes receipt commands, so the terminal surface must take precedence instead of leaving an unresolvable draw modal on top.

The overlay blocks pointer interaction, while `ModalState` blocks map/camera consumers. Update `TimeInputHandler` to receive the already-registered `ModalState` and ignore space/speed shortcuts while any modal owns the scene; update `GameMenuDocument.Update` so Escape may close its own visible menu, but cannot open the menu while another modal owner is already locked. This intentionally applies consistent keyboard blocking to every existing `ModalState` owner, not only card draw: no modal dialog should allow time/speed changes or a second pause menu behind it. Add regression checks for a representative non-draw modal as well as the draw flow.

Create `Assets/Scripts/Unity/UI/CardDrawView.cs` as a plain presentation class owned by `HUDDocument`. It will:

- show the full-screen, picking-enabled modal shield immediately when a flow takes ownership, before command waits or paid-discard travel, and keep it active through recovery/cleanup so unrelated HUD controls cannot be clicked while `ModalState` blocks map/camera input;
- build offered faces through `ActionCardBuilder.Build(face, includeDiscardHint: false)` so the existing art, cost, requirements, cooldown, war chance, and playable-country rendering stays single-sourced;
- build backs from `ActionVisualConfig.defaultBackImage`;
- convert the originating deck/card world bounds into overlay-local coordinates;
- deal temporary cards sequentially to stable centered slots at 0.25 seconds each;
- flip each card sequentially at 0.2 seconds by scaling its horizontal axis to zero, swapping back for face, then scaling back to one;
- register selectable `PointerUpEvent` handlers only after every flip completes;
- animate hover in/out between 1.0 and 1.3 over exactly 0.2 seconds, bring the hovered element above siblings, and restore ordering/scale on exit;
- lock selection on the first valid release, return unselected cards sequentially in `ChoiceIndex` order at 0.25 seconds each, and move the selected copy to a supplied hand element at 0.3 seconds;
- expose deterministic cleanup that cancels scheduled/async hover work, removes every temporary copy, restores hidden real elements, and hides the overlay.

Use named timing constants and unscaled/realtime frame progression so presentation still advances while the simulation is paused. Every geometry wait and movement loop must be cancellation-aware; a detached element or disabled HUD must terminate through cleanup instead of leaving a pending task or invisible card.

### Coordinate commands, state, pause, and restoration

Create `Assets/Scripts/Unity/UI/CardDrawAnimator.cs` as a plain presentation-flow coordinator, also owned by `HUDDocument`. Construct it with the existing injected `VisualState`, `IWriteOnlyCommandAccessor`, `ILocalization`, visual configs, `ModalState`, the HUD `UIDocument`, the `CountryActionsView`, and the new `CardDrawView`. This is a per-HUD helper, not a singleton and not a second `UIDocument` or MonoBehaviour, so no scene or VContainer registration is required.

The coordinator owns exactly one generation/cancellation token and one pause flag per flow:

- **Explicit Draw:** synchronously mark the card surface busy, display the picking-enabled overlay shield, and acquire `ModalState`; push `DrawCardsCommand` first and, only if the game was running, `PauseCommand` second in the same frame. Bound the wait for a coherent non-empty authoritative `DrawChoices` projection. If it arrives, deal/reveal it from the deck; if not, release only flow-owned state and refresh authoritative availability.
- **Restored pending offer:** when the HUD starts/enables or a state refresh reveals an offer with no active local flow, ensure the selected country's Actions slide is open, acquire modal/pause ownership, and reconstruct the authoritative choices face-up and selectable. Do not replay deal/flip, reroll, or emit `DrawCardsCommand`. Expose a narrow `CountryInfoView.EnsureActionsOpen` presentation helper so the restored flow has live deck/hand geometry for the eventual selected-to-slot travel even when the slide initially rebuilt closed.
- **Selection:** snapshot occupied hand slots, then after the view returns unselected cards push one `ReceiveCardCommand` with the selected `ChoiceIndex`; bound the wait for the pending offer to clear and a matching-identity card to appear in a newly occupied slot. Briefly allow/rebuild `CountryActionsView`, find that authoritative rendered slot, hide it, animate the selected copy to its bounds, reveal it, and then finish. If receipt is rejected, ambiguous, or times out while the authoritative offer still exists, rebuild the same offer face-up and selectable; never fake a receipt.
- **Paid-discard result:** after the card reaches the deck, distinguish three authoritative outcomes within the bounded wait. A pending offer continues into deal/reveal. A confirmed removal of the original hand card with no pending offer is the valid Part A “no other drawable card” success: refresh the vacancy and finish without choices. If the original card remains and no offer appears, treat it as rejection/timeout and restore it. Never infer discard success from animation alone.
- **Serialized cancellation:** retain the active flow task and make restoration await a `CancelAndWaitAsync` barrier before it can acquire a new flow. Use a distinct per-flow modal owner token and generation checks for shared view/busy/sorting/pause cleanup as a second line of defense. A stale `finally` may unlock only its own token and may not hide, unsuppress, restore sorting order, or unpause after a newer generation starts.
- **Cleanup:** in the current generation's `finally`, restore any hidden deck/hand/card elements and the HUD document's exact prior sorting order, clear both refresh suppression and presentation-busy state, unlock that flow's `ModalState` token, and emit `UnpauseCommand` only when this flow introduced the pause. A flow started while already paused leaves the game paused.

`HUDDocument` adds `ModalState` to its existing VContainer-injected dependencies, constructs the view/coordinator after `CountryInfoView`, wires `CountryActionsView.OnDrawRequested`, routes existing country paid-discard input to the coordinator, and calls the coordinator from `HandleCountryActionsChanged` after refreshing ordinary views. Refactor the country-action event wiring into idempotent subscribe/unsubscribe helpers: `Start` subscribes after constructing the views, `OnDisable` unsubscribes and requests cancellation, and a later `OnEnable` asynchronously awaits cancellation completion before it resubscribes/restores. Before restoration, call `CountryInfoView.EnsureActionsOpen` so receipt can target a rendered hand slot. This also corrects the existing start-only subscription pattern, which otherwise leaves country-card input detached after a HUD disable/re-enable cycle. Draw, play, and discard handlers mutually guard `CardPlayAnimator.IsPlaying` and `CardDrawAnimator.IsPlaying` so the two presentation owners cannot overlap; ordinary country play marks the shared card surface busy before starting and clears it from the completion path.

### Make paid discard one continuous draw flow

Move the country paid-discard presentation out of `CardPlayAnimator` and into `CardDrawAnimator.StartPaidDiscard`. At the start of that single flow, immediately raise the HUD document and display the picking-enabled overlay shield, push `DiscardCardCommand` before an optional `PauseCommand`, suppress the real hand, and animate the existing face from its hand bounds to the deck using the current 0.55-second travel. Then apply the three-way authoritative result check above. A pending offer continues directly into the same face-down deal/reveal/select/receive flow without hiding the shield, unlocking modal state, or unpausing between phases; confirmed success with no drawable replacement shows the vacancy and finishes; rejection restores the card without guessed choices.

Delete `CardPlayAnimator.StartCountryCardDiscard` and `PlayCountryDiscardSequence` once `HUDDocument` routes the gesture to the new coordinator. In `PlayCountrySequence`, remove the obsolete country replacement lookup and deck-to-hand travel after a played card returns to the deck; keep an unconditional unsuppress and final authoritative refresh so the vacancy becomes visible instead of retaining the temporary re-suppression that existed only for replacement lookup. Keep action-result barriers, test-card/deck travel, defensive cleanup, and the org-card `PlaySequence` replacement path unchanged.

### Refresh and lifecycle rules

Retain `CountryActionsView.SuppressRefresh` while temporary copies own the card surface. State notifications may update label/offer data underneath, but must not clear/rebuild the live gesture. At the single receipt handoff, turn suppression off, explicitly refresh from current state/resources, turn suppression back on, then find and hide the destination card for travel. Reset suppression unconditionally outside conditional lookup branches.

On `HUDDocument.OnDisable`, cancel the coordinator before ordinary view teardown. On re-enable/start, inspect authoritative state and restore any pending offer face-up. A panel slide closing is not allowed to cancel an authoritative offer; the full-screen draw overlay remains the interaction surface until receipt. A full HUD disable/load interruption cleans local presentation and restoration rebuilds from state when the HUD returns.

## Agent Steps

- [ ] Resolve the three approved Part B questions in `Docs/Specs/26_08_08_16_card-draw-ui/spec.md` so the spec records the mandatory modal, approved timings, and continuous paid-discard ownership as decisions rather than ambiguities.
- [ ] Update `src/Game.Main/VisualStateConverter.cs` so offered draw choices, like hand cards, receive projected `PlayableCountryIds` when relevant without changing gameplay playability. Extend `src/Game.Tests/VisualStateConverterCardDrawTests.cs` and draw-choice notification/equality coverage.
- [ ] Add `action.draw.button` and `action.draw.hand_size` to `Assets/Localization/en.asset` / `ru.asset` with the recorded English `Draw` / `Hand: {0}/{1}` and Russian `Взять карту` / `Рука: {0}/{1}` values.
- [ ] Update `Assets/Scripts/Unity/UI/CountryActionsView.cs` with the Draw callback, localized `Hand: N/M` presentation, authoritative/presentation availability combination, immediate busy-state setter, active-gesture cancellation/rejection, and manual `PointerUpEvent` bounds handling. Update `Assets/UI/Overlay/OrgInfo/OrgActions.uss` with layout-only deck-control rules.
- [ ] Add the dedicated interactive draw overlay/row to `Assets/UI/HUD/HUD.uxml` and its responsive one-/two-/three-card layout classes to `Assets/UI/HUD/HUD.uss`; reuse shared modal/card styles and per-child margins rather than unsupported `gap`.
- [ ] Update `Assets/Scripts/Unity/UI/TimeInputHandler.cs` to inject the existing `ModalState` and suppress pause/speed keyboard commands while a modal is locked; update `Assets/Scripts/Unity/UI/GameMenuDocument.cs` so Escape closes its own visible menu but does not open it behind another modal owner.
- [ ] Add `Assets/Scripts/Unity/UI/CardDrawView.cs` for card-back creation, existing face-builder reuse, sequential deal/flip/hover/return/travel animation, stable element ordering, selection lockout, geometry conversion, and deterministic cleanup.
- [ ] Add `Assets/Scripts/Unity/UI/CardDrawAnimator.cs` for explicit draw, restored offer, occupied-slot-delta receipt confirmation, the valid paid-discard-with-no-offer result, timeouts, per-flow modal tokens, serialized cancel/wait restoration, HUD sorting-order ownership, suppression, and generation-safe cleanup.
- [ ] Add a narrow `CountryInfoView.EnsureActionsOpen` helper, then wire the draw coordinator in `Assets/Scripts/Unity/UI/HUDDocument.cs`: inject `ModalState`, pass the existing `UIDocument`, centralize idempotent country-action event subscription across `Start`/`OnEnable`/`OnDisable`, guard it against `CardPlayAnimator`, feed state/locale changes, await old-flow cancellation before opening the Actions slide/restoring pending offers, and cancel on disable.
- [ ] Refactor `Assets/Scripts/Unity/UI/CardPlayAnimator.cs`: remove the migrated country paid-discard sequence and both country automatic-replacement lookup/travel blocks; retain an unconditional final country-hand refresh plus org-card replacement, country play/test/deck effects, result barriers, modal cleanup, and completion notification.
- [ ] Run the Release build after the `src/` projection/test change so Unity-consumed Core DLLs are refreshed, then refresh Unity after script/UXML/USS/localization changes and fix every compiler/import error. Confirm no scene edit or DI registration is necessary because `HUDDocument` already references `HUD.uxml` and owns the plain helper classes.

## User Steps

### 1. Verify the draw controls and layouts in Unity Editor

Open `Assets/Scenes/Map.unity`, enter Play Mode, open the selected country's Actions panel, and verify the localized Draw control plus `Hand: N/8` label at common aspect ratios. Check enabled, full-hand disabled, pending-offer disabled, and no-drawable-card disabled states.

### 2. Verify the choice animation and interruption matrix

Exercise one-, two-, and three-choice offers. Confirm 0.25-second sequential deal/return, 0.2-second sequential flips, exact 0.2-second 130% hover in/out, first-click lockout, return order, 0.3-second travel to the actual gapped hand slot, and no click-through to HUD/map or nonterminal higher-order windows. Repeat while already paused, after switching locale, after rapid HUD disable/re-enable, and after loading a save with a pending offer.

### 3. Verify paid discard and normal play continuity

Paid-discard a country card and confirm its existing card-to-deck travel continues directly into the modal offer without an unlocked or unpaused frame; confirm the discarded card is absent from choices. Also create the Part A edge case where no other card is drawable and confirm the discard succeeds, the vacancy renders, and no choice is fabricated. Play a normal country card and confirm no automatic replacement animation follows. Confirm org-card replacement animation is unchanged.

## Tests

- Run `refresh_unity`, wait for compilation/import to finish, and read Unity console errors after each C#/UXML/USS/localization batch. This environment cannot perform that Editor verification, so implementation must report it as skipped if UnityMCP is unavailable.
- Run the `dotnet-build Release` workflow after the `src/Game.Main` projection/test change and run the full .NET test suite. Add focused assertions that an offered unplayable card carries the same playable-country projection required by its face and that draw-choice notification/equality observes changes to that list.
- Manually verify Draw emits one command only on primary release inside its bounds, emits no command when disabled or released outside, and pushes the action before pause; verify pointer controls, Escape menu opening, and space/number time shortcuts are all blocked while the offer is modal.
- Manually verify one-, two-, and three-choice layouts, face/reveal order, hover duration/scale/z-order, single-selection lock, unselected return order, stable-identity receipt, lowest-free-slot destination, and live hand-label updates.
- Manually verify explicit draw timeout, rejected receipt recovery, already-paused completion, rapid HUD disable/re-enable with no stale unlock/unpause/sorting cleanup, restored pending offer, paid-discard continuity, successful paid discard with no offer, and that only a flow-owned pause is removed.
- Manually trigger/leave open representative higher-order nonterminal UI (fly text and an available modal window) during a pending offer and confirm the raised HUD draw shield stays on top, blocks it, then restores the HUD's exact original sorting order so the underlying surface becomes visible afterward. Separately confirm End Game at order 1100 remains the terminal exception above the draw shield.
- Verify the new `ModalState` keyboard policy on draw and one existing non-draw modal: space/number shortcuts do nothing, Escape cannot open a second game menu, and normal shortcuts resume after every modal owner releases.
- Manually verify ordinary country play has no replacement draw while the legacy org-card replacement sequence remains intact.

## Constitution Check

No conflicts found. Part A remains the sole ECS/gameplay authority under `src/`; the only `src/` change is read-only visual projection of already-computed playable-country data. The new Unity classes hold only temporary presentation timing, input, sorting, and cleanup state. UI remains UI Toolkit/UXML/USS, with no Canvas or UGUI. `HUDDocument` continues as the VContainer-resolved binding MonoBehaviour and constructs per-view helpers using injected dependencies, matching existing `CountryInfoView`/`ActionLogView` practice; no singleton, service locator, `FindObjectOfType`, or scene registration is introduced. All Unity files stay in the existing `GS.Unity.UI` feature assembly, and implementation remains gated on approval of this plan.

Use `/implement B` to implement this plan or request changes.
