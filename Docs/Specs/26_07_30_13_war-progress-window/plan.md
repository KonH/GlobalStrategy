# Plan: War Progress Window

## Spec

Source: `Docs/Specs/26_07_30_13_war-progress-window/spec.md` (approved; owner
clarifications baked in).

Open a modal war-progress window from an existing HUD war icon. Show live
`[-100, 100]` progress with attacker-red / defender-blue fills, an effects list of
actually applied history entries (not unused rule text), side statistics, and a
scrollable battle list ordered oldest→newest (pinned to the newest). Migrate war
progress off the `WarProgress` component onto a war-owned `war_progress` resource
with optional savable `ResourceHistory`. Active-battle progress uses the troop-balance
formula (0 when both empty). Finished rows keep attacker-first / defender-second
casualties with side coloring.

Out of scope: HUD icon creation/routing, player battle orders, multi-country wars,
animation/audio.

## Goal

Ship a read-only UI Toolkit modal that projects selected-war ECS state through
`src/Game.Main`, and migrate progress mutation (monthly decay + battle finish) onto
the resource/effect/history path so the window’s effects list and HUD icons share one
source of truth.

## Approach

### 1. War-owned `war_progress` resource + history

- Add `OwnerType.War` and `ResourceSeedTarget.War`.
- Add `ResourceDefinitions.WarProgress = "war_progress"` and a
  `resource_config.json` entry: `SeedTarget: War`, `DefaultInitialValue: 0`,
  `RecordHistory: true`. Add `ResourceDefinition.RecordHistory` (default `false`);
  only `war_progress` enables it. Do not seed at `InitSystem` time — wars create the
  entity themselves (`FindResources(War)` is unused by init loops).
- Add `[Savable] ResourceHistory` composed on the same entity as `Resource` +
  `ResourceOwner`, holding `List<ResourceChangeEntry> History`. Each entry stores
  `EffectId`, signed `AppliedDelta`, and game `DateTime Timestamp`.
- **Persistence:** `SaveSystem.SerializeValue` / `LoadSystem.DeserializeValue` do not
  support `List<>`. Extend both to handle `List<ResourceChangeEntry>` only: encode
  each entry as `{effectId}\x1E{delta:R}\x1E{timestamp:O}`, join with `\x1F` (same
  separator family as existing `string[]`). Put encode/decode helpers next to
  `ResourceChangeEntry`. Empty list ↔ empty string. Update
  `SavableDiscoveryTests` to expect `ResourceHistory`.
- `Wars.DeclareWar`: stop adding `WarProgress`; create a separate resource entity
  `ResourceOwner(warId, OwnerType.War)` + `Resource { war_progress, 0 }` + empty
  `ResourceHistory`. `Wars.StopWar`: destroy that war’s `war_progress` resource
  entity (history goes with it) along with existing war/battle cleanup.
- Remove `WarProgress` from `War.cs` and every reader/writer.

### 2. Route decay and battle finish through effect-aware mutations + history

- Extend `ResourceMutations.TryApplyClampedDelta` (and set, if needed) with an
  overload that takes `effectId`, `DateTime timestamp`, and `ResourceConfig` (or a
  `RecordHistory` flag resolved from config). After a non-zero applied delta, if
  `RecordHistory` is enabled, append to `ResourceHistory` on that resource entity.
  Callers without history keep using the existing overload (no append).
- Extend `ResourceSystem.GatherAndApply` similarly: when the applied delta is
  non-zero and the target resource’s definition has `RecordHistory`, append using
  the effect’s `EffectId` and `currentTime` (thread `ResourceConfig` +
  `currentTime` into the apply path as needed). This keeps Instant/Monthly effects
  honest if war progress ever flows through `ResourceSystem`.
- `WarSystem.Update`: on month boundary, for each `War`, apply
  `-AttackerWarProgressDecayPerMonth` via `ResourceMutations` against
  `ownerId = warId`, `resourceId = war_progress`, clamp `[-100, 100]`, with
  `effectId = "war_progress_decay"` and the boundary `currentTime`. Do not mutate a
  component.
- `WarBattleSettlement.FinishBattle`: replace direct `WarProgress` mutation with the
  same mutation helper, signed `±WarBattles.BattleProgressGain`, clamp
  `[-100, 100]`, `effectId = "war_progress_battle_{battleId}"`, timestamp =
  settlement time (pass `currentTime` into finish if not already available).
- Direct progress writes that skip the effect/history overload are forbidden for
  `war_progress`. Other resources stay on the existing mutation path.

