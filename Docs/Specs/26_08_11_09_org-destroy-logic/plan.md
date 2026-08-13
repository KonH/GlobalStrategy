# Plan: Org Destroy Logic

## Spec

Source: `Docs/Specs/26_08_11_09_org-destroy-logic/spec.md` (approved; owner
clarifications baked in — no war-survival guard, immediate player-loss ending,
`OrganizationGameOutcome.Result` flips immediately, hand/card checks generic
over `CardOwnerKind`, destroyed-org per-tick skip in scope).

When an org holds zero total control everywhere, has no control card in hand
(any `CardOwnerKind` pool), a full hand, every hand card unplayable (ignoring a
cooldown-only block), and no gold to discard, mark it destroyed with a
persistent `IsOrgDestroyed` flag (entity kept), flip its
`OrganizationGameOutcome.Result` to `Loser` immediately, purge its residual
control, exclude it from goal/win eligibility, skip it in bot decision-tick,
and emit a one-shot destroy event for `VisualState` FIFO consumption. Add a new
`LastOrgStandingCondition` win condition (sole non-destroyed org wins); reduce
the shipped `completionCondition.members` to `ScoreGoalCondition` (lowered to
`50000`) + `LastOrgStandingCondition` only, dropping `TotalControlCondition`
and `FullControlCondition` from the active config. When the player's own org
is destroyed, let normal completion first declare the sole surviving org the
`LastOrgStanding` winner when exactly one remains. Otherwise end the session
immediately as a player loss; zero survivors produces a completed game with no
winner, as does the multiple-survivor fallback when no configured condition is
met.

**Companion UI** (`Docs/Specs/26_08_11_09_org-destroy-ui/`) is out of scope for
window/document/view work — but this plan **does** own the `VisualState`
projection layer (`Game.Main`, no MonoBehaviour/UI Toolkit) the UI plan
consumes, mirroring how `26_08_07_08_country-destroy-logic/plan.md` §5 owned
`CountryDestroyedResultsState`/`WorldCountriesState.DestroyedCountryIds` for
the country-destroy UI to consume. Concretely this plan adds:
`VisualState.OrgDestroyedResults` (FIFO, mirrors `CountryDestroyedResults`) and
a `PlayerOrganizationState.IsDestroyed` projection (mirrors
`WorldCountriesState.DestroyedCountryIds`, but scoped to the single org
`OrgInfoDocument` cares about). The UI plan only builds the window
document/view + `EndGameWindow` sequencing fix + `OrgInfoDocument` consumption
of `IsDestroyed`. This plan also owns converter ordering: publish
`OrgDestroyedResults` before `GameCompletion` in the same `Update` pass so the
UI consumer can acquire `ModalState` before completion observers run.

Acceptance criteria (condensed):
- **Destroy conditions** — zero total control (`OrgMetrics.GetTotalControl ==
  0`) is a hard gate; when true, additionally require: no control card in any
  `CardOwnerKind` pool's hand; every pool's hand full; every hand card
  unplayable ignoring a cooldown-only block; gold below `DiscardGoldCost`.
  Country-owned cards are evaluated across every available non-destroyed
  country target and block destruction if any target is playable after
  ignoring cooldown as the sole failure; org-owned cards use their proper org
  context. All conditions must hold simultaneously. No war-survival guard.
- **Flag + event** — `[Savable] IsOrgDestroyed` tag added (entity kept); one-shot
  `OrgDestroyedApplied { OrganizationId }` created same tick, swept **next**
  tick via `CleanupEffectNotificationsSystem`, idempotent (`Has<IsOrgDestroyed>`
  guard, mirrors `CountryDestroySystem`).
- **Cleanup** — residual `ControlEffect` entities for that org destroyed;
  `OrganizationGameOutcome.Result = Loser` set immediately; org skipped
  entirely in bot decision-tick.
- **Goals** — destroyed orgs excluded from `GoalsProjector.Build`'s per-org
  loop and from `GameCompletionSystem`'s win-candidate evaluation (but not from
  `GetParticipants`'s `Result`-setting sweep).
- **New win condition** — `LastOrgStandingCondition`: met when exactly one org
  remains non-destroyed and it is the org being evaluated; zero survivors does
  not satisfy it.
