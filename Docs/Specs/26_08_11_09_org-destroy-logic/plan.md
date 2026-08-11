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
is destroyed and at least two other orgs remain (not the last-org-standing
case), end the session immediately as a loss via the same `GameCompletion`
flow the normal win path uses, with no winner declared.

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
of `IsDestroyed`.

Acceptance criteria (condensed):
- **Destroy conditions** — zero total control (`OrgMetrics.GetTotalControl ==
  0`) is a hard gate; when true, additionally require: no control card in any
  `CardOwnerKind` pool's hand; every pool's hand full; every hand card
  unplayable ignoring a cooldown-only block; gold below `DiscardGoldCost`. All
  must hold simultaneously. No war-survival guard.
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
  remains non-destroyed and it is the org being evaluated.
- **Config** — `completionCondition.members` becomes exactly `score_goal`
  (`50000`) + `last_org_standing`; `total_control`/`full_control_countries`
  removed from shipped config (classes may remain unused).
- **Player-loss ending** — when the player's own org is destroyed and it is
  *not* the last-org-standing case, `GameCompletion.IsCompleted = true` with
  `WinnerOrganizationId` left empty (no winner declared); other orgs' `Result`
  stay `InProgress`.

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
   c. **Unplayable ignoring cooldown** — for each hand card entity,
      `ActionPlayability.Evaluate(...)`; collect failing `Entries`' reason
      codes; if there are zero failing entries, or every failing entry's
      `ReasonCode == "on_cooldown"` → false (the card is playable, or its only
      block is cooldown, which doesn't count).
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
each tick is sufficient and idempotent.

### 7. `LastOrgStandingCondition` (`Game.Systems`)

```csharp
public sealed class LastOrgStandingCondition : ICompletionCondition {
    public bool IsMet(CompletionConditionContext context) {
        var availableOrgIds = GameCompletionSystem.GetAvailableOrgIds(context.World);
        return availableOrgIds.Count == 1 && availableOrgIds.Contains(context.OrganizationId);
    }
}
```
No `GetCurrent`/`GetTarget` needed beyond what `ICompletionCondition` requires
(mirrors the shape of the other three; add a debug-friendly `GetCurrent`/
`GetTarget` pair only if `CompletionConditionContext`'s existing debug/UI
surface requires it — check `GoalsProjector`/`WarProgressWindow`-style
consumers of `ICompletionCondition` for whether they assume those members
exist beyond `IsMet` before deciding).

**Config wiring** (`Game.Configs`):
- `CompletionConditionType` (`src/Game.Configs/CompletionConditionType.cs`):
  add `LastOrgStanding` enum member + `"last_org_standing"` string token.
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

### 8. Goals (`GoalsProjector`)

`GoalsProjector.Build`'s per-org loop (`src/Game.Main/GoalsProjector.cs:61`,
`foreach (Archetype arch in world.GetMatchingArchetypes(orgRequired, null))`)
currently has no exclusion. Add a `world.Has<IsOrgDestroyed>(entity)` skip
inside that loop's per-entity body, mirroring how destroyed countries are
excluded from `GetAvailableCountryIds`-driven target math.

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

Converter (`src/Game.Main/VisualStateConverter.cs`): add an `OrgDestroyedApplied`
scan block beside the existing `countryDestroyedReq` block inside
`UpdateGameLog` (`VisualStateConverter.cs:1173-1181`), enqueueing
`OrgDestroyedSnapshotState(applied[i].OrganizationId)`.

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

- [ ] **Add `IsOrgDestroyed` + `OrgDestroyedApplied`** — empty `[Savable]` tag
  on org entity; one-shot `{ OrganizationId }` on own entity, not savable.

- [ ] **Move `ClassifyRaisesControl` to `Game.Systems`** — new
  `ActionEffectClassifier.RaisesControl`; update `BotObservation` call sites.

- [ ] **Add generic hand/pool query helper** — `OrgDestroyHandQuery` (or
  extended `CountryCardDrawQuery`) covering both `CardOwnerKind.Country` and
  `.Org` pools; treat an unpopulated pool as trivially satisfied.

- [ ] **Implement `OrgDestroySystem`** — `TryDestroyIfConditionsMet`,
  `EvaluateAll`, `IsOrgDestroyed`; all five conditions; idempotent flag +
  single event; wire control destroy + `OrganizationGameOutcome.Result =
  Loser`.

- [ ] **`ControlQuery.DestroyAllControlForOrg`** — collect-then-destroy
  mirroring `DestroyAllControlInCountry`.

- [ ] **Orchestrate in `GameLogic`** — sweep `OrgDestroyedApplied` beside
  `UpdateCountryDestroyed`; run `OrgDestroySystem.EvaluateAll` before
  `GameCompletionSystem.Update`; add `ApplyPlayerDestroyedLoss` call after it;
  mirror in `LoadState`.

- [ ] **`GameCompletionSystem`** — `GetAvailableOrgIds`; skip `IsOrgDestroyed`
  in the win-candidate loop; `ApplyPlayerDestroyedLoss`.

- [ ] **`LastOrgStandingCondition` + config wiring** — new condition class;
  `CompletionConditionType.LastOrgStanding` + factory case; update
  `Assets/Configs/game_settings.json` and `GameSettings.cs` defaults to the
  two-member shape (`score_goal` 50000 + `last_org_standing`).

- [ ] **`GoalsProjector.Build` exclusion** — skip `IsOrgDestroyed` orgs in the
  per-org loop.

- [ ] **`BotSession.Update` skip** — guard the bot decision-tick loop against
  destroyed orgs.

- [ ] **`VisualState`/`VisualStateConverter` projections** —
  `OrgDestroyedResultsState`/`VisualState.OrgDestroyedResults`; converter
  enqueue; `PlayerOrganizationState.IsDestroyed` + `UpdatePlayerOrganization`
  source.

- [ ] **Tests + validate** — see Tests; run `dotnet test
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
  every hand card unplayable but *only* via `on_cooldown` → no destroy; all
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
  empty `WinnerOrganizationId` and leaves other orgs `InProgress` when the
  player's org is destroyed and ≥2 others remain; no-ops when already
  completed or when the player's org isn't destroyed.
- **`CompletionConditionTests`** — `LastOrgStandingCondition.IsMet` true only
  for the sole remaining non-destroyed org; false when ≥2 remain or when
  evaluated for a destroyed org.
- **`GoalsProjectorTests`** — destroyed orgs excluded from the per-org goal
  loop.
- **Cleanup / VisualState** — `OrgDestroyedApplied` survives until after
  converter same tick; `UpdateOrgDestroyed` next tick removes it; FIFO enqueue
  order; acknowledge drains; `PlayerOrganizationState.IsDestroyed` reflects the
  flag for the player's own org.
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