### 3. Battle creation order

- Add savable `DateTime CreatedAt` to `Battle`. Thread `currentTime` into
  `WarBattleFill.FillSlots` and set `CreatedAt` at creation.
- Change `WarBattles.GetBattles` sort to `CreatedAt` ascending, then
  `BattleId` ordinal — not lexical `BattleId` alone.

### 4. `SelectedWarState` projection (`src/Game.Main`)

- Add `SelectedWarState` / related immutable entry types on `VisualState` (mirror
  `SelectedCountryState`): selected `WarId`, validity, current progress, history
  entries (oldest→newest), attacker/defender country ids + stats (recruits,
  troops-in-active-battles, war casualties, damage, durability), and ordered battle
  rows (active vs finished payloads).
- Add `SelectedWarProjector` (or methods on `VisualStateConverter`) using
  `WarBattles.GetParticipants` / `GetBattles` / `GetForces`, `ResourceQuery` for
  country resources and war `war_progress`/`ResourceHistory`. Stats: sum
  `BattleState.Active` force troops per side; sum finished-battle force
  `Casualties` per side. Active-row progress:
  `both troops == 0 ? 0 : clamp(100 * (att - def) / (att + def), -100, 100)`.
- `WarProgressWindowDocument.Open(warId)` sets selection; `Hide` clears it.
  `VisualStateConverter` refreshes `SelectedWar` every tick while a war is
  selected (Leaderboard-style live refresh; document no-ops when not visible).
- Update `WarIconsProjector` to read `ResourceQuery.GetValue(world, warId,
  ResourceDefinitions.WarProgress)` instead of the `War`+`WarProgress` archetype
  (OwnerType is ignored by query today; `warId` uniqueness is enough).

### 5. UI Toolkit modal

- Add `Assets/UI/Modal/WarProgressWindow/WarProgressWindow.uxml` + `.uss`. Progress
  bar = two opposing `VisualElement` fills over `[-100, 100]` (no shared slider).
  Add `.gs-color-attacker` (red) and `.gs-color-defender` (blue) to
  `SharedStyles.uss`.
- Implement `WarProgressWindowDocument` like `LeaderboardWindowDocument`:
  `[RequireComponent(typeof(UIDocument))]`, `sortingOrder ≈ 510`, `ModalState`,
  `btn-close` via `PointerUpEvent` + `ContainsPoint`, hide on Awake. Add plain
  `WarProgressWindowView` that only binds projected state (no ECS, no commands).
- Header: reuse `hud.war.title_format`. Add EN+RU keys for section labels, effect
  row templates (decay vs battle), battle active/finished formats, empty battle
  state — use the localization skill for real Russian.
- Battle `ScrollView`: rebuild on refresh; pin to bottom with a one-shot
  `GeometryChangedEvent` after layout. Finished row:
  `Battle at {province} ({winner}, -{attCas} / -{defCas})` with winner name and each
  casualty side-colored; casualty order always attacker then defender. Active row:
  `Battle at {province} [{progress}] ({attTroops} vs {defTroops})` with side colors.
- Scene: attach `UIDocument` on existing `WarProgressWindowUI` in `Map.unity`,
  assign UXML + `HUDPanelSettings` (copy Leaderboard/Settings wiring). DI
  registration and HUD `Open(warId)` forwarding already exist.

## Agent Steps

- [ ] **Add war owner / seed / definition** — `OwnerType.War`,
  `ResourceSeedTarget.War`, `ResourceDefinitions.WarProgress`,
  `ResourceDefinition.RecordHistory` (default false), and `war_progress` in
  `resource_config.json` with `RecordHistory: true`.

- [ ] **Add ResourceHistory + save/load encoding** — `ResourceChangeEntry` +
  `[Savable] ResourceHistory`; extend `SaveSystem`/`LoadSystem` for
  `List<ResourceChangeEntry>`; update `SavableDiscoveryTests`.

- [ ] **History-aware ResourceMutations / ResourceSystem** — append clamped
  applied deltas when `RecordHistory` is enabled; keep existing overloads
  history-free.

- [ ] **Migrate DeclareWar / StopWar** — create/destroy war-owned
  `war_progress` + `ResourceHistory`; remove `WarProgress` component usage.

- [ ] **Route decay and FinishBattle** — `WarSystem.Update` and
  `WarBattleSettlement.FinishBattle` apply via effect-id mutations with
  `[-100, 100]` clamp; distinct decay vs battle effect ids.

- [ ] **Battle CreatedAt + sort** — persist `Battle.CreatedAt`; sort
  `WarBattles.GetBattles` by creation time.