- **Config** — `completionCondition.members` becomes exactly `score_goal`
  (`50000`) + `last_org_standing`; `total_control`/`full_control_countries`
  removed from shipped config (classes may remain unused).
- **Player-loss ending** — when the player's own org is destroyed and it is not
  already completed by a configured condition, `GameCompletion.IsCompleted =
  true` with `WinnerOrganizationId` left empty. Zero survivors is therefore a
  no-winner player loss; exactly one survivor is selected first by
  `LastOrgStandingCondition`; surviving orgs in a no-winner ending stay
  `InProgress`.
- **Presentation** — add `LastOrgStanding` to config parsing/factory/domain and
  to the pre-game hint/runtime-goals projections. Prefer runtime progress as
  `destroyed opponents / total opponents`.

## Goal

Ship domain destroy logic under `src/` so control/hand/gold evaluation →
destroy → completion → converter ordering is correct each tick, goals/bot-tick
stay consistent, a destroyed org cannot subsequently win, exactly one surviving
org wins outright, the player's own destruction (outside last-standing) ends
the session as a loss, and the UI plan only binds FIFO/flag projections already
established here (no additional destroy-math ECS in Part B).

## Approach

### 1. Components (`Game.Components`)

| Component | Savable | Shape | Lifecycle |
|---|---|---|---|
| `IsOrgDestroyed` | yes | empty tag on org entity (mirrors `IsDestroyed`) | permanent for session; survives save/load |
| `OrgDestroyedApplied` | no | `{ string OrganizationId }` on **own** entity | created on destroy; read by converter same tick; swept **next** tick |

Add `IsOrgDestroyed.cs`; put `OrgDestroyedApplied` beside other one-shots (near
`CountryDestroyedApplied`). Do not delete the org entity or `Organization`
component.

### 2. Shared classify/query helpers (`Game.Systems`) — move out of `Game.Bots`

`Game.Bots.BotObservation.ClassifyRaisesControl` (`src/Game.Bots/BotObservation.cs:72-79`,
currently `private static`) duplicates exactly the check the destroy condition
needs ("does this action's configured effects include a positive
`ControlChangeEffectParams`"). `Game.Systems` has no reference to `Game.Bots`
(one-way dependency confirmed: `Game.Bots` → `Game.Systems`), so:

- Add `public static bool RaisesControl(ActionDefinition? def, EffectConfig
  effectConfig)` to a new small `Game.Systems/ActionEffectClassifier.cs` (same
  body as `ClassifyRaisesControl`).
- Replace `BotObservation.ClassifyRaisesControl`'s body with a call to it (or
  delete it and call the new helper at both existing call sites in
  `BotObservation.cs`), so the logic is not duplicated across assemblies.

**Generic hand/pool query** — `CountryCardDrawQuery` (`src/Game.Systems/CountryCardDrawQuery.cs`)
is hardcoded to `CardOwnerKind.Country` throughout (`TryGetStatus`,
`CountHandCards`, `FindDeckEntity`, all filter `owners[i].Value ==
CardOwnerKind.Country`). Add a small new `Game.Systems/OrgDestroyHandQuery.cs`
(or extend `CountryCardDrawQuery` with an owner-kind parameter — either is
acceptable, prefer a new file to avoid touching the country-specific class'
existing call sites) exposing, per `CardOwnerKind`:

```text
GetHandCardEntities(IReadOnlyWorld world, string orgId, CardOwnerKind kind) → IEnumerable<int>
TryGetHandSize(IReadOnlyWorld world, string orgId, CardOwnerKind kind, out int handCount, out int handSize) → bool
```

Mirror the `CardDeck` + `CardOwnerType` + `CardHand` archetype scan already
duplicated in `CountryCardDrawQuery.FindDeckEntity`/`CountHandCards` and
`BotObservation.Build` (`src/Game.Bots/BotObservation.cs:116-163, 189-201`),
generalized over `CardOwnerKind` instead of hardcoded to `Country`. This reads
whichever pools exist for the org — if the `Org` pool is empty/unpopulated
today, `TryGetHandSize` for that kind simply reports `0`/no deck found, which
the destroy check treats as "trivially satisfied" for that pool (nothing to
check), not as a blocker.

