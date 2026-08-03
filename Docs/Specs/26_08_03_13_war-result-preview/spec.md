# Spec: War Result Preview

## Feature Intent

As a player viewing a Declare War or Revenge country action card, I want a win-probability badge (1–99%) on the card front before I start the war, so that I can judge whether the potential war countries' recruits and combat strength favor the side that would declare.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

- A "Declare War" instance naming a rival is in the selected country's hand (playable or unplayable).
  - The player opens that country's Actions panel => a win-probability badge is visible on the card front image, immediately below the header bar and aligned to the right, showing an integer percent in the inclusive range 1–99.
  - The badge value is from the declaring country's perspective (the selected country that would become the attacker against the named rival) => higher percent means that country is more likely to win the war that card would start; 1 means effectively no chance to win; 99 means effectively no chance to lose.
- A "Revenge" instance naming a prior winner is in the selected country's hand (playable or unplayable).
  - The player opens that country's Actions panel => the same badge placement and 1–99 integer display appear on that card's front.
  - The badge value is from the revenge-declaring country's perspective (the selected country that would become the attacker against the named target) => same 1 = no win chance / 99 = no lose chance meaning.
- The player views any other country action card that is not Declare War and not Revenge (e.g. Make Friend, Sell Arms, Ultimatum).
  - The Actions panel shows that card => no win-probability badge appears on it.
- The relative combat inputs of the two potential war countries change while a Declare War or Revenge card remains in hand (recruits and/or skill-driven damage/durability for either side).
  - The Actions panel refreshes / rebinds => the badge percent updates to match the new estimation; it is not frozen at draw time.
- The two potential war countries have equal estimated combat strength under the preview formula.
  - The badge is shown => it displays 50% (still inside 1–99; equal forces are not forced to 1 or 99).
- The deck pile for the country Actions panel is shown (card backs only).
  - The player looks at the deck pile => no win-probability badge is required on the face-down deck stack.
- A Declare War or Revenge card is shown during the ordinary draw / play card transition animation that rebuilds a front-facing card copy.
  - The animated front copy is visible => it carries the same badge and current percent as the hand card would, so the preview is not hand-only chrome that vanishes mid-animation.

## Tech Notes

- **Surfaces that show Declare War / Revenge fronts today:**
  - Country Actions hand: `Assets/Scripts/Unity/UI/CountryActionsView.cs` builds each hand card via `ActionCardBuilder.Build` (`Assets/Scripts/Unity/UI/ActionCardBuilder.cs`). Layout order is header (`action-card-header`) → art (`action-card-art` / `action-card-art-image`) → body — matching the issue's "below card header … on card front image".
  - Styles: `Assets/UI/Overlay/OrgInfo/OrgActions.uss` (imported by `Assets/UI/HUD/CountryInfo/CountryInfo.uxml`); country-only overlays live in `Assets/UI/HUD/CountryInfo/CountryInfo.uss` (e.g. `.action-card-unplayable-reason`). An unused `.action-card-success-pct` class already exists in `OrgActions.uss` (legacy success-rate chrome from `Docs/Plans/29_icons-and-cards-visual-redesign.md` / country-action-cards plan) but is **not** currently attached by `ActionCardBuilder` — prefer a dedicated war-preview badge class (e.g. `.action-card-war-win-chance`) positioned absolutely inside `.action-card-art`, top-right under the header, rather than overloading the old footer success-% style.
  - Draw/play front copies: `CardTransitionView.ShowCountry` and `CardPlayAnimator` slot population (`Assets/Scripts/Unity/UI/CardTransitionView.cs`, `CardPlayAnimator.cs`) also call `ActionCardBuilder.Build` / `PopulateSlot` — extend the builder API so every front rebuild path can pass the percent without forking layout.
  - Out of these surfaces: `OrgActionsView` (org cards), `CharactersView` (character advisor cards), and the face-down deck pile — no badge.
- **Attacker / defender pairing (locked by existing card effects):**
  - `declare_war`: attacker = card `CountryContext.CountryId` (selected country); defender = `RelationCardTarget.TargetCountryId`. Wired in `CreateActionEffectSystem` (`DeclareWarEffectParams` branch) calling `Wars.DeclareWar(world, countryId, targetCountryId, …)` (`src/Game.Systems/CreateActionEffectSystem.cs`, `src/Game.Systems/Wars.cs`). Target already projected as `ActionCardEntry.TargetCountryId` in `VisualStateConverter.BuildEntry`.
  - `revenge`: attacker = card `CountryContext.CountryId`; defender = `RevengeCardTarget.TargetCountryId`. Wired in the `DeclareRevengeWarEffectParams` branch (same file); instances synced by `RevengeCardSyncSystem` (`src/Game.Systems/RevengeCardSyncSystem.cs`) from `RevengeEligibilityQuery` (`src/Game.Systems/RevengeEligibilityQuery.cs`). Same `ActionCardEntry.TargetCountryId` projection path when `RevengeCardTarget` is present.
  - Percent is always the **attacker's** chance to win the war that play would start (not the player's org as a separate combatant — orgs do not fight; countries do).
