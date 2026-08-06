# Plan

## Spec faithful summary

For country cards only, render every play requirement in localized player language, show the countries where a card is currently playable, notify the player when an unplayable card is released, and add a one-second hold gesture that discards a card for configurable gold. Increase every card silhouette from 300px to 360px without changing width or the header/art sizing, keep face-up hand/flying/test copies consistent, and guarantee discard replacement never immediately redraws the discarded entity. Org-card behavior remains untouched apart from inheriting the shared taller silhouette.

## Goal

Make country-card availability understandable and let players deliberately replace an unwanted card through a reliable UI Toolkit gesture and an explicit ECS command pipeline.

## Approach

This plan depends on the sibling deck plan's marked country-card pool, selected-country `ActionPlayability`, structured `ActionConditionDebugEntry` locale key/arguments, structured first-failure data, and deterministic `ActionCardEntry.PlayableCountryIds`. UI code will only localize and render those contracts; it will not recreate condition-to-wording switches or compute country playability.

Add `DiscardCardCommand` and `DiscardCardSystem` as a dedicated country-card transaction. The system validates the marked card and hand slot, rechecks gold, deducts the configured cost, and adds `CardDiscard`. Run it before `CheckHandSizeSystem`; because the sibling draw queries exclude `CardDiscard`, the normal refill necessarily selects a different eligible entity. Systems share ECS data and markers only—no system calls another system.

Keep gesture state inside `CountryActionsView`: `PointerDownEvent` schedules the one-second hint, pointer movement/leave tracks bounds, and `PointerUpEvent` chooses discard, cancel, failed-discard fly text, unplayable-reason fly text, or the existing quick play. The view reports actions through callbacks supplied by `HUDDocument`; command access, localization, config, flags, tooltips, and fly text remain injected/composed there through VContainer.

## Agent Steps

- [ ] Add `DiscardGoldCost = 50` to `src/Game.Configs/GameSettings.cs` and keep `Assets/Configs/game_settings.json` and `src/Game.WebClient/wwwroot/configs/game_settings.json` synchronized; add `action.discard.hint`, `action.discard.no_gold`, and any required condition keys to `Assets/Localization/en.asset` and `Assets/Localization/ru.asset` with real Russian translations.
- [ ] Add `src/Game.Commands/DiscardCardCommand.cs` with organisation, selected country, action id, target country id, and slot identity; rely on command source generation rather than adding a parallel mutable service.
- [ ] Add `src/Game.Systems/DiscardCardSystem.cs` to validate that the addressed entity is a marked country card currently in the requested hand slot, verify and deduct gold using the resource-query idiom, and tag it `CardDiscard`; return observable failure data needed for the no-gold notification without invoking draw/hand-size systems.
- [ ] Integrate `DiscardCardSystem.Update` in `src/Game.Main/GameLogic.cs` after play-card removal and before `CheckHandSizeSystem.Update`; pass `GameSettings.DiscardGoldCost`, preserve `CheckHandSizeSystem` → relation sync → draw → discard cleanup ordering, and rely on the sibling deck plan's `CardDiscard` candidate exclusion to guarantee a different replacement.
- [ ] Add discard-focused coverage in `src/Game.Tests/DiscardCardSystemTests.cs` and `src/Game.Tests/DrawCardSystemTests.cs` for success, insufficient gold, stale slot/target rejection, country-only enforcement, exact deduction, system ordering, same-slot refill, and exclusion of the discarded entity.
- [ ] Refactor `Assets/Scripts/Unity/UI/ActionCardBuilder.cs` to accept localized `(text, passed)` requirement rows and `PlayableCountryIds`; replace the single-reason label with a non-scrolling shrink-to-fit requirements block, append affordability as a row, and build the left art badge with at most two flags plus literal `...`.
- [ ] Update layout in `Assets/UI/Overlay/OrgInfo/OrgActions.uss`: set `.action-card` to `240px × 360px`, reserve the added 60px for requirements, add only layout classes for requirement rows, badge, and discard hint, and reuse shared positive/negative and existing cost/icon classes rather than duplicating visual styles.
- [ ] Update `Assets/Scripts/Unity/UI/CountryActionsView.cs` to localize the deck plan's structured keys/arguments, pass all rows and playable-country ids into `ActionCardBuilder`, show a whole-badge tooltip with every localized `country_name.{id}`, and surface the first structured failing reason through fly text on an unplayable quick release.
- [ ] Implement the country-only hold gesture in `CountryActionsView.cs` with scheduled `PointerDownEvent`, pointer bounds tracking, and `PointerUpEvent` plus `ContainsPoint`; cancel silently off-card, short-circuit discard before play, style unaffordable hint text with the existing class, and do not add handlers to `OrgActionsView`.
- [ ] Update `Assets/Scripts/Unity/UI/CardTransitionView.cs` to use the fixed `240f × 360f` animation size and thread requirements/badge data through `ShowCountry`; update `Assets/Scripts/Unity/UI/CardPlayAnimator.cs` so both the flying card and populated country test-card copy preserve the same face data.
- [ ] Update `Assets/Scripts/Unity/UI/DebugCardAvailabilityView.cs` to resolve the same structured condition locale key/arguments as cards and fly text, keeping the raw technical label only as optional secondary debug detail.
- [ ] Update `Assets/Scripts/Unity/UI/HUDDocument.cs` and `Assets/Scripts/Unity/UI/CountryInfoView.cs` construction/callback wiring so `CountryActionsView` receives localization, country visuals, tooltip access, discard cost, discard/play callbacks, and fly-text notification through existing injected dependencies; push `DiscardCardCommand` only from the document callback.
- [ ] Update equality/projection fixtures affected by UI-consumed state in `src/Game.Tests/ActionCardEntryEqualityTests.cs`, `src/Game.Tests/ActionConditionDebugTests.cs`, and relevant `VisualStateConverter*Tests.cs`; verify all three face-up card call sites consume identical requirement and badge data.

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
- Verify org cards gain only the shared height and receive no requirements, playable-country badge, failed-play fly text, or discard gesture.

## Constitution Check

No conflicts found — plan aligns with all principles.

Use the implement skill to start working on the plan or request changes.