### 3. `OrgDestroySystem` (`Game.Systems`) — helper, `GameLogic`-orchestrated

No system-to-system `Update` calls (`ecs_patterns.md`). Expose a plain static
helper invoked only from `GameLogic` (and tests):

```text
EvaluateAll(World world, ActionConfig config, EffectConfig effectConfig,
    ResourceQuery resources, CountryRelations relations, GameSettings settings,
    int maxControlPool) → int newlyDestroyed
TryDestroyIfConditionsMet(World world, int orgEntity, ...) → bool destroyedThisCall
IsOrgDestroyed(IReadOnlyWorld world, string orgId) → bool
```

**`TryDestroyIfConditionsMet` algorithm (idempotent, per org entity):**
1. If already `Has<IsOrgDestroyed>` → false (no second event).
2. `OrgMetrics.GetTotalControl(world, orgId) > 0` → false (hard gate; skip the
   rest).
3. For each `CardOwnerKind` (`Org`, `Country`):
   a. **No control card** — for each `GetHandCardEntities(world, orgId, kind)`
      entity, resolve its `GameAction.ActionId` via `ActionConfig.Find` to an
      `ActionDefinition`, and if `ActionEffectClassifier.RaisesControl(def,
      effectConfig)` → false (a control card exists; org survives).
   b. **Hand full** — `TryGetHandSize(...)`; if a pool has any deck and
      `handCount < handSize` → false.
   c. **Unplayable ignoring cooldown, in the real owner context** — build the
      candidate set once with `GameCompletionSystem.GetAvailableCountryIds`.
      For a `CardOwnerKind.Country` card, run `ActionPlayability.Evaluate(...)`
      for every available non-destroyed country id as its country context. The
      card blocks destruction if **any** target has no failing entry after
      `ReasonCode == "on_cooldown"` failures are removed; being unplayable in
      one country is insufficient when another country works. For a
      `CardOwnerKind.Org` card, evaluate once with the org-card context (no
      arbitrary selected-country context), preserving any target component on
      the entity. A card counts as unplayable only if every applicable context
      retains a non-cooldown failure.
4. `OrgMetrics.GetGold(world, orgId) < GameSettings.DiscardGoldCost` — if
   false (org can afford to discard) → false.
5. All conditions held: `world.Add(orgEntity, new IsOrgDestroyed())`; destroy
   residual control (§4); set `OrganizationGameOutcome.Result = Loser` on this
   org's outcome component; create `OrgDestroyedApplied { OrganizationId }` on
   a new entity; return true.

**`EvaluateAll`** iterates every `Organization` entity lacking
`IsOrgDestroyed`, calling step 1 above for each; returns the count newly
destroyed (mirrors `CountryDestroySystem.DestroyAllZeroProvinceCountries`'s
shape).

### 4. Control cleanup (`ControlQuery`)

Add `ControlQuery.DestroyAllControlForOrg(World world, string orgId)` —
collect-then-destroy every `ControlEffect` where `OrgId == orgId` (mirrors
`DestroyAllControlInCountry`'s pattern exactly, `src/Game.Systems/ControlQuery.cs:91-106`,
just swapping the filtered field). Since zero total control is itself a
precondition, this should be a no-op in the common case — call it anyway as a
hygiene guard against stale/negative entries, matching the country-destroy
precedent's own reasoning.

### 5. Call site & `GameLogic.Update` ordering

**Card/hand/gold state settle → destroy check → completion (incl.
last-org-standing) → player-loss check → converter.**

Insert into `src/Game.Main/GameLogic.cs`:

1. **Sweep last tick's `OrgDestroyedApplied`** — add
   `CleanupEffectNotificationsSystem.UpdateOrgDestroyed(_world);` beside the
   existing `UpdateCountryDestroyed` call (`GameLogic.cs:151-152`), same
   ordering rationale (survive until after `VisualStateConverter` in the tick
   they were created).
2. **Run the destroy check** — call `OrgDestroySystem.EvaluateAll(...)` after
   `CleanupCardDiscardSystem.Update(_world);` (`GameLogic.cs:307`) and before
   `GameCompletionSystem.Update(...)` (`GameLogic.cs:308`) — control/hand/gold
   state is fully settled for the tick by that point.