- **Combat inputs that real war logic already uses (mirror these, do not invent a second skill model):**
  - Recruits: country `Resource` `ResourceDefinitions.Recruits` via `ResourceQuery.GetValue` (`src/Game.Configs/ResourceDefinitions.cs`, `src/Game.Systems/ResourceQuery.cs`). Battle fill commits a random fraction of available recruits as troops (`WarBattleSystem.FillSlots` in `src/Game.Systems/WarBattleFill.cs`).
  - Damage / durability: live country resources `ResourceDefinitions.Damage` / `Durability`, already recomputed by `DamageCollector` / `DurabilityCollector` (`src/Game.Systems/DamageCollector.cs`, `DurabilityCollector.cs`) as `(base + skillA + skillB) × (1 + …bonus…)`, with skills from `WartimeSkillQuery` (`ruler`/`military_advisor` `power` for damage; `ruler`/`economic_advisor` `stinginess` for durability). Side-stat projection already reads the same bundle in `WarProgressSnapshot.BuildSideStats` (`src/Game.Systems/WarProgressSnapshot.cs`).
  - Per-round strike shape (for "looks like real war logic"): `potentialCasualties ≈ troops × damage / durability` (divisors `WarBattles.DamageDivisor` / `DurabilityDivisor` cancel in a ratio comparison) in `WarBattleSystem.Strike` (`src/Game.Systems/WarBattleRounds.cs`).
  - Existing ratio helper worth reusing as a building block: `WarProgressSnapshot.ComputeActiveBattleProgress(attackerTroops, defenderTroops)` maps a two-side troop comparison onto `[-100, 100]` — preview strength should fold recruits × offensive effectiveness (damage vs enemy durability) into comparable side scores, then map to 1–99 (exact map is Ambiguities).
- **Estimation helper (new, ECS-side, no MonoBehaviour math):**
  - Add a plain static helper under `src/Game.Systems/` (e.g. `WarWinChanceEstimator.EstimateAttackerWinPercent(IReadOnlyWorld, string attackerCountryId, string defenderCountryId, …) → int` clamped to `[1, 99]`). Keep formula pure and unit-testable beside `WarBattleSystemTests` / `PeaceChanceTests` style coverage in `src/Game.Tests/`.
  - Call it only for `actionId` `declare_war` and `revenge` when building country `ActionCardEntry` in `VisualStateConverter.BuildEntry` (`src/Game.Main/VisualStateConverter.cs`). Do not run war battle simulation / RNG / province targeting for the preview.
  - Extend `ActionCardEntry` (`src/Game.Main/VisualState.cs`) with an optional projected field (e.g. `int? WarWinChancePercent` or `int WarWinChancePercent` with `0` = absent). Update `StateEquality.ActionCardEntryEquals` (`src/Game.Main/StateEquality.cs`) so percent changes refresh the hand UI.
- **UI binding:**
  - `ActionCardBuilder.Populate` / `Build` / `PopulateSlot`: accept optional win-chance percent; when present, add a `Label` inside the art container (right-aligned, below header visually), text like `{n}%` (or a localized format key — see localization). When absent, omit the element entirely.
  - `CountryActionsView.BuildHandCard`: pass `card.WarWinChancePercent` into the builder for hand cards.
  - `CardTransitionView.ShowCountry` / `CardPlayAnimator` front rebuilds: pass the percent from the `ActionCardEntry` being animated (or re-read from current `CountryActionsState.Hand`) so transition copies stay consistent.
  - Tooltip (optional polish, not required by the issue): may mention that the value is an estimate; do not block shipping on tooltip copy.
- **Localization:**
  - If the badge is only numeric (`"42%"`), a dedicated locale key is optional; if explanatory tooltip / aria-style label text is added, add keys under e.g. `action.war_win_chance.*` in `Assets/Localization/en.asset` and `ru.asset` via the `localization` skill (real Russian, not English placeholders) per `.claude/rules/unity/localization.md`.
- **Constitution / layering:**
  - Estimation stays in `src/` ECS helpers; UI Toolkit + VContainer presentation only reads projected `VisualState` — no combat math inside MonoBehaviours (Constitution: ECS for game logic; UI Toolkit only).

## Out of Scope

- Changing real war declaration, battle rounds, peace chance, or war-result window behavior (`Wars`, `WarBattleSystem`, `Wars.ResolvePeace`, war result UI).
- Full Monte-Carlo battle simulation, initiative, province targeting, concurrent battle capacity, or random troop-commitment variance inside the preview — preview is a deterministic simple estimate only.
- Showing the badge on org action cards, character cards, war progress / war result windows, map war icons, or debug terminal output.
- Multi-country / allied wars beyond the current two-participant model.
- Bot/AI decision-making that consumes the preview percent (bots may ignore it).
- Redesigning overall card chrome beyond the win-chance badge itself.
- Replacing or removing the unused legacy `.action-card-success-pct` class unless convenient while adding the new badge styles.

## Ambiguities

- [NEEDS CLARIFICATION: Exact formula mapping attacker/defender recruits + damage/durability into an integer 1–99%. Proposed default if unanswered: sideStrength = recruits × damage / max(enemyDurability, ε); winFraction = attackerStrength / (attackerStrength + defenderStrength); percent = clamp(round(winFraction × 100), 1, 99); equal strengths → 50. Confirm or replace.]
- [NEEDS CLARIFICATION: For Revenge, should the estimate include the pending attacker-only revenge bonuses (+10% damage / +5% durability from `DeclareRevengeWarEffectParams` / `revenge_declare_war_effect`) as if the war had already started, or only currently live `damage`/`durability` resources (bonuses apply only after play)?]
- [NEEDS CLARIFICATION: Should the estimate read live `damage`/`durability` resources (includes any already-applied `troops_damage_bonus_percent` / Sell Arms style bonuses), or recompute from config bases + character skills only, ignoring temporary wartime bonus resources?]
- [NEEDS CLARIFICATION: Visual design beyond placement — badge background/color (single neutral style vs green/yellow/red by band), font size, and whether unplayable cards dim the badge with the rest of the card or keep it fully opaque.]
- [NEEDS CLARIFICATION: Both countries have zero recruits (and/or zero effective strength after the formula). Show 50, show 1 for the attacker, or another rule?]
- [NEEDS CLARIFICATION: Must the badge appear on unplayable hand copies (assumed yes above) and on draw/play transition fronts (assumed yes above), or hand-only when the card is currently playable?]
