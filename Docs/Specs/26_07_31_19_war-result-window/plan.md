# Plan: War Result Window

## Spec

Source: `Docs/Specs/26_07_31_19_war-result-window/spec.md` (approved; owner
clarifications baked in).

When a war peaces with a winner/loser, emit an enriched one-shot snapshot, always
write the short action-log line under v1 defaults, and — when the player org has
strictly greater than zero control in either participant — pause and open a sibling
`WarResultWindow` that freezes the war-progress layout plus winner label and spoils
(gold / control / provinces). Multiple same-tick resolutions queue FIFO. Debug
`StopWar` at progress `0` stays ignored (no `WarResolvedApplied`). Notification
gates use the conditions system (`ExpressionNode`) via config flags + conditions —
not a hard-coded influence bypass.

Depends on war progress logic (#80, done) and the existing war progress window UI
on main.

Acceptance criteria (condensed):
- **Influence gate** — player org control `> 0` in attacker **or** defender → pause
  + open result window; no influence → no pause/window from this feature; action log
  still written.
- **Window chrome** — same layout as war progress (header via `hud.war.title_format`,
  dual-fill progress, effects, side stats, battles) + gold-colored
  `` `<Winner>` won! `` label + results block (gold taken/distribution, control
  deltas, transferred provinces). Empty/zero spoils still show sections, not crash
  or invent rows.
- **Close** — hide modal, restore map/HUD, `UnpauseCommand` if this feature paused.
- **FIFO** — several influence-gated resolutions in one tick → one window after
  another; sim stays paused until the last owned result closes.
- **Config** — event-notification list with `Pause` / `ShowWindow` / `WriteActionLog`
  (default `true`) + `ExpressionNode` conditions per behaviour; war_resolved v1 uses
  influence conditions for pause/window and always-true / null for action log.
- **Progress-0 StopWar** — out of scope; no notification path required.

## Goal

Ship a config-driven event-notification path for war resolved that enriches the ECS
one-shot before `DestroyWar`, projects a FIFO result queue through `VisualState`, and
opens a read-only UI Toolkit sibling of the war progress window — pausing only when
conditions say so — without changing peace math or the live progress-window click flow.

## Approach

### 1. Enrich `WarResolvedApplied` snapshot before `DestroyWar`

Today `Wars.ResolvePeace` (`src/Game.Systems/Wars.cs`) applies provinces → occupation
clear → gold → control → `DestroyWar`, then creates a bare
`WarResolvedApplied { WinnerCountryId, LoserCountryId }`
(`src/Game.Components/GameLogEffects.cs`). Progress-`0` still early-returns with no
event (leave unchanged).

**Assembly boundary (explicit):**

| Layer | Assembly | Owns |
|---|---|---|
| Snapshot DTOs on the one-shot | `Game.Components` | Plain structs/lists on `WarResolvedApplied` (and any nested snapshot record types next to it) — **no** `GS.Main` types |
| Builders | `Game.Systems` | `WarProgressSnapshot` + peace mutator return accumulators; may take `CountryConfig` from `Game.Configs` — **must not** reference `GS.Main` / `VisualState` / `SelectedWar*` |
| Projection | `Game.Main` | Map component DTOs → `WarProgressHistoryEntryState` / `WarSideStatsState` / `WarBattleRowState` / `WarResultSnapshotState`; `SelectedWarProjector` becomes a thin mapper over `WarProgressSnapshot` |

**Expand `WarResolvedApplied`** (same entity / cleanup path) with identity + spoils +
frozen progress UI payload (all fields are `Game.Components` DTOs):

| Field group | Contents |
|---|---|
| Identity | `WarId`, `AttackerCountryId`, `DefenderCountryId`, `WinnerCountryId`, `LoserCountryId`, `Progress` (final pre-destroy) |
| Gold | `GoldTaken` (`D × G`, may be `0`); `GoldRecipients[]` of `{ OwnerType, OwnerId, Amount }` — **payout side only** (winner orgs + country remainder); when `GoldTaken == 0`, array empty |
| Control | `ControlDeltas[]` of `{ CountryId, OrgId, Delta, TotalAfter }` for winner boosts and loser cuts actually applied (`Delta == 0` omitted); empty when nothing shifted |
| Provinces | `TransferredProvinceIds[]` (ordered as transferred); empty when none |
| Progress UI freeze | History rows (`EffectId`, `AppliedDelta`, `Timestamp`), attacker/defender side-stat snapshots (same scalars the live progress UI projects — country id, recruits, troops-in-battles, casualties, damage, durability + tooltip bases/bonuses including `CountryEntry.BaseDamage` / `BaseDurability`), battle rows (same field set as live battle rows) |

Refactor silent mutators to return/accumulate results:

- `TransferOccupiedProvinces` → `List<string>` of transferred ids (empty on early exit).
- `TransferGoldSpoils` / `CollectGoldFromSide` / `PayoutGoldToSide` → total taken + list of payout recipients (and optionally collect-side for debugging; UI only needs payout + total taken). When `amount == 0`, return zeros/empty without calling adjust.
- `ApplyControlShifts` / winner-boost / loser-cut helpers → list of applied deltas with post-shift totals (read total after each apply via `ControlQuery.GetOrgControlInCountry`).

**Thread `CountryConfig` for side-stat bases:** `SelectedWarProjector.BuildSideStats`
today uses `countryConfig?.FindByCountryId(...)?.BaseDamage` / `BaseDurability`
(fallback `40` only when config/entry is missing). The freeze must match the live
progress UI, so thread `CountryConfig` from `GameLogic` through
`Wars.StopWar` → `Wars.TryResolvePeaceByChance` → `Wars.ResolvePeace` into
`WarProgressSnapshot` side-stat builders. Update all call sites (GameLogic peace
chance + debug StopWar) and tests that invoke those signatures. Do **not** hardcode
`40` when a real `CountryConfig` is available.

**Capture progress freeze before `DestroyWar`:** extract shared read helpers from
`SelectedWarProjector` (`BuildHistory` / `BuildSideStats` / `BuildBattles` /
`ComputeActiveBattleProgress`) into `src/Game.Systems/WarProgressSnapshot.cs`
returning **component** DTOs (not `GS.Main` state types). `SelectedWarProjector`
maps those DTOs into VisualState types. `ResolvePeace` order:

1. `TryGetWarState` (existing).
2. Progress `== 0` → clear occupation + destroy; **no** `WarResolvedApplied` (unchanged).
3. Winner/loser by sign.
4. Build progress freeze via `WarProgressSnapshot` (war still alive; pass `CountryConfig`).
5. Transfer provinces → clear occupation → gold → control (collect return values).
6. `DestroyWar`.
7. Create entity + full `WarResolvedApplied`.

Keep sweeping via `CleanupEffectNotificationsSystem.UpdateWarResolved` (still called
at the start of the next tick before the next peace chance pass). No change to
sweep timing; only the component payload grows (managed lists on the struct are
already precedent via `ResourceHistory`).

### 2. Event notification config (`GameSettings` + `game_settings.json`)

Add nested settings (prefer a dedicated object on `GameSettings` for clarity):

```csharp
// GameSettings
public EventNotificationSettings EventNotifications { get; set; } = new();

public class EventNotificationSettings {
  public List<EventNotificationEntry> Events { get; set; } = /* war_resolved default */;
}

public class EventNotificationEntry {
  public string EventType { get; set; } = "";           // "war_resolved"
  public bool Pause { get; set; } = true;
  public bool ShowWindow { get; set; } = true;
  public bool WriteActionLog { get; set; } = true;
  public ExpressionNode? PauseCondition { get; set; }
  public ExpressionNode? ShowWindowCondition { get; set; }
  public ExpressionNode? WriteActionLogCondition { get; set; } // null = always true
}
```

JSON key: `eventNotifications.events[]` with camelCase booleans + nested
`ExpressionNode` objects (`type` / `value` / `members`), same shape as action
`conditions` in `ActionDefinition`.

**v1 `war_resolved` defaults** (also the C# property initializers so missing JSON
still behaves):

- Flags all `true`.
- `pauseCondition` / `showWindowCondition`:

```json
{
  "type": "gt",
  "members": [
    { "type": "control" },
    { "type": "value", "value": 0 }
  ]
}
```

- `writeActionLogCondition`: omit / `null` → `ExpressionNode.Evaluate` already
  returns `1.0` for null (always pass).

Other event types are not shipped; the list shape must allow future entries without
schema rewrite.

### 3. Condition evaluation for participant OR (influence)

Existing `ExpressionContext.Control` is single-country (`CountryActionConditionContext`
sets it from `ControlQuery.GetOrgControlInCountry`). There is no `or` / `max` leaf
today. **Do not hard-code influence outside ExpressionNode.**

**Concrete approach — OR-eval across participants:**

Add `EventNotificationConditionContext` in `src/Game.Systems/` (or beside the
evaluator in `src/Game.Main`):

```text
Passes(world, playerOrgId, participantCountryIds, ExpressionNode? condition) :
  if condition is null → true
  for each countryId in participantCountryIds:
    ctx = ExpressionContext { Control = GetOrgControlInCountry(world, playerOrgId, countryId) }
    if ExpressionNode.Evaluate(condition, ctx) != 0 → true
  return false
```

For war resolved, `participantCountryIds = [AttackerCountryId, DefenderCountryId]`.
The config expression stays the normal `gt(control, 0)` card-condition style; the
dispatcher supplies per-country context and ORs. No new ExpressionNode leaf required
for v1. (Optional later: a dedicated leaf if designers want single-eval max — not
needed now.)

Player org id = `VisualState.PlayerOrganization` / the same id
`VisualStateConverter` already uses when gating other log kinds.

### 4. Notification dispatcher + VisualState queue + action log

**Evaluate in `src/Game.Main` (has `World` + `GameSettings`), drive UI from projected
state** — keeps ControlQuery/ExpressionNode off MonoBehaviours while matching the
spec’s “react to `WarResolvedApplied`, not inside `ResolvePeace`” boundary.
`Game.Main` is also where component snapshot DTOs are mapped into VisualState types
(see §1 assembly boundary).

- Add `EventNotificationDispatcher` (static helper or small class) that, given world,
  settings, player org id, and one `WarResolvedApplied`:
  1. Look up entry by `EventType == "war_resolved"` (if missing, treat as all flags
     true + v1 conditions above).
  2. `writeLog = entry.WriteActionLog && Passes(..., WriteActionLogCondition)` —
     participants unused for null condition.
  3. `show = entry.ShowWindow && Passes(..., ShowWindowCondition, [attacker, defender])`.
  4. `pause = show && entry.Pause && Passes(..., PauseCondition, [attacker, defender])`
     (pause only meaningful when a window will open; still evaluate PauseCondition so
     config can diverge later).
  5. Return a decision + mapped `WarResultSnapshotState` for the queue when `show`.

- **`VisualState.WarResults` (new `INotifyPropertyChanged` queue state):**
  - FIFO list of immutable snapshot states (progress freeze + spoils + winner/loser
    ids + `ShouldPause` bool).
  - `Enqueue` from converter (stable order = archetype / entity creation order for
    that tick’s `WarResolvedApplied` batch — same order `ResolvePeace` ran).
  - `TryPeek` / `AcknowledgeCurrent` for the Unity host (close pops front; if more
    remain, host opens next without unpausing when any remaining item wanted pause).

- **`VisualStateConverter.UpdateGameLog`:** stop unconditionally appending every
  `WarResolvedApplied`. Route through the dispatcher’s `writeLog` decision. Under v1
  defaults this remains always-on and still ignores `GameLogSettings.IncludePlayerActions`
  (same as today’s war-resolved path). Keep
  `GameLogEntryKind.WarResolved` + short `BuildWarResolvedLine` /
  `game_log.war_resolved_format` — do **not** dump spoils into the log.

- Same converter tick that reads `WarResolvedApplied` for the log also enqueues show
  decisions onto `WarResults` (one pass over the archetype).

### 5. Sibling `WarResultWindow` UI (maximum reuse)

Mirror war progress under new names — do **not** reopen `WarProgressWindow` against a
dead `SelectedWar` selection:

| Asset / type | Path |
|---|---|
| UXML / USS | `Assets/UI/Modal/WarResultWindow/WarResultWindow.uxml` + `.uss` |
| Document | `Assets/Scripts/Unity/UI/WarResultWindowDocument.cs` |
| View | `Assets/Scripts/Unity/UI/WarResultWindowView.cs` |

Reuse:

- Import `SharedStyles.uss` + share attacker/defender utility classes; either
  `@import` / duplicate-link `WarProgressWindow.uss` selectors that are layout-generic
  or extract shared rules into a small shared USS partial — avoid forking panel chrome.
- Extract bind helpers from `WarProgressWindowView` (progress fills, effects columns,
  side stats, battles rebuild + pin-to-newest, **tooltip wiring**) into an internal
  shared binder (e.g. `WarProgressLayoutBinder`) that accepts the same projected field
  shapes (`progress`, history, sides, battles) plus a `TooltipSystem`. Both views call
  it.
- Header: `hud.war.title_format` with attacker/defender localized names.
- `sortingOrder ≈ 510` (same band as war progress), `ModalState` ownership flag,
  `btn-close` via `PointerUpEvent` + `ContainsPoint`, hide on Awake.

**Tooltip parity with war progress:** `WarResultWindowDocument` must mirror
`WarProgressWindowDocument`: create `TooltipSystem` on the UIDocument root in
`Awake`, pass it into the view/binder, and call `_tooltip.Update(Time.deltaTime)`
from `Update`. Side-stat and effect-row tooltips stay functional on the frozen
snapshot.

Additions only on the result window:

- Winner label under the header: locale `war_result.winner_format` (`"{0} won!"`).
  Color via shared utility **`.gs-color-gold`** added to
  `Assets/UI/Shared/SharedStyles.uss` and documented in
  `.claude/rules/unity/uitoolkit.md` (same pattern as `.gs-color-attacker` /
  `.gs-color-defender`). Feature USS (`WarResultWindow.uss`) stays **layout-only** —
  do not invent a feature-local color class such as `.war-result-winner` for the gold
  text.
- Results section after the shared progress layout: gold taken + per-recipient
  distribution (org names via `organization_name.*`, country remainder via
  `country_name.*`); control deltas; province list via `province_name.{ProvinceId}`.
  Empty/zero states use dedicated locale strings (no invented province rows).
- **Gold amount formatting:** display every gold quantity (`GoldTaken`, each
  recipient `Amount`, and any zero/empty copy that embeds a number) with
  invariant `:F1` (one decimal place), matching the project UI gold-display rule.
  Do not use integer truncation or default `G` formatting for gold in this window.

**Document behaviour (pause ownership):**

- Subscribes to `VisualState.WarResults` (+ locale). When not visible and queue
  non-empty → `OpenCurrent()`: set modal, show root, bind snapshot.
- If the snapshot’s `ShouldPause` is true **and** `!_state.Time.IsPaused` before
  pushing: push `PauseCommand` via `IWriteOnlyCommandAccessor` and set
  `_issuedPause = true`. If the sim is **already** paused, still show the modal /
  take `ModalState`, but **do not** claim pause ownership (`_issuedPause` stays
  false) and **do not** push `PauseCommand`.
- Close / `Hide`: clear modal ownership, hide; if `_issuedPause` and queue will be
  empty after acknowledge → `UnpauseCommand` and clear `_issuedPause`; if more items
  remain, acknowledge, bind next, and keep `_issuedPause` (sim stays paused) until
  the last owned result closes. If `_issuedPause` was never set (already paused on
  open), close must **not** push `UnpauseCommand`.
- Read-only: no war/stop/battle commands.

Register `WarResultWindowDocument` in `GameLifetimeScope` like
`WarProgressWindowDocument`. Prefer the document self-driving from `WarResults`
(EndGame-style) rather than HUD click forwarding; HUD inject only if needed for
lifetime ordering.

**Localization:** add EN keys under `war_result.*` (winner format, section titles,
empty gold/control/provinces copy, distribution row formats). At implement time use
the **localization** skill for real Russian in `ru.asset` — do not leave English
placeholders in RU.

### 6. Pause / modal ownership notes

- War progress sets `ModalState` but does **not** auto-pause — leave that alone.
- War result pushes `PauseCommand` only when `ShouldPause` and the sim was not
  already paused (`!_state.Time.IsPaused`); `_issuedPause` tracks that claim only.
- On close, only unpause if this feature issued the pause (`_issuedPause`) —
  never clear an unrelated / pre-existing pause.
- Opening a result while war progress is open: acceptable to stack modal ownership;
  result close restores interaction; if progress was open underneath, existing
  progress document’s `_ownsModalState` may already be false after a competing
  modal — keep behaviour simple: result window is the notification path; do not
  redesign progress modal stacking in this feature.

### 7. Progress-0 / StopWar

No behavioural change to the progress-`0` early return (still no
`WarResolvedApplied`). Signature updates that thread `CountryConfig` through
`StopWar` / `TryResolvePeaceByChance` / `ResolvePeace` still apply to those call
sites; the progress-`0` path simply ignores config for snapshot purposes. Tests
assert still no `WarResolvedApplied`, no queue enqueue, no pause from this feature.

## Agent Steps

- [ ] **Enrich `WarResolvedApplied` + component snapshot DTOs** — expand
  `src/Game.Components/GameLogEffects.cs` (or sibling file) with identity, gold,
  control, province, and frozen progress/battle/history DTO fields; keep struct
  usable as a one-shot ECS component with managed lists; **no** `GS.Main` types in
  `Game.Components` or `Game.Systems`.

- [ ] **Extract `WarProgressSnapshot` in `Game.Systems`** — move history/side-stats/
  battle builders out of `SelectedWarProjector` into `WarProgressSnapshot`; accept
  `CountryConfig` for base damage/durability (same fallbacks as live UI); retarget
  the projector in `Game.Main` to map component DTOs → VisualState types only.

- [ ] **Thread `CountryConfig` through peace entry points** — extend
  `Wars.StopWar` / `TryResolvePeaceByChance` / `ResolvePeace` signatures; pass
  config from `GameLogic` (and update tests) so freeze side stats match live
  progress UI.

- [ ] **Return values from peace mutators** — refactor
  `TransferOccupiedProvinces`, gold collect/payout, and control shift helpers to
  accumulate transferred ids / gold taken + recipients / control deltas; wire into
  `ResolvePeace` snapshot emission; progress-`0` path unchanged.

- [ ] **Add event notification config** — `EventNotificationSettings` /
  `EventNotificationEntry` on `GameSettings`; ship `eventNotifications` in
  `Assets/Configs/game_settings.json` with war_resolved defaults (flags true +
  `gt(control,0)` pause/show conditions + null write-log condition).

- [ ] **Condition OR-eval helper** — `EventNotificationConditionContext` /
  `Passes(...)` using per-participant `ExpressionContext.Control` and
  `ExpressionNode.Evaluate`; unit-test influence in neither / one / both.

- [ ] **Dispatcher + `WarResults` queue + log gating** — evaluate decisions in
  `Game.Main`; map component snapshots → `WarResultSnapshotState`; project FIFO
  `VisualState.WarResults`; gate `UpdateGameLog` war-resolved lines on
  `WriteActionLog` decision; preserve short log format.

- [ ] **Shared progress layout binder + tooltips** — extract reusable bind helpers
  from `WarProgressWindowView` (including `TooltipSystem` usage); keep progress
  window behaviour identical; result document mirrors progress tooltip lifecycle
  (`TooltipSystem` on root, pass into binder, `Update` tick).

- [ ] **Shared `.gs-color-gold` utility** — add to `Assets/UI/Shared/SharedStyles.uss`
  and document in `.claude/rules/unity/uitoolkit.md`; winner label uses that class;
  `WarResultWindow.uss` stays layout-only.

- [ ] **WarResultWindow UXML/USS/view/document** — sibling modal under
  `Assets/UI/Modal/WarResultWindow/`; winner label + results block; gold amounts
  formatted `:F1`; pause only when `ShouldPause && !_state.Time.IsPaused` (claim
  `_issuedPause` only then); unpause on last close only if `_issuedPause`; FIFO
  consume from `WarResults`; VContainer registration in `GameLifetimeScope`.

- [ ] **Localization keys** — EN entries for all new `war_result.*` strings; run
  localization skill for real RU translations.

- [ ] **Scene UIDocument wiring** — add `WarResultWindowUI` + `UIDocument` in
  `Map.unity` (Unity MCP if available; otherwise documented YAML fallback) matching
  other modals’ `HUDPanelSettings`.

- [ ] **Tests + validate** — see Tests; run `dotnet test src/GlobalStrategy.Core.sln`
  and Release build for plugin DLLs per workflow.

## User Steps

These steps require Unity Editor scene/asset work, visual inspection in the Editor,
or other hands-on Unity steps.

### 1. Confirm WarResultWindow scene wiring

Open `Map.unity`, select the new `WarResultWindowUI` (or equivalent), and verify it
has `UIDocument` with the WarResultWindow UXML and the same `HUDPanelSettings` as
`WarProgressWindowUI` / other modals. Enter Play mode and confirm no missing-panel /
null-root errors in the console.

### 2. Influence-gated open / pause / close

With the player org holding control `> 0` in a participant and the sim running
(unpaused), force a peace resolution (debug StopWar with non-zero progress, or wait
for chance). Confirm the game pauses, the result modal opens above map/HUD, gold
“`{Winner}` won!” label (`.gs-color-gold`) appears under the header, progress/
effects/stats/battles (including tooltips) match the pre-peace war, gold amounts
show one decimal place, and Close restores map interaction and unpauses. Separately,
if the sim is already paused when a result opens, confirm Close does **not** unpause.

### 3. No-influence path

With the player org at `0` control in both participants, resolve a war. Confirm no
result window and no pause from this feature, but the short war-resolved action-log
line still appears.

### 4. Spoils empty states and FIFO

Resolve a same-month / no-eligible-province peace (zeros/empty lists) and confirm
gold/control/province sections render empty/zero copy (gold still `:F1` where a
number is shown) without missing chrome. Resolve two influence-gated wars in one
tick (or back-to-back before dismiss) and confirm FIFO: first window, dismiss,
second window, dismiss, then unpause (when this feature owned the pause).

## Tests

- **`PeaceResolutionTests.cs` (update)** — successful peace populates enriched
  `WarResolvedApplied` (attacker/defender/progress, gold taken/recipients, control
  deltas, transferred provinces, non-empty freeze when history/battles exist);
  side-stat bases reflect threaded `CountryConfig` (not hardcoded `40` when config
  supplies bases); progress-`0` still emits no component; existing winner/loser
  assertions remain; call sites updated for `CountryConfig` parameter.
- **`WarsTests.cs` / gold-control helpers** — zero gold → `GoldTaken == 0` and empty
  recipients; payout remainder to country recorded; control no-ops → empty deltas;
  province none → empty id list; StopWar/`ResolvePeace` signatures accept
  `CountryConfig`.
- **`EventNotificationConditionTests.cs` (new)** — null condition → pass;
  `gt(control,0)` fails when both participants have player control `0`; passes when
  either has `> 0`; does not require control in both.
- **`EventNotificationDispatcherTests.cs` (new)** — war_resolved defaults: write log
  always; show+pause only with influence; flags false suppress window/pause/log
  independently; missing config entry falls back safely.
- **`VisualStateConverter` / game-log tests** — war resolved still produces short
  `GameLogEntryKind.WarResolved` under defaults; with `WriteActionLog: false` in a
  test settings override, no log entry; `WarResults` queue enqueues only show
  decisions in ResolvePeace order; acknowledge drains FIFO.
- **`SelectedWarProjectorTests.cs`** — still green after helper extraction /
  DTO→VisualState mapping (behaviour unchanged for live wars; bases still come from
  `CountryConfig`).
- Full suite: `dotnet test src/GlobalStrategy.Core.sln`. Modal layout, `.gs-color-gold`,
  tooltips, `:F1` gold display, and Play Mode pause-ownership feel covered by User
  Steps (no Unity UI test harness).

## Constitution Check

Checked against `Docs/Constitution.md`.

No conflicts found — plan aligns with all principles.

- **Rendering** — no RP/shader/material changes.
- **ECS game logic** — peace math, snapshot emission, and condition evaluation inputs
  stay under `src/`; Unity documents/views bind projected state and emit
  Pause/Unpause/close only. `Game.Systems` does not reference `GS.Main`.
- **VContainer** — register `WarResultWindowDocument` in `GameLifetimeScope`; no ad-hoc
  service locators.
- **UI Toolkit only** — UXML/USS + document/view pair; shared `.gs-color-gold` in
  SharedStyles; no Canvas/uGUI.
- **Plan / spec discipline** — colocated under
  `Docs/Specs/26_07_31_19_war-result-window/` after the approved spec.
- **File organisation / assemblies** — UI in `Assets/Scripts/Unity/UI`; component DTOs
  in `Game.Components`, builders in `Game.Systems`, VisualState mapping in
  `Game.Main`; no new asmdef.
- **C# style** — tabs, braces, `_` private fields, no redundant access modifiers.

Use the implement skill to start working on the plan or request changes.