3. **`GameCompletionSystem.Update(...)`** (`GameLogic.cs:308`, unchanged call
   site) — now sees `IsOrgDestroyed` exclusions and can complete via
   `LastOrgStandingCondition` the same tick a destruction drops the org count
   to one.
4. **Player-loss check** — insert a new
   `GameCompletionSystem.ApplyPlayerDestroyedLoss(_world, _gameCompletionEntity,
   _context.InitialOrganizationId);` call at the current blank line
   (`GameLogic.cs:309`), i.e. **after** step 3 and **before**
   `_commandAccessor.Clear()` (`GameLogic.cs:310`). See §6 for its body. Running
   after `GameCompletionSystem.Update` means the normal last-org-standing win
   already had first chance to complete the game (correctly crediting the
   surviving org as winner) before this loss-only fallback applies.
5. Mirror steps 2–4 in `LoadState()` around its own
   `GameCompletionSystem.Update(...)` call (`GameLogic.cs:333`) for
   save/load consistency — a save taken mid-tick before these ran should still
   reconcile correctly on load.

### 6. `GameCompletionSystem` changes

```csharp
public static HashSet<string> GetAvailableOrgIds(IReadOnlyWorld world)
```
Mirrors `GetAvailableCountryIds` (`GameCompletionSystem.cs:67-80`) — walks the
`Organization` archetype, skips `world.Has<IsOrgDestroyed>(entity)`.

**`GetParticipants`/win-candidate loop** (`GameCompletionSystem.cs:82-99` +
the evaluation loop in `Update`): `GetParticipants` itself stays unchanged (all
orgs, so `Result` can still be set on every participant when a win completes).
In the win-candidate evaluation loop, add `if
(world.Has<IsOrgDestroyed>(participant.Entity)) { continue; }` before calling
`condition.IsMet(...)` — a destroyed org can never become the declared winner,
even via `ScoreGoalCondition` (score isn't zeroed by destruction, so this is a
real guard, not just defensive).

**New `ApplyPlayerDestroyedLoss`:**
```csharp
public static void ApplyPlayerDestroyedLoss(World world, int completionEntity, string playerOrgId) {
    ref var completion = ref world.Get<GameCompletion>(completionEntity);
    if (completion.IsCompleted || string.IsNullOrEmpty(playerOrgId)) { return; }
    int playerEntity = FindOrgEntity(world, playerOrgId); // new small private helper, same
                                                            // Organization-archetype scan GetParticipants uses
    if (playerEntity < 0 || !world.Has<IsOrgDestroyed>(playerEntity)) { return; }
    completion.WinnerOrganizationId = "";
    completion.IsCompleted = true;
    // Other orgs' OrganizationGameOutcome.Result intentionally left InProgress;
    // the player's own Result is already Loser from OrgDestroySystem.
}
```
Since `Update` and this new method both early-return once `IsCompleted` is
`true`, no one-shot-event bookkeeping is needed — checking the flag directly
each tick is sufficient and idempotent. With exactly one survivor,
`GameCompletionSystem.Update` completes first through
`LastOrgStandingCondition` and the fallback no-ops; with zero survivors the
condition is false and the fallback completes with an empty winner id.

### 7. `LastOrgStandingCondition`, parser, factory, and config

```csharp
public sealed class LastOrgStandingCondition : ICompletionCondition {
    public bool IsMet(CompletionConditionContext context) {
        var availableOrgIds = GameCompletionSystem.GetAvailableOrgIds(context.World);
        return availableOrgIds.Count == 1 && availableOrgIds.Contains(context.OrganizationId);
    }
}
```
The condition is false for zero or two-plus available orgs. For presentation,
use `target = total org count - 1` and `current = total opponents - surviving
opponents`, clamped to `[0, target]`; destroyed org entities remain present, so
this yields stable `destroyed opponents / total opponents` progress.

**Config wiring** (`Game.Configs`):
- `CompletionConditionType` and `CompletionConditionTypeParser`
  (`src/Game.Configs/CompletionConditionType.cs`): add `LastOrgStanding` and
  parse the `"last_org_standing"` token.