- [ ] **SelectedWarState + projector** — VisualState types, converter refresh
  while selected, `WarIconsProjector` resource read; equality so identical
  projections do not raise `PropertyChanged`.

- [ ] **Modal UXML/USS/view/document** — WarProgressWindow assets, SharedStyles
  attacker/defender colors, document+view following Leaderboard modal pattern,
  localization keys (EN + real RU).

- [ ] **Scene UIDocument wiring** — add `UIDocument` to `WarProgressWindowUI` in
  `Map.unity` (Unity MCP if available; otherwise documented YAML fallback) and
  require a clean console after import.

- [ ] **Update tests + validate** — migrate/extend tests listed below; run
  `dotnet test src/GlobalStrategy.Core.sln` and Release build for plugin DLLs.

## User Steps

These steps require Unity Editor scene/asset work, visual inspection in the Editor,
or other hands-on Unity steps.

### 1. Confirm scene UIDocument wiring

Open `Map.unity`, select `WarProgressWindowUI`, and verify it has `UIDocument` with
the WarProgressWindow UXML and the same `HUDPanelSettings` as other modals. Enter
Play mode and confirm no missing-panel / null-root errors in the console.

### 2. Open / close and map interaction

With a player-relevant war icon visible, click it and confirm the modal opens above
map/HUD, header uses localized attacker/defender names via `hud.war.title_format`,
and the top-right close button restores map/HUD interaction without changing the war.

### 3. Progress bar and live updates

Confirm the dual-fill bar covers `[-100, 100]` (attacker red from left, defender blue
from right). Advance time or finish a battle while the window stays open and confirm
progress and the effects list update in place; with no history yet the effects list is
empty.

### 4. Statistics and battle list

Confirm side-by-side recruits / troops-in-battles / casualties / damage / durability
(including `0` when empty), oldest→newest battles scrolled to the newest, empty-state
copy when there are no battles, and correct active vs finished row text/colors
(attacker-first casualties). Start or finish a battle while open and confirm the list
updates and stays pinned to the newest row.

## Tests

- `src/Game.Tests/WarsTests.cs` — declare creates war-owned `war_progress` at 0 with
  empty history; stop destroys resource+history; no `WarProgress` component remains.
- `src/Game.Tests/WarSystemTests.cs` — monthly decay applies through the resource,
  clamps to `-100`, and appends a decay history entry with the expected signed delta.
- `src/Game.Tests/WarBattleSystemTests.cs` / settlement coverage — finish applies
  ±`BattleProgressGain` to the resource, clamps to `±100`, appends a battle history
  entry; no `WarProgress` reads/writes.
- `src/Game.Tests/ResourceMutationsTests.cs` — RecordHistory on/off; zero applied
  delta does not append; clamp produces the actual applied amount in history.
- `src/Game.Tests/SaveLoadRoundTripTests.cs` — war with progress history and battles
  with `CreatedAt` survive round-trip; load does not re-apply finished settlement.
- `src/Game.Tests/WarIconsProjectorTests.cs` — progress sourced from the war-owned
  resource.
- `src/Game.Tests/SelectedWarProjectorTests.cs` (new) — empty history; chronological
  history; side stats (zeros); battle order by `CreatedAt`; active progress formula
  including both-troops-zero → `0`; finished casualty attacker-first payload;
  clearing selection invalidates state; identical `Set` does not notify.
- Full suite: `dotnet test src/GlobalStrategy.Core.sln`. UI layout/pointer/scroll
  pinning covered by User Steps (no Unity UI test harness).

## Constitution Check

Checked against `Docs/Constitution.md`.

No conflicts found — plan aligns with all principles.

- **Rendering** — no RP/shader/material changes.
- **ECS game logic** — progress, history, battles, and mutations stay under `src/`;
  Unity documents/views only bind projected state and emit `Open`/`Hide`.
- **VContainer** — existing hierarchy registration of `WarProgressWindowDocument` is
  reused; no ad-hoc service locators.
- **UI Toolkit only** — UXML/USS + document/view pair; no Canvas/uGUI.
- **Plan / spec discipline** — colocated under
  `Docs/Specs/26_07_30_13_war-progress-window/` after the approved spec.
- **File organisation / assemblies** — UI stays in `Assets/Scripts/Unity/UI`; core
  types in existing `src` projects; no new asmdef.
- **C# style** — tabs, braces, `_` private fields, no redundant access modifiers.

Use the implement skill to start working on the plan or request changes.
