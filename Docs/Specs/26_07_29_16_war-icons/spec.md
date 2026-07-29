# Spec: War Icons

## Feature Intent

As a player organization with control in a country participating in an active war, I want a compact war shortcut in the HUD that identifies the primary attacker and defender, shows the current war progress on hover, and navigates to that war's progress view on click, so that wars relevant to my organization remain visible and directly accessible while I use the map.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

- No active war has any participant country where the player organization has positive control.
  - The HUD is shown => no war-icon row or empty placeholder is visible above the map-lens switcher, and the existing map-lens layout and behavior remain unchanged.
- One or more active wars have at least one participant country where the player organization has positive control.
  - The HUD is shown => exactly one horizontal war-icon row is visible immediately above the map-lens switcher.
  - Several wars qualify => one button is shown per qualifying war in the same row; a war is never duplicated when the player organization has control in more than one of its participant countries.
  - A war has no participant country with positive player-organization control => that war has no button, even if other wars qualify.
- Player-organization control is evaluated for a participant country.
  - The player's control is the sum of all current `ControlEffect.Value` values for the player organization's id and that country id.
  - The sum is greater than `0` => that country makes its war relevant and eligible for the row.
  - The sum is `0` or less, or there is no matching control effect => that country does not make its war relevant.
  - Any one participant has a positive sum => the war qualifies; the attacker and defender do not both need player-organization control.
- A qualifying war has its current core-model participants (one attacker and one defender).
  - Its button is rendered => it contains the replaceable crossed-swords placeholder icon and the flags of the primary attacker and primary defender.
  - The flags are ordered attacker first, defender second.
  - A country flag cannot be resolved from `CountryVisualConfig` => the button remains present and usable, with only that missing flag hidden; the other flag and crossed-swords icon still render.
- The data model later permits multiple attackers and/or defenders.
  - A war button is rendered => it still shows exactly two country flags: the first attacker and the first defender in the projected participant order; additional participants affect relevance filtering but do not add more flags or create additional buttons.
- The player points at a qualifying war button.
  - The tooltip opens through the existing HUD tooltip system => its header is the localized label `A - B War`, where `A` is the primary attacker's localized country name and `B` is the primary defender's localized country name.
  - The tooltip content is shown => it includes the war's current numeric progress value, preserving fractional values rather than rounding them to an integer.
  - The active locale changes while the HUD is running => the country names and the localized `War`/progress text refresh to the active locale.
- The current progress of a displayed war changes.
  - The projected visual state refreshes => the next tooltip presentation shows the new value in the core model's `[-100, 100]` range; the button is not duplicated or reordered merely because progress changed.
- A war starts, stops, gains player control in a participant country, or loses its last positive player-control participant.
  - The projected visual state refreshes => the row adds or removes the corresponding button without requiring a scene reload.
  - The final qualifying war is removed => the entire row becomes hidden and no empty HUD space remains above the map lenses.
- The player releases the primary pointer over a qualifying war button.
  - The button emits a navigation/open request containing that war's `WarId` => `HUDDocument` forwards that id to the DI-injected war progress window shell rather than inferring it from country ids or row position.
  - The request is handled by the war progress window shell => its empty `Open(string warId)` implementation is invoked for the requested war; displaying window contents remains out of scope.
  - The player presses or releases outside the button, or uses a non-primary pointer button => no navigation request is emitted.

## Tech Notes

- **Dependency:** this feature consumes the active-war state introduced by `Docs/Specs/26_07_25_06_war-mechanics-core/spec.md` / issue #69:
  - `War { WarId }` and `WarProgress { Value }` share the war entity.
  - `WarParticipant { WarId, Kind, CountryId }` supplies attacker/defender rows.
  - `ControlEffect { OrgId, CountryId, Value }` supplies the player-control filter.
  - The dependency is present on `main`; this slice must not duplicate or replace the core war model.
- **Presentation projection (`src/Game.Main`):**
  - Add a `WarIconsState : INotifyPropertyChanged` to `VisualState`, containing a read-only list of immutable entries with at least `WarId`, `Progress`, primary attacker country id, and primary defender country id.
  - `VisualStateConverter` projects the state from `War`/`WarProgress`/`WarParticipant` and filters it using the currently selected player organization's aggregated `ControlEffect` values.
  - Group participants by `WarId`, order each side deterministically, choose the first attacker and first defender for the visual entry, and order entries deterministically (for example by `WarId`) so ECS archetype iteration order cannot make buttons jump between refreshes.
  - A malformed war missing either its war entity/progress or one of its two primary sides is omitted from the HUD rather than producing a broken button.
  - State equality must compare entry content so `PropertyChanged` fires only when the displayed war set or one of its displayed values changes.