- `CompletionConditionFactory.Create` (`src/Game.Systems/CompletionConditionFactory.cs:6-68`):
  add a case constructing `new LastOrgStandingCondition()` (the member's
  `value` field is unused/ignored — document `0` as the JSON convention).
- `Assets/Configs/game_settings.json`'s `completionCondition.members`:
  replace the current three-member list with exactly:
  ```json
  { "type": "score_goal", "value": 50000 },
  { "type": "last_org_standing", "value": 0 }
  ```
- `GameSettings.cs`'s C# default `completionCondition` composition (fallback
  when config is missing/malformed) — update to the same two-member shape and
  `50000` (its current default score value, `275592`, is already stale
  relative to the shipped `270000`; bring both in line).

### 8. Goal and win-condition presentation projections (`Game.Main`)

`GoalsProjector.Build`'s per-org loop (`src/Game.Main/GoalsProjector.cs:61`,
`foreach (Archetype arch in world.GetMatchingArchetypes(orgRequired, null))`)
currently has no exclusion. Add a `world.Has<IsOrgDestroyed>(entity)` skip
inside that loop's per-entity body, mirroring how destroyed countries are
excluded from `GetAvailableCountryIds`-driven target math.

Add `WinConditionHintKind.LastOrgStanding`, a matching leaf descriptor, and
cases in both projection flatteners. `WinConditionHintProjector` emits the kind
for the pre-game `SelectOrg` presentation. `GoalsProjector` emits the runtime
row for each surviving org with `Current = destroyed opponents` and `Target =
total opponents`; config `value` remains the JSON convention `0`, not the
progress target. Update `VisualState` equality helpers as required. The UI plan
owns formatting and EN/RU copy, not parsing or progress math.

### 9. Bot decision-tick skip (`Game.Bots`)

`BotSession.Update` (`src/Game.Bots/BotSession.cs:47-57`) is the only
autonomous per-tick per-org loop that "takes actions." Guard the `foreach
(var bot in _botsByOrgId.Values)` loop: skip calling
`bot.ExecuteDecisionTick(...)` when `OrgDestroySystem.IsOrgDestroyed(_logic.World,
bot.OrgId)` is true. No new bot heuristics — a simple existence check, per the
spec's Out-of-Scope note.

### 10. `VisualState`/`VisualStateConverter` projections (`Game.Main`) — owned here for Part-B to consume

Mirror `WarResultsState`/`CountryDestroyedResultsState` (`src/Game.Main/VisualState.cs:872-938`)
exactly:

- `OrgDestroyedSnapshotState { string OrganizationId }`.
- `OrgDestroyedResultsState` — `Enqueue`/`TryPeek`/`AcknowledgeCurrent` +
  `INotifyPropertyChanged`.
- `VisualState.OrgDestroyedResults` property.

Converter (`src/Game.Main/VisualStateConverter.cs`): add a dedicated
`UpdateOrgDestroyedResults` scan for `OrgDestroyedApplied`, enqueueing
`OrgDestroyedSnapshotState(applied[i].OrganizationId)`, and call it in
`VisualStateConverter.Update` **before** `UpdateGameCompletion` (after
`UpdatePlayerOrganization` is acceptable). Do not put this enqueue in the
later `UpdateGameLog` pass: completion would publish first, and gating
`EndGameWindow` cannot retroactively give the destroy window the modal lock.
The required observable same-pass order is
`OrgDestroyedResults.PropertyChanged` then `GameCompletion.PropertyChanged`.
This remains the Part-A invariant. Part B complements it by making EndGame
yield while the org FIFO is pending and by acknowledging an org snapshot before
unlocking `ModalState`; those UI rules preserve org-before-EndGame when some
other modal was already open and `Unlocked` subscriber order is arbitrary.

**`PlayerOrganizationState.IsDestroyed`** (`VisualState.cs:76-97`): add a
`bool IsDestroyed` param to `PlayerOrganizationState.Set(...)`, sourced in
`UpdatePlayerOrganization` (`VisualStateConverter.cs:288-296`) via
`world.Has<IsOrgDestroyed>(orgEntity)` — the minimal option (no new sibling
`HashSet`-based state, since `OrgInfoDocument` only ever cares about the
player's own org; do **not** build a `WorldOrganizations`-style
all-orgs-destroyed-set unless the UI plan finds a second consumer that needs
it).

