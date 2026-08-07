# Plan

## Spec faithful summary

For country cards only, render every play requirement in localized player language, show the countries where a card is currently playable, notify the player when an unplayable card is released, and add a one-second hold gesture that discards a card for configurable gold. Increase every card silhouette from 300px to 360px without changing width or the header/art sizing, keep face-up hand/flying/test copies consistent, and guarantee discard replacement never immediately redraws the discarded entity. Org-card behavior remains untouched apart from inheriting the shared taller silhouette.

## Goal

Make country-card availability understandable and let players deliberately replace an unwanted card through a reliable UI Toolkit gesture and an explicit ECS command pipeline.

## Approach

This plan depends on the sibling deck plan's marked country-card pool, selected-country `ActionPlayabilityResult`, full ordered structured requirement entries, `FirstFailure`, and deterministic `ActionCardEntry.PlayableCountryIds`. The result owns authored conditions, primary-country match, pool capacity, cooldown, and gold exactly once. UI code will only localize and render that contract; it will not recreate condition-to-wording switches, compute country playability, or append another affordability row.

Add `DiscardCardCommand` and `DiscardCardSystem` as a dedicated country-card transaction. The system validates the marked card and hand slot, rechecks gold, captures the slot, deducts the configured cost, removes `CardInHand`, and then adds `CardDiscard`. Run it before `CheckHandSizeSystem`; the removed hand marker creates the vacancy, and because the sibling draw queries exclude `CardDiscard`, the normal refill necessarily selects a different eligible entity in the captured slot. Systems share ECS data and markers only—no system calls another system.

Keep gesture state inside `CountryActionsView`: `PointerDownEvent` captures the pointer and retains an `IVisualElementScheduledItem` for the one-second hint, pointer movement tracks bounds, and the captured `PointerUpEvent` chooses discard, cancel, failed-discard fly text, unplayable-reason fly text, or the existing quick play even when release occurs outside the card. Release pointer capture and cancel/clear the scheduled item on quick release, off-card release, `PointerCancelEvent`, capture loss, view refresh/rebuild, and element detachment so stale callbacks cannot reveal a hint later. The view reports actions through callbacks supplied by `HUDDocument`; command access, localization, config, tooltips, and fly text remain injected/composed there through VContainer.

Compose an immutable, presentation-only country-card face model in `CountryActionsView` from `ActionCardEntry` and its injected dependencies. It carries localized requirement rows and resolved `(countryId, flag sprite)` badge items. Pass the same face model through the slot-aware click callback to the hand builder, flying card, and populated test-card copy; compose a fresh model for the replacement card after refresh. Do not use static/global asset lookup inside builders or animation views.

## Agent Steps