- **HUD composition (UI Toolkit only):**
  - Add a `WarIcons` UXML/USS template under `Assets/UI/HUD/WarIcons/`, compose it from `Assets/UI/HUD/HUD.uxml`, and position the template instance in `HUD.uss` directly above `.lens-switcher-panel`.
  - Add a plain `WarIconsView` under `Assets/Scripts/Unity/UI/` and let the existing `HUDDocument` binding construct it, subscribe/unsubscribe to `VisualState.WarIcons.PropertyChanged`, and call `Refresh`.
  - The view owns only rendering and emits an `Action<string>`-style callback carrying `WarId`; it does not query ECS or own window state.
  - Use the project-standard `PointerUpEvent` plus primary-button and `ContainsPoint` checks; do not use `Button.clicked` or `ClickEvent`.
  - Register each button with the existing `TooltipSystem`. Tooltip content uses the shared `tooltip-header` and content/effect classes already loaded by the HUD document.
  - The dynamic row remains `flex-direction: row` with no wrapping. Since `gap` is unsupported in Unity 6000.4.1f1, add left margin only to buttons after the first.
  - Internal sizes and spacing belong in the feature USS; shared colors, borders, and typography come from `SharedStyles.uss`.
- **Button visuals:**
  - Add `Assets/Textures/Buttons/War/crossed_swords.png` as a replaceable placeholder by copying an existing imported UI image and its import settings under the established button-texture hierarchy. The owner will replace the image later without requiring code or UXML changes.
  - Reference the placeholder from the feature USS or a serialized visual config according to the chosen UI Toolkit binding.
  - Country flags reuse `CountryVisualConfig.Find(countryId)?.flag`, matching the country info, relations, and leaderboard views.
  - The icon and flags use `PickingMode.Ignore` so the parent button remains the sole pointer target.
- **Localization:**
  - Reuse `country_name.{CountryId}` for both country names.
  - Add localized HUD keys for the war-title format and progress label to both `Assets/Localization/en.asset` and `Assets/Localization/ru.asset`; do not hard-code English UI text.
  - Formatting must support insertion of two localized names without assuming that every locale uses the English word order outside its format string.
- **Progress-window handoff:**
  - The click contract is identity-based: `WarId` is the only navigation payload.
  - Add a `WarProgressWindowDocument` shell following the `LeaderboardWindowDocument` injection pattern, register it in `GameLifetimeScope`, and inject it into `HUDDocument`.
  - `HUDDocument` forwards the view callback to `WarProgressWindowDocument.Open(string warId)`.
  - `Open(string warId)` intentionally has an empty implementation in this feature. The progress window's UXML, view model, contents, controls, close behavior, modal state, and game behavior are not part of this feature.
- **Verification targets for the eventual plan:**
  - Pure-C# tests should cover relevance filtering (positive, zero, negative, multiple participant matches), de-duplication by `WarId`, deterministic attacker/defender selection and war ordering, progress refresh, malformed-war omission, and removal after war/control changes.
  - Unity-side verification should cover the one-row placement above map lenses, empty-state collapse, crossed-swords/flag composition, localized tooltip content, missing-flag fallback, primary-pointer click routing with the correct `WarId`, and no regression to lens switching.

## Out of Scope

- Implementing the war progress window itself, including its layout, progress visualization, controls, close behavior, and modal state.
- Changing war declaration, participant eligibility, monthly decay, progress calculation, war stopping, or any other core war rule.
- Displaying wars that have no participant country with positive player-organization control.
- Showing more than the primary attacker and primary defender flags on a war button, even if multi-country wars are introduced later.
- Showing participant names, progress text, or other details permanently in the HUD row; these details belong to the tooltip or the future progress window.
- Generating or redesigning the crossed-swords artwork.
- Adding map coloring, map markers, notifications, game-log entries, audio, animation, or alerts for war start/end/progress changes.
- Selecting a participant country when the war button is clicked; the click targets a war by `WarId`.