UI plan binds `OrgDestroyedResults` + `PlayerOrganization.IsDestroyed` +
`GameCompletion`; no window/document/view work here.

### 11. Assembly boundaries

| Layer | Owns |
|---|---|
| `Game.Components` | `IsOrgDestroyed`, `OrgDestroyedApplied` |
| `Game.Configs` | `CompletionConditionType.LastOrgStanding`, `GameSettings` default completion members |
| `Game.Systems` | `OrgDestroySystem`, `ActionEffectClassifier`, `OrgDestroyHandQuery`, `ControlQuery.DestroyAllControlForOrg`, `GameCompletionSystem` changes, `LastOrgStandingCondition`, `CompletionConditionFactory` case, `GoalsProjector`-adjacent exclusion (actually `Game.Main`, see below) |
| `Game.Bots` | `BotObservation.ClassifyRaisesControl` → thin call to `Game.Systems.ActionEffectClassifier.RaisesControl`; `BotSession.Update` destroyed-org skip |
| `Game.Main` | `GameLogic` orchestration; `GoalsProjector.Build` exclusion; `VisualState`/`VisualStateConverter` projections |

No new asmdef. No MonoBehaviour domain logic.

## Agent Steps

- [x] **Add `IsOrgDestroyed` + `OrgDestroyedApplied`** — empty `[Savable]` tag
  on org entity; one-shot `{ OrganizationId }` on own entity, not savable.

- [x] **Move `ClassifyRaisesControl` to `Game.Systems`** — new
  `ActionEffectClassifier.RaisesControl`; update `BotObservation` call sites.

- [x] **Add generic hand/pool query helper** — `OrgDestroyHandQuery` (or
  extended `CountryCardDrawQuery`) covering both `CardOwnerKind.Country` and
  `.Org` pools; treat an unpopulated pool as trivially satisfied and retain the
  owner kind needed for target-aware playability checks.

- [x] **Implement `OrgDestroySystem`** — `TryDestroyIfConditionsMet`,
  `EvaluateAll`, `IsOrgDestroyed`; all five conditions; idempotent flag +
  single event; wire control destroy + `OrganizationGameOutcome.Result =
  Loser`.

- [x] **`ControlQuery.DestroyAllControlForOrg`** — collect-then-destroy
  mirroring `DestroyAllControlInCountry`.

- [x] **Orchestrate in `GameLogic`** — sweep `OrgDestroyedApplied` beside
  `UpdateCountryDestroyed`; run `OrgDestroySystem.EvaluateAll` before
  `GameCompletionSystem.Update`; add `ApplyPlayerDestroyedLoss` call after it;
  mirror in `LoadState`.

- [x] **`GameCompletionSystem`** — `GetAvailableOrgIds`; skip `IsOrgDestroyed`
  in the win-candidate loop; `ApplyPlayerDestroyedLoss`.

- [x] **`LastOrgStandingCondition` + parser/config wiring** — new condition;
  `CompletionConditionType.LastOrgStanding` + parser token + factory case; update
  `Assets/Configs/game_settings.json` and `GameSettings.cs` defaults to the
  two-member shape (`score_goal` 50000 + `last_org_standing`).

- [x] **Presentation projections** — add `LastOrgStanding` to
  `WinConditionHintKind`, `WinConditionHintProjector`, and `GoalsProjector`;
  skip destroyed org rows and project destroyed-opponents/total-opponents.

- [x] **`BotSession.Update` skip** — guard the bot decision-tick loop against
  destroyed orgs.

- [x] **`VisualState`/`VisualStateConverter` projections** —
  `OrgDestroyedResultsState`/`VisualState.OrgDestroyedResults`; converter
  enqueue before `UpdateGameCompletion` as an explicit observable invariant;
  `PlayerOrganizationState.IsDestroyed` + `UpdatePlayerOrganization` source.

- [x] **Tests + validate** — see Tests; run `dotnet test
  src/GlobalStrategy.Core.sln` and Release build per workflow / `dotnet-build`
  skill.

