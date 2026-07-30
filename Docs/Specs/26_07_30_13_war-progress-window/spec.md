# Spec: War Progress Window

## Feature Intent

As a player, I want to open a dedicated window for an active war that shows its current progress, the effects driving that progress, each side's military strength, and a scrollable battle history, so that I can understand how a war I care about is unfolding beyond the compact HUD icon.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

- A war-icon button relevant to the player is visible in the HUD (see `Docs/Specs/26_07_29_16_war-icons/spec.md`).
  - The player clicks that button => the war progress window opens for that button's `WarId`, on top of the map and HUD.
  - The window is open and the player clicks its close button in the top-right corner => the window closes and map/HUD interaction is restored, without ending or otherwise changing the war.
- The war progress window is open for a war.
  - The window is shown => its header reads "`<AttackerCountryName>` - `<DefenderCountryName>` War", using each country's localized display name.
  - The header is shown => a close button sits in its top-right corner, matching the corner placement and interaction pattern of the existing Leaderboard window's close button.
- The war progress window is open for a war.
  - The window is shown => a horizontal war progress slider spans the war's full `[-100, 100]` range, with the attacker's share filled from the left in red and the defender's share filled from the right in blue, positioned at the war's current progress value.
  - The war's progress value changes while the window is open => the slider updates to the new position without requiring the window to be closed and reopened.
- The war progress window is open for a war.
  - The window is shown => a war progress effects list explains what is currently moving the slider: one row for the configured monthly decay that favors the defender, and one row for the configured per-battle progress swing awarded to a battle's winning side.
- The war progress window is open for a war.
  - The window is shown => an attacker/defender stats block displays, side by side for each side: recruits currently available, troops currently committed to active battles, cumulative casualties suffered in this war, and the side's current damage and durability values.
  - One or both sides currently have zero recruits, zero troops in active battles, or zero cumulative casualties => the corresponding value reads as `0` rather than being hidden.
- The war progress window is open for a war.
  - The window is shown => a battles list appears below the stats block, scrolled so the most recently started battle is visible at the bottom.
  - The war has no battles yet (freshly declared, no active-battle slot filled) => the battles list is empty and shows an empty-state message instead of rows.
  - A new battle starts or an existing battle finishes while the window stays open => the list updates in place and the view remains scrolled to the newest entry.
- The battles list renders a `Finished` battle.
  - The row is shown => it reads "Battle at `<ProvinceName>` (`<WinnerCountryName>`)" followed by that battle's attacker-side casualties and defender-side casualties, each prefixed with a minus sign.
  - The winning side is the attacker => the winner name and the winner's own casualties figure are shown in red; the defender's casualties figure is shown in blue.
  - The winning side is the defender => the winner name and the winner's own casualties figure are shown in blue; the attacker's casualties figure is shown in red.
- The battles list renders an `Active` battle.
  - The row is shown => it reads "Battle at `<ProvinceName>`" followed by a mini progress indicator on the same `[-100, 100]` scale as the war progress slider, and the battle's current attacker troop count versus defender troop count, attacker figure in red and defender figure in blue.
  - Neither side has any battle troops left in that battle (evaluated the instant a round leaves it that way, before settlement removes the row) => the mini indicator still renders using the defined zero-troops fallback rather than dividing by zero.

## Tech Notes