- [ ] Add `DiscardGoldCost = 50` to `src/Game.Configs/GameSettings.cs` and keep `Assets/Configs/game_settings.json` and `src/Game.WebClient/wwwroot/configs/game_settings.json` synchronized; add `action.discard.hint`, `action.discard.no_gold`, and any required condition keys to `Assets/Localization/en.asset` and `Assets/Localization/ru.asset` with real Russian translations.
- [ ] Add `src/Game.Commands/DiscardCardCommand.cs` with organisation, selected country, action id, target country id, and slot identity; rely on command source generation rather than adding a parallel mutable service.
- [ ] Add `src/Game.Systems/DiscardCardSystem.cs` to validate that the addressed entity is a marked country card currently in the requested hand slot, recheck gold using the resource-query idiom, capture the slot, deduct the exact cost, remove `CardInHand`, and then add `CardDiscard`; return observable failure data needed for the no-gold notification without invoking draw/hand-size systems.
- [ ] Integrate `DiscardCardSystem.Update` in `src/Game.Main/GameLogic.cs` after play-card removal and before `CheckHandSizeSystem.Update`; pass `GameSettings.DiscardGoldCost`, preserve `CheckHandSizeSystem` → relation sync → draw → discard cleanup ordering, and rely on the explicit hand vacancy plus the sibling deck plan's `CardDiscard` candidate exclusion to refill the same slot with a different card.
- [ ] Add discard-focused coverage in `src/Game.Tests/DiscardCardSystemTests.cs` and `src/Game.Tests/DrawCardSystemTests.cs` for success, insufficient gold, stale slot/target rejection, country-only enforcement, exact deduction, `CardInHand` removal before `CardDiscard`, system ordering, same-slot refill, and exclusion of the discarded entity.
- [ ] Refactor `Assets/Scripts/Unity/UI/ActionCardBuilder.cs` to accept localized `(text, passed)` requirement rows and resolved `(countryId, flag sprite)` badge items; replace the single-reason label with a non-scrolling shrink-to-fit requirements block, render the canonical rows without appending affordability, and build the left art badge with at most two flags plus literal `...`.
- [ ] Update layout in `Assets/UI/Overlay/OrgInfo/OrgActions.uss`: set `.action-card` to `240px × 360px`, reserve the added 60px for requirements, add only layout classes for requirement rows, badge, and discard hint, and reuse shared positive/negative and existing cost/icon classes rather than duplicating visual styles.
- [ ] Update `Assets/Scripts/Unity/UI/CountryActionsView.cs` to localize the canonical structured keys/arguments, derive overall availability and cost styling from the projected `CanPlay`/gold entry rather than recomputing affordability, render all result rows exactly once, and compose immutable face data by resolving `PlayableCountryIds` through injected `CountryVisualConfig` into `(countryId, flag sprite)` badge items. Show a whole-badge tooltip with every localized `country_name.{id}` and surface `FirstFailure` through fly text on an unplayable quick release.
- [ ] Implement the country-only hold gesture in `CountryActionsView.cs` with pointer capture on `PointerDownEvent`, a retained `IVisualElementScheduledItem`, pointer bounds tracking, and captured `PointerUpEvent` plus `ContainsPoint`; release capture and cancel/clear the schedule on quick release, off-card release, `PointerCancelEvent`, `PointerCaptureOutEvent`, refresh/rebuild, and detachment; hide the hint deterministically, short-circuit discard before play, style unaffordable hint text with the existing class, and do not add handlers to `OrgActionsView`.
- [ ] Update `Assets/Scripts/Unity/UI/CardTransitionView.cs` to use the fixed `240f × 360f` animation size and accept immutable face data through `ShowCountry`; update `Assets/Scripts/Unity/UI/CardPlayAnimator.cs` so both the flying card and populated country test-card copy receive that same face data, while the replacement draw uses freshly composed data for its slot, with no static/global country-asset lookup.
- [ ] Update `Assets/Scripts/Unity/UI/DebugCardAvailabilityView.cs` to resolve the same structured condition locale key/arguments as cards and fly text, keeping the raw technical label only as optional secondary debug detail.
- [ ] Update `Assets/Scripts/Unity/UI/HUDDocument.cs`, `Assets/Scripts/Unity/UI/CountryInfoView.cs`, `CountryActionsView.OnCardClicked`, and `CardPlayAnimator.StartCountryCardPlay` construction/callback wiring so country-card play carries `ActionCardEntry.SlotIndex` and immutable face data end to end, `PlayCardActionCommand.SlotIndex` is populated, and `CountryActionsView` receives localization, country visuals, tooltip access, discard cost, discard/play callbacks, and fly-text notification through existing injected dependencies. Push `DiscardCardCommand` only from the document callback.
- [ ] Update equality/projection fixtures affected by UI-consumed state in `src/Game.Tests/ActionCardEntryEqualityTests.cs`, `src/Game.Tests/ActionConditionDebugTests.cs`, and relevant `VisualStateConverter*Tests.cs`; verify the canonical requirements contain each gate exactly once and all three face-up card call sites consume identical requirement and resolved badge data.

## User Steps

### 1. Validate card layout in Unity

Open the game HUD at representative resolutions and inspect cards with short, long, passing, and failing requirement lists. Confirm hand, deck pile, flying card, and fixed test card are all 240px × 360px; header/art/description retain their visual sizing; rows shrink rather than scroll or overflow; and left playable-country and right war badges do not overlap.

### 2. Validate badge and tooltip behavior

Inspect cards with zero, one, two, and more than two playable countries. Confirm deterministic flags, the literal ellipsis, localized full-country tooltip contents, and identical badge data on flying/test copies.

### 3. Validate discard and failed-play gestures

Quick-release playable and unplayable country cards, then hold for at least one second while affordable and unaffordable, both on-card and after moving off-card. Confirm play remains unchanged, localized failure fly text appears, the hint price/style is correct, successful discard deducts gold and draws a different card into the same slot, and no country-card interaction is accepted during the existing play animation.

## Tests

- Run the repository `dotnet build` workflow and full `dotnet test` suite, including the new discard system/integration cases and structured-condition equality tests.
- In Unity, enter Play Mode and perform the three manual checks above in both English and Russian; watch the Console for UI Toolkit, missing-locale, command, or DI errors.
- Verify the hold uses scheduled UI Toolkit events (not `Button.clicked`/`ClickEvent`), hidden hints do not intercept input, and tooltip content remains within the HUD panel after geometry layout.
- Verify pointer capture guarantees off-card release delivery and every pending hold schedule is cancelled on quick/off-card release, pointer cancellation/capture loss, refresh/rebuild, and detachment, with no stale hint callbacks.
- Verify cards, debug view, and failed-play fly text render the canonical playability entries without duplicated gold or omitted synthetic gates, and flying/test copies receive resolved flag sprites through injected composition.
- Verify org cards gain only the shared height and receive no requirements, playable-country badge, failed-play fly text, or discard gesture.

## Constitution Check

No conflicts found — plan aligns with all principles.

Use the implement skill to start working on the plan or request changes.