## User Steps

No Unity Editor scene, prefab, or Play Mode wiring is required for this
logic-only plan. Visual inspection of the destroy **window** and
`EndGameWindow` sequencing belongs to the companion UI plan.

## Tests

Add/extend under `src/Game.Tests/`:

- **`OrgDestroySystemTests` (new)** — org with control → no destroy; zero
  control but a control card in hand → no destroy; zero control, no control
  card, hand not full → no destroy; zero control, no control card, hand full,
  a hand card playable → no destroy; zero control, no control card, hand full,
  every hand card unplayable but *only* via `on_cooldown` → no destroy; a
  country-owned card unplayable in one available country but playable in
  another → no destroy; a country-owned card with a non-cooldown failure in
  every available country contributes to destroy; destroyed countries are not
  target contexts; an org-owned card uses org context; all
  five conditions true (including a genuinely non-cooldown unplayable reason)
  → `IsOrgDestroyed` + one `OrgDestroyedApplied`, `OrganizationGameOutcome.Result
  == Loser`, control entities gone; second call idempotent (no duplicate
  event); org pool (not just country pool) control card also blocks destroy.
- **`ActionEffectClassifierTests` (new or folded into existing
  `BotObservationTests`)** — moved logic still classifies control-raising
  actions correctly from both call sites.
- **`GameCompletionSystemTests`** — `GetAvailableOrgIds` omits destroyed;
  destroyed org never wins even if its score would otherwise satisfy
  `ScoreGoalCondition`; `ApplyPlayerDestroyedLoss` sets `IsCompleted` with
  empty `WinnerOrganizationId` when zero survive or multiple survive without a
  met condition, leaving survivors `InProgress`; exactly one survivor wins via
  `LastOrgStandingCondition`; no-ops when already completed or when the
  player's org isn't destroyed.
- **Parser/factory/config tests** — `last_org_standing` parses and constructs;
  shipped/default members are exactly score 50000 + last-org-standing.
- **`CompletionConditionTests`** — `LastOrgStandingCondition.IsMet` true only
  for the sole remaining non-destroyed org; false when zero or ≥2 remain or
  when evaluated for a destroyed org.
- **Presentation projector tests** — pre-game hint emits the new kind; runtime
  goals exclude destroyed org rows and show `0/N`, intermediate, and `N/N`
  destroyed-opponent progress.
- **Cleanup / VisualState** — `OrgDestroyedApplied` survives until after
  converter same tick; `UpdateOrgDestroyed` next tick removes it; FIFO enqueue
  order; acknowledge drains; `PlayerOrganizationState.IsDestroyed` reflects the
  flag for the player's own org. Add a converter regression test subscribing to
  both state objects and asserting `OrgDestroyedResults.PropertyChanged` is
  observed before `GameCompletion.PropertyChanged` when both publish in one
  converter pass. This test stays in Part A; Part B owns pending-FIFO and
  acknowledge-before-unlock arbitration after publication.
- **`BotSession`/bot-tick** — destroyed org's bot is skipped (no
  `ExecuteDecisionTick` call observed, e.g. via a spy/counter).
- Full suite: `dotnet test src/GlobalStrategy.Core.sln`.

## Constitution Check

Checked against `Docs/Constitution.md`.

No conflicts found — plan aligns with all principles.

- **Rendering** — no RP/shader/material changes.
- **ECS game logic** — destroy, control, goals, completion, bot-tick skip all
  live in `src/`; no Unity MonoBehaviour domain logic.
- **VContainer** — no new DI services or singletons; existing `GameLogic`/
  `GameCompletionSystem`/`BotSession` call sites extended in place.
- **UI Toolkit only** — no Canvas/uGUI; window work deferred to UI plan.
- **Plan / spec discipline** — colocated under
  `Docs/Specs/26_08_11_09_org-destroy-logic/` after the approved spec.
- **File organisation / assemblies** — components / configs / systems / bots /
  main boundaries respected (see §11); no new asmdef.
- **C# style** — tabs, braces, `_` private fields, no redundant access
  modifiers.

Use the implement skill to start working on the plan or request changes.
