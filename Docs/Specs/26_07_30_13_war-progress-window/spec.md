# Spec: War Progress Window

## Feature Intent

As a player, I want to open a dedicated window for an active war that shows its current progress, applied progress changes, each side's military strength, and a scrollable battle history, so that I can understand how the war is unfolding beyond the compact HUD icon.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

- A war-icon button relevant to the player is visible in the HUD.
  - The player clicks the icon => the war progress window opens for that war above the map and HUD.
  - The window is open and the player clicks its close button in the top-right corner => the window closes, map and HUD interaction is restored, and the war is unchanged.
- The war progress window is open for an active war.
  - The window is shown => its header reads "`<AttackerCountryName>` - `<DefenderCountryName>` War" using the countries' localized display names.
  - The window is shown => a close button is present in the top-right corner.
  - The war's current progress changes while the window is open => the displayed progress updates without reopening the window.
- The war progress window is open for an active war.
  - The window is shown => a horizontal progress slider covers the full `[-100, 100]` range, with attacker progress filled from the left in red and defender progress filled from the right in blue.
  - The war has no recorded progress changes yet => the effects list is empty rather than showing rule descriptions that have not been applied.
  - Monthly decay or a battle result changes the war's progress => the effects list adds the actual applied change, identifying whether it came from decay or a battle result and showing its signed applied amount.
  - Several progress changes have been applied => the effects list displays them chronologically from oldest to newest.
- The war progress window is open for an active war.
  - The window is shown => attacker and defender statistics appear side by side: recruits available, troops in active battles, cumulative casualties in this war, damage, and durability.
  - Either side has zero recruits, zero troops in active battles, or zero casualties => the applicable statistic displays `0`.
  - Any displayed statistic changes while the window is open => the affected value updates in place.
- The war progress window is open for an active war.
  - The window is shown => the battle list is ordered from oldest at the top to newest at the bottom and initially scrolled to the bottom so the recent battle is visible.
  - The war has no battles => the list displays an empty-state message instead of battle rows.
  - A battle starts or finishes while the window remains open => the list updates and stays scrolled to the newest entry.
- The battle list displays a finished battle.
  - The row is shown => it reads "Battle at `<ProvinceName>` (`<WinnerCountryName>`, `-<AttackerCasualties>` / `-<DefenderCasualties>`)".
  - The row is shown => the winner's name, attacker casualties, and defender casualties use attacker red or defender blue according to their respective sides; casualty order remains attacker first, defender second regardless of the winner.
- The battle list displays an active battle.
  - The row is shown => it reads "Battle at `<ProvinceName>` [`<Progress>`] (`<AttackerTroops>` vs `<DefenderTroops>`)", with attacker values red and defender values blue.
  - Both troop totals are zero => the active-battle progress indicator displays `0`.
  - At least one troop total is nonzero => the active-battle progress indicator displays the clamped attacker-versus-defender troop balance on the `[-100, 100]` scale.

## Tech Notes

- HUD entry and modal behaviour:
  - Fill `Assets/Scripts/Unity/UI/WarProgressWindowDocument.cs`'s existing `Open(string warId)` stub. `HUDDocument` already receives `warId` from `WarIconsView` through `warId => _warProgressWindow?.Open(warId)`, and `GameLifetimeScope` already registers the document.
  - Follow `LeaderboardWindowDocument` for modal behaviour: wire `btn-close` with `PointerUpEvent` plus `ContainsPoint`, use `ModalState`, and use a sorting order around `510`.
  - Add `Assets/UI/Modal/WarProgressWindow/WarProgressWindow.uxml` and `.uss`; build the progress bars from two `VisualElement` fills because no shared slider exists. Add reusable `.gs-color-attacker` (red) and `.gs-color-defender` (blue) utilities to `SharedStyles.uss`.
  - Reuse `hud.war.title_format` for the header. Add localized English and Russian keys for section labels, history rows, battle rows, and the empty battle state.
- Current progress and its applied-change list:
  - Add `OwnerType.War` and `ResourceSeedTarget.War`, allowing a resource to be owned directly by a war rather than by a country or organisation.
  - Add `ResourceDefinitions.WarProgress` (`"war_progress"`) and configure it as a war-owned resource clamped to `[-100, 100]`. Add `ResourceDefinition.RecordHistory`, defaulting to `false`, and enable it only for `war_progress`.
  - Add savable `ResourceHistory` on the same resource entity as `Resource` and `ResourceOwner`, containing `List<ResourceChangeEntry> history`. Each entry records a distinct effect/source id, signed applied amount, and game `DateTime` timestamp. Destroying the war-owned resource also destroys its history.
  - Migrate away from `WarProgress { double Value }` in `src/Game.Components/War.cs`: `Wars.DeclareWar` creates the war's `war_progress` resource at zero; `Wars.StopWar` destroys it with the rest of the war-owned state; readers, including `WarIconsProjector`, read the resource rather than a mirrored component.
  - Extend `ResourceSystem` and `ResourceMutations.TrySetValue` / `TryApplyClampedDelta` so a mutation resulting from a `ResourceEffect` appends the actual clamped delta to history when `RecordHistory` is enabled. Direct mutations must not bypass this effect/history path for war progress.
  - Route `WarSystem.Update` monthly decay (`GameSettings.AttackerWarProgressDecayPerMonth`) and `WarBattleSettlement.FinishBattle`'s signed `WarBattles.BattleProgressGain` through distinct resource effects. Their ids identify decay versus battle-result history rows, and history is projected oldest first.
- Statistics and battle rows:
  - Project a selected-war state in `src/Game.Main`, following `SelectedCountryState` / `SelectedProvinceState`, with current `war_progress`, resource history, participant country data, and ordered battle rows. Refresh it while the modal is visible, following the live-refresh pattern used by `LeaderboardWindowDocument`.
  - Use `WarBattles.GetParticipants`, `GetBattles`, and `GetForces` from `src/Game.Systems/WarBattles.cs`. Read country recruits, damage, durability, and war initiative through `ResourceQuery` and `ResourceDefinitions`; sum forces in `BattleState.Active` for troops in battles and all finished-battle force casualties for war casualties.
  - Use `Battle`, `BattleForce`, and `BattleState` from `src/Game.Components/WarBattle.cs` / `BattleState.cs`. Localize province names through existing `province_name.{ProvinceId}` keys and resolve the winner country from its battle side.
  - Assign each battle a persisted creation timestamp or monotonic creation sequence when it is created, then order battle rows by that field rather than lexical `BattleId`; this remains stable once ids reach multiple digits.
  - For active battles calculate `clamp(100 * (attackerTroops - defenderTroops) / (attackerTroops + defenderTroops), -100, 100)`, returning `0` when both totals are zero.
  - Rebuild the small battle `ScrollView` on refresh and pin it to the bottom using a one-shot `GeometryChangedEvent`, after layout is available.
- Read-only boundary:
  - The document and view only query/project state. They do not issue commands to stop a war, alter progress, or give battle orders.

## Out of Scope

- HUD war-icon creation, tooltip content, and click routing; this feature consumes the existing HUD handoff.
- Player-issued battle orders, war negotiation, or stopping a war from this window.
- Multi-country war presentation beyond the current one-attacker-versus-one-defender model.
- Animation, audio, or fly-text effects for opening the window, changed progress, or new battle rows.