- **Dependency — HUD entry point**: `Docs/Specs/26_07_29_16_war-icons/spec.md` (issue #75, PR #90, not yet merged to `main`) already adds the clickable HUD war-icon row and an intentionally empty `WarProgressWindowDocument.Open(string warId)` shell (`Assets/Scripts/Unity/UI/WarProgressWindowDocument.cs` on `feature/war-icons`), DI-registered and wired from `HUDDocument`'s `warId => _warProgressWindow?.Open(warId)` callback. This feature fills in that shell's implementation; it must not re-invent the HUD button, the tooltip, or the `WarId` click contract. Implementation of this plan needs PR #90 merged (or its branch integrated) first, the same way `war-progress-logic`'s plan named issue #71 as a prerequisite.
- **Dependency — war/battle data model**: `Docs/Specs/26_07_29_16_war-progress/` (issue #80, merged to `main`) already provides everything this window reads:
  - `War { WarId }`, `WarProgress { Value }` (composed on the war entity), `WarParticipant { WarId, Kind, CountryId }` — `src/Game.Components/War.cs`.
  - `WarBattleCapacity`, `Battle { BattleId, WarId, TargetProvinceId, State, Winner }`, `BattleForce { BattleId, CountryId, Side, Troops, Casualties }` — `src/Game.Components/WarBattle.cs`. `BattleForce.Casualties` is already scoped to its own battle (force rows are created fresh per battle at zero), so a finished battle's row is exactly that battle's casualties — no extra accumulation logic is needed there.
  - Query helpers already exist in `src/Game.Systems/WarBattles.cs`: `GetParticipants`, `GetBattles(world, warId, state?)`, `GetForces(world, battleId)`.
  - Country `recruits`/`damage`/`durability` are existing `Resource` values read via `ResourceQuery.GetValue(world, countryId, ResourceDefinitions.*)` (`src/Game.Systems/ResourceQuery.cs`, `src/Game.Configs/ResourceDefinitions.cs`).
  - Monthly decay: `GameSettings.AttackerWarProgressDecayPerMonth` (`src/Game.Configs/GameSettings.cs`), applied by `WarSystem.Update`. Per-battle progress swing: `GameSettings.WarBattles.BattleProgressGain` (default `10`), applied in `WarBattleSettlement`.
  - Province display names use the existing `province_name.{ProvinceId}` localization key (see `ProvinceInfoView.cs` line 62), not a new lookup.
- **Presentation projection (`src/Game.Main`)**: add a `SelectedWarState`-shaped `INotifyPropertyChanged` state (naming and shape to match `SelectedCountryState`/`SelectedProvinceState`) holding: `WarId`, `IsValid`/open flag, attacker/defender country ids and names, current `Progress`, per-side recruits/troops-in-active-battles/cumulative-casualties/damage/durability, and an ordered list of battle rows (`BattleId`, `ProvinceId`, `State`, `Winner`, per-side troops, per-side casualties). Populate it from `WarBattles` query helpers plus `ResourceQuery` when `WarProgressWindowDocument.Open(warId)` is called — this is push-once-on-open content, not a continuously-projected `VisualState` branch like `WarIconsState`, but it still needs to refresh live while the window stays open (see the "updates while open" acceptance rows), so wire it the same way `LeaderboardWindowDocument` refreshes from `PropertyChanged` while `IsVisible`.
- **Window shell**: implement `WarProgressWindowDocument` (currently an empty `MonoBehaviour` stub) following `LeaderboardWindowDocument`'s exact pattern: `Awake` wires `btn-close` via `PointerUpEvent` + `ContainsPoint` (never `Button.clicked`/`ClickEvent`), `Show()`/`Hide()` toggle `ModalState.IsModalOpen` and `DisplayStyle`, sorting order below `FlyTextNotifierDocument`'s `1000` and clear of `LeaderboardWindowDocument`'s `500` (e.g. `510`). Add a `WarProgressWindowView` plain view class under `Assets/Scripts/Unity/UI/` owning `Refresh(SelectedWarState)`.
- **UXML/USS**: add `Assets/UI/Modal/WarProgressWindow/WarProgressWindow.uxml` + `.uss`, composed the same way as `Assets/UI/Modal/LeaderboardWindow/`: `gs-blackfade` backdrop, `gs-panel` body, `gs-title`-styled header label, `btn-close` (`gs-btn`, "X", top-right), a `ScrollView` for the battles list. Reuse `SharedStyles.uss` for all typography/color/panel styling; only layout-specific rules belong in the feature USS.
- **Progress slider and mini battle indicators**: no existing slider/bar component exists in the project to reuse (checked `Assets/Scripts/Unity/UI` and `Assets/UI`); build it as two `VisualElement` fill bars (one red growing from center-left, one blue growing from center-right) inside a fixed-width track, driven by `style.width` percentages computed from the `[-100, 100]` value in C# — no new shared UI Toolkit control is needed for a first version.
- **Red/blue side coloring**: `SharedStyles.uss` currently has no blue color utility (only `.gs-color-positive` green / `.gs-color-negative` red, per `.claude/rules/unity/uitoolkit.md`'s class catalogue). Add `.gs-color-attacker` (red, may reuse `.gs-color-negative`'s value) and `.gs-color-defender` (new blue) to `SharedStyles.uss` so this window and any future war UI share one definition instead of hard-coding colors per feature USS file.
- **Localization**: reuse the existing `hud.war.title_format` (`"{0} - {1} War"`) key from the war-icons feature for the header — do not add a second header format key. Add new keys (both `en.asset` and `ru.asset`, via the `localization` skill) for: section labels (progress, effects, recruits available, troops in battles, casualties, damage, durability, battles list, empty-battles message), the decay/battle-progress effect row templates, and the finished/active battle row templates.
- **Battle list refresh and scroll pinning**: rebuild the `ScrollView`'s rows on each refresh (small, bounded list per war — no incremental diff needed, unlike `ActionLogView`'s animated log). After adding rows, set `scrollOffset` to the bottom once layout is available — per the documented `worldBound`-is-zero-until-layout gotcha, do this from a one-shot `GeometryChangedEvent` handler rather than immediately after `Add`.

## Out of Scope

- The HUD war-icon row, its tooltip, and its click routing — delivered by `Docs/Specs/26_07_29_16_war-icons/` (issue #75 / PR #90); this feature only implements what that shell hands off to.
- Any change to war declaration, battle simulation, progress calculation, decay, or occupation resolution — this window is read-only and must not push any command that mutates war/battle state.
- Player-issued battle orders, war negotiation, or a way to stop a war from this window (the existing debug-only stop path is unaffected).
- Multi-country wars (more than one attacker or one defender per side) — the current core model is one country per side; this window shows exactly one attacker and one defender, matching the current data model. See ambiguity note below.
- Animations, sound, or fly-text notifications tied to opening the window, the slider moving, or new battles appearing.
- A persisted, decomposed history of exactly how much of the current `WarProgress.Value` came from decay versus from battle results (see ambiguity below) — the effects list explains the two rules, not a running per-source ledger, unless the owner selects the option that adds one.

## Ambiguities

- [NEEDS CLARIFICATION: The "list of war progress effects (decay applied, total battle results effects)" line reads as if it should show *totals*, but `WarProgress.Value` is a single running number with no persisted breakdown of how much came from monthly decay versus from battle outcomes — there is no `GameLog`/history entry for either today. Two options: **(A, assumed above)** show the two *rules* driving progress (the configured decay rate, e.g. "Decay: -2.5/month toward defender", and the configured battle swing, e.g. "Battle win: ±10 progress") as static descriptive rows, no new persisted state; **(B)** add new persisted per-war counters (e.g. `CumulativeDecayApplied`, `CumulativeBattleProgress`) to `WarProgress` so the window can show actual running totals for each source, which changes the core war-progress data model from issue #80's already-merged shape.]
- [NEEDS CLARIFICATION: An `Active` battle has no persisted "progress" value — only current per-side troop counts. The spec above assumes the `[-100, 100]` mini indicator for a non-finished battle is computed live as the troop-count balance, e.g. `clamp(100 * (attackerTroops - defenderTroops) / (attackerTroops + defenderTroops), -100, 100)`, with `0` used when both sides have `0` troops. Please confirm this formula, or provide the intended one, since "N_troops vs M_troops colored" in the issue does not itself specify how the bracketed range is derived from those two numbers.]
- [NEEDS CLARIFICATION: For a finished battle's "`-N_troops_casualties`, `/` `-M_troops_casualties`" line, the spec assumes a fixed attacker-first, defender-second order (matching the header's "A - B" order and the slider's red-left/blue-right convention) regardless of which side won. Please confirm, since the issue text does not state whether the winner's casualties are always listed first instead.]
