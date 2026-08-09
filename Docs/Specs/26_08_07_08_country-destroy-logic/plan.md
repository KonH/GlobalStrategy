# Plan: Country Destroy Logic

## Spec

Source: `Docs/Specs/26_08_07_08_country-destroy-logic/spec.md` (approved; owner
clarifications baked in).

When a country loses all provinces, mark it destroyed with a persistent
`IsDestroyed` flag (entity kept), clear its control and relations, exclude it from
goal availability, emit a one-shot destroy event for VisualState FIFO, and make
cards targeting it unplayable with a localized “doesn’t exist” reason. No revival.
Destroyed countries stay in `WorldCountries.CountryIds`.

**Companion UI** (`Docs/Specs/26_08_07_08_country-destroy-ui/`) is out of scope —
this plan only surfaces VisualState the UI will consume; implement UI via that
spec’s own plan.

Acceptance criteria (condensed):
- **Zero provinces → destroy** — add `[Savable] IsDestroyed` on the country
  entity; keep `Country` and the entity; idempotent (no duplicate flag/event).
- **Still has provinces → no-op** — no flag, no event.
- **One-shot event** — `CountryDestroyedApplied { CountryId }` on its own entity,
  not `[Savable]`; created same tick as destroy; swept **next** tick like
  `WarResolvedApplied` via `CleanupEffectNotificationsSystem.UpdateCountryDestroyed`
  (beside `UpdateWarResolved`). **Do not** add it to `CleanupActionEffectsSystem`
  / `UpdateActionEffects`: peace and StopWar emit destroy events *before* those
  sweeps in `GameLogic.Update`, so a same-tick sweep would drop them before
  `VisualStateConverter` (same ordering bug already documented for
  `WarResolvedApplied`). Spec AC wording that cites
  `ActionSucceeded`/`CleanupActionEffectsSystem` is superseded by this.
- **Control** — destroy every `ControlEffect` for that `CountryId` (all orgs);
  totals for that country become zero.
- **Goals** — `GetAvailableCountryIds` excludes `IsDestroyed`;
  `TotalControlCondition` already scales with available count;
  `FullControlCondition` auto-shrinks its target (see Approach §4).
- **Check scope** — after ownership change, evaluate the **old owner only**
  (`ChangeOwner`’s `OldOwnerId`); war peace batch: check the **loser once** after
  transfers. Zero-provinces-at-start/load: same destroy helper (immediate; no
  special case).
- **Relations** — remove every `CountryRelation` referencing the destroyed id on
  either side; exclude destroyed from `GetSuitableRelationCandidates`.
- **Cards** — targeted at destroyed country (direct / relation / revenge) →
  unplayable with new reason token + locale `"{CountryName} doesn't exist"`.
- **VisualState** — persistent destroyed set + FIFO acknowledgeable queue
  (WarResults pattern) for UI consumption.

## Goal

Ship domain destroy logic under `src/` so ownership → destroy → completion →
VisualStateConverter ordering is correct, goals/relations/cards/control stay
consistent, destroyed countries cannot be (re)selected, and the UI plan only
binds the FIFO modal / presentation (no additional destroy-math ECS).

## Approach

### 1. Components (`Game.Components`)

| Component | Savable | Shape | Lifecycle |
|---|---|---|---|
| `IsDestroyed` | yes | empty tag on country entity (like `IsSelected`) | permanent for session; survives save/load |
| `CountryDestroyedApplied` | no | `{ string CountryId }` on **own** entity | created on destroy; read by converter same tick; swept **next** tick |

Add `IsDestroyed.cs` and put `CountryDestroyedApplied` next to other one-shots
(`GameLogEffects.cs` or a sibling file). Do not delete the country entity or
`Country` component.

### 2. `CountryDestroySystem` (`Game.Systems`) — helpers, GameLogic-orchestrated

No system-to-system `Update` calls (`ecs_patterns.md`). Expose plain static
helpers invoked only from `GameLogic` (and tests):

```text
TryDestroyIfNoProvinces(World world, string countryId) → bool destroyedThisCall
DestroyAllZeroProvinceCountries(World world)           → int newlyDestroyed
IsCountryDestroyed(IReadOnlyWorld world, string countryId) → bool
```

**`TryDestroyIfNoProvinces` algorithm (idempotent):**
1. Resolve country entity by `Country.CountryId`; missing → false.
2. If already `Has<IsDestroyed>` → false (no second event).
3. Province check: lightweight scan of `ProvinceOwnership` for any
   `OwnerId == countryId` (prefer `HasAnyProvince` helper over full
   `GetProvincesByOwner` dictionary). Missing owner ⇒ zero provinces.
4. If any province → false.
5. Else: `world.Add(countryEntity, new IsDestroyed())`; if
   `world.Has<IsSelected>(countryEntity)`, `world.Remove<IsSelected>(countryEntity)`
   (same-tick: converter drops selection; UI deselect command remains optional
   belt-and-suspenders); clear control; remove relations; create entity +
   `CountryDestroyedApplied { CountryId }`; return true.
   Init/load zero-province pass must likewise strip `IsSelected` from any country
   that is or becomes destroyed.

**Control clear:** collect-then-`Destroy` all entities with `ControlEffect` where
`CountryId` matches (all orgs). Prefer destroy over `Value = 0`. Add
`ControlQuery.DestroyAllControlInCountry(World, countryId)` (or private helper
inside `CountryDestroySystem` using the same collect-then-destroy pattern as
`ControlQuery.ReduceOrgControlInCountry`).

**Relations clear:** add
`CountryRelations.RemoveAllReferencing(World, countryId)` — iterate
`CountryRelation`, destroy when `LeftCountryId` or `RightCountryId` matches,
bump `CountryRelationsVersion` once if any removed. Also update
`GetSuitableRelationCandidates` to skip candidates with `IsDestroyed` (and skip
calling country if destroyed).

### 3. Call sites & `GameLogic.Update` ordering

**Ownership mutate → destroy check → completion → converter.**

| Call site | Behavior |
|---|---|
| `DebugChangeProvinceOwnerCommand` | after `ChangeOwner` when `Changed`, `TryDestroyIfNoProvinces(oldOwnerId)` |
| `Wars.TransferOccupiedProvinces` / peace | after a peace that transferred ≥1 province, check **loser once** (not each province). Thread loser ids out of `TryResolvePeaceByChance` / `StopWar` / `ResolvePeace` (e.g. return or accumulate `List<string>` of losers that lost territory); `GameLogic` calls `TryDestroyIfNoProvinces` per unique loser. Do **not** call destroy from inside `Wars` as a nested system entry. |
| Init (first seed) | after `ProvinceOwnershipSystem.Seed` in the `InitSystem.Update` branch, `DestroyAllZeroProvinceCountries` (covers zero-at-start) |
| `LoadState` | after load/reconcile, before/alongside completion refresh: `DestroyAllZeroProvinceCountries` (idempotent; restores edge cases missing the flag) |

**Cleanup:** add `CleanupEffectNotificationsSystem.UpdateCountryDestroyed` sweeping
`CountryDestroyedApplied` (same `RemoveComponent<T>` pattern as
`UpdateWarResolved`). Call it at the **start** of the next tick — place beside
`UpdateWarResolved` so last tick’s events survive until after
`VisualStateConverter` in the tick they were created.

Suggested relative order inside `GameLogic.Update` (sites are separate — do not
collapse peace and StopWar into one hook):
1. Sweep last tick’s `CountryDestroyedApplied` beside `UpdateWarResolved`.
2. After `Wars.TryResolvePeaceByChance(...)` returns, destroy-check each unique
   loser that lost ≥1 province in that call.
3. After each debug `StopWar` that resolves peace with transfers, destroy-check
   that loser (StopWar runs much later in the tick than step 2).
4. After debug `ChangeOwner` when `Changed`, destroy-check `OldOwnerId`.
5. … card pipeline …
6. `GameCompletionSystem.Update` (sees `IsDestroyed` exclusions).
7. `_visualStateConverter.Update` (reads this tick’s `CountryDestroyedApplied`).

### 4. Goals (`GameCompletionSystem` / `FullControlCondition`)

**`GetAvailableCountryIds`:** when collecting `Country` entities, skip those with
`IsDestroyed`. `GoalsProjector` and `GameCompletionSystem.Update` already use this
set — `TotalControlCondition.GetTarget` (`threshold * Count * MaxControlPool`)
auto-scales.

**`FullControlCondition.GetTarget`:** change from fixed `_requiredCountryCount` to:

```csharp
return Math.Min(_requiredCountryCount, context.AvailableCountryIds.Count);
```

**Justification:** available set already excludes destroyed countries; `Min`
shrinks the fixed target only when fewer countries remain than the configured
requirement, keeping the goal achievable without easing it while enough
countries still exist. Same spirit as `TotalControlCondition` keying off
`AvailableCountryIds.Count`. Numerator already ignores destroyed countries
because `GetCurrent` only walks `AvailableCountryIds`.

Update `CompletionConditionTests` expectations where target was unconditionally
`15` under a smaller available set.

### 5. VisualState FIFO + persistent destroyed projection (`Game.Main`)

Clone `WarResultsState` (parallel naming: `WarResults` / `WarResultsState`):

- `CountryDestroyedSnapshotState` — at least `CountryId` (UI may add flavor later).
- `CountryDestroyedResultsState` — `Enqueue` / `TryPeek` / `AcknowledgeCurrent` +
  `INotifyPropertyChanged` (same shape as `WarResultsState`).
- `VisualState.CountryDestroyedResults` property.

Converter:
- Scan `CountryDestroyedApplied` each tick → `Enqueue` snapshots (stable archetype
  order).
- Project persistent destroyed ids (e.g. `WorldCountries` gains
  `DestroyedCountryIds` `HashSet`, or a dedicated `DestroyedCountriesState`) by
  reading country entities with `IsDestroyed`. **Keep** full
  `WorldCountries.CountryIds` including destroyed (spec).

UI plan binds the queue + destroyed set; no window work here.

### 6. Cards — playability + reason token

**Early gate** (before condition/cost/cooldown), both paths:

1. **`ActionPlayability.Evaluate`** — if the card’s country target is destroyed,
   return `false`. Targets:
   - `countryId` argument (direct country context), and/or
   - `RelationCardTarget.TargetCountryId` / `RevengeCardTarget.TargetCountryId`
     on the card entity when present.
2. **`VisualStateConverter.BuildEntry`** — same check **first**; set
   `isUnplayable = true`, `unplayableReason = "country_no_longer_exists"` so the
   reason is distinct from condition-derived tokens.

Token name: `country_no_longer_exists` (matches existing
`action.country.unplayable.*` style).

**Locale (logic includes reason copy; window copy is UI plan):**
- EN: `action.country.unplayable.country_no_longer_exists` → `"{0} doesn't exist"`
- RU: real translation via **localization** skill (not English placeholder).

**Presentation glue (minimal, not the destroy window):** one switch arm in
`CountryActionsView` for `country_no_longer_exists`.
`CountryActionsView.Refresh` / `BuildHandCard` today have **no** card-home
country id — only `ActionCardEntry.TargetCountryId`. Either:
(a) pass `selected.CountryId` into `Refresh` from `CountryInfoView` and use
`!string.IsNullOrEmpty(card.TargetCountryId) ? card.TargetCountryId : selectedCountryId`,
or (b) when `BuildEntry` sets this reason, also put the destroyed id on the entry
(e.g. ensure `TargetCountryId` / a dedicated field carries it for direct cards).
Do not assume “card’s country id” is already available in the view.

### 6b. Selection guard (`SelectCountrySystem`)

In `SelectCountrySystem.Update`, when `targetId` is non-empty and the matching
country entity has `IsDestroyed`, do **not** add `IsSelected` (selection fails).
Empty-id deselection unchanged. Required by the UI companion AC; land it here so
Part A owns all `IsDestroyed` domain rules (UI plan skips this step if present).

### 7. Assembly boundaries

| Layer | Owns |
|---|---|
| `Game.Components` | `IsDestroyed`, `CountryDestroyedApplied` |
| `Game.Systems` | `CountryDestroySystem`, control/relation helpers, goal/playability changes, cleanup sweep |
| `Game.Main` | orchestration in `GameLogic`; VisualState queue + converter projection |
| `Assets/Scripts` / localization | reason switch arm + locale keys only; **no** `CountryDestroyedWindow` |

No new asmdef. No MonoBehaviour domain logic.

## Agent Steps

- [x] **Add `IsDestroyed` + `CountryDestroyedApplied`** — empty `[Savable]` tag on
  country entity; one-shot `{ CountryId }` on own entity, not savable.

- [x] **Implement `CountryDestroySystem` helpers** — `TryDestroyIfNoProvinces`,
  `DestroyAllZeroProvinceCountries`, `IsCountryDestroyed`; province existence
  check; idempotent flag + single event; wire control destroy + relation purge.

- [x] **Control + relations helpers** — `DestroyAllControlInCountry` (collect then
  `Destroy`); `CountryRelations.RemoveAllReferencing`; exclude `IsDestroyed` from
  `GetSuitableRelationCandidates`.

- [x] **Orchestrate in `GameLogic`** — init/load full zero-province pass; after
  peace/StopWar check unique losers that lost provinces; after debug
  `ChangeOwner` check `OldOwnerId`; sweep `CountryDestroyedApplied` next tick via
  `CleanupEffectNotificationsSystem.UpdateCountryDestroyed`; keep ordering
  destroy → `GameCompletionSystem` → converter.

- [x] **Thread loser ids from peace path** — adjust
  `Wars.TryResolvePeaceByChance` / `StopWar` / `ResolvePeace` return or out-list
  so `GameLogic` can destroy-check without `Wars` calling destroy as a nested
  system.

- [x] **Goals** — exclude `IsDestroyed` in `GetAvailableCountryIds`;
  `FullControlCondition.GetTarget` = `Min(configured, AvailableCountryIds.Count)`;
  update completion tests.

- [x] **VisualState FIFO + destroyed projection** — `CountryDestroyedResultsState` +
  snapshot on `VisualState.CountryDestroyedResults`; converter enqueue from
  `CountryDestroyedApplied`; project persistent destroyed ids; keep destroyed
  countries in `CountryIds`.

- [x] **Card unplayable gate + locale** — early gate in `ActionPlayability` +
  `BuildEntry` token `country_no_longer_exists`; EN/RU locale keys; one
  `CountryActionsView` switch arm with country name format arg (plumb destroyed
  id into the entry or pass selected country id into `Refresh` — see Approach §6).

- [x] **SelectCountrySystem guard** — skip `IsSelected` when target has
  `IsDestroyed`; add/extend tests (select destroyed → no `IsSelected`; normal
  select still works; empty id still clears).

- [x] **Tests + validate** — see Tests; run
  `dotnet test src/GlobalStrategy.Core.sln` and Release build per workflow /
  `dotnet-build` skill.

## User Steps

No Unity Editor scene, prefab, or Play Mode wiring is required for this logic-only
plan. Localization RU is agent-side via the localization skill. Visual inspection
of the destroy **window** belongs to the companion UI plan.

## Tests

Add/extend under `src/Game.Tests/`:

- **`CountryDestroySystemTests` (new)** — country with provinces → no destroy;
  last province lost → `IsDestroyed` + one `CountryDestroyedApplied`; if it was
  selected, `IsSelected` removed; second call idempotent (no duplicate event);
  control entities for that country gone (`GetTotalControlInCountry == 0`); all
  friend/rival relations referencing it removed; other countries’ unrelated
  relations intact.
- **`SelectCountrySystem` tests** — selecting a destroyed country does not add
  `IsSelected`; normal select still works; empty id still clears.
- **`GameCompletionSystemTests` / `CompletionConditionTests`** —
  `GetAvailableCountryIds` omits destroyed; `TotalControlCondition` target shrinks
  with available count; `FullControlCondition.GetTarget` uses `Min(configured,
  availableCount)` (e.g. configured 15 with 14 available → target 14; with 20
  available → still 15).
- **`CountryRelationsTests`** — `RemoveAllReferencing`; candidates exclude
  destroyed.
- **`ActionPlayabilityTests` / converter opinion-gate tests** — card with
  destroyed direct or relation/revenge target → `Evaluate` false;
  `BuildEntry` reason `country_no_longer_exists`.
- **Cleanup / VisualState** — event survives until after converter same tick;
  `UpdateCountryDestroyed` next tick removes it; FIFO enqueue order; acknowledge
  drains; persistent destroyed set reflects flag; `CountryIds` still contains
  destroyed id.
- **Peace / ownership integration** — after `TransferOccupiedProvinces` emptying
  loser, loser destroyed once; debug `ChangeOwner` stripping last province
  destroys old owner only.
- Full suite: `dotnet test src/GlobalStrategy.Core.sln`.

## Constitution Check

Checked against `Docs/Constitution.md`.

No conflicts found — plan aligns with all principles.

- **Rendering** — no RP/shader/material changes.
- **ECS game logic** — destroy, control, relations, goals, playability live in
  `src/`; Unity only maps the unplayable reason token for existing card UI.
- **VContainer** — no new services; no ad-hoc singletons.
- **UI Toolkit only** — no Canvas/uGUI; destroy window deferred to UI plan.
- **Plan / spec discipline** — colocated under
  `Docs/Specs/26_08_07_08_country-destroy-logic/` after the approved spec.
- **File organisation / assemblies** — components / systems / main boundaries
  respected; no new asmdef; one feature folder rule N/A (no new
  `Assets/Scripts/` feature folder).
- **C# style** — tabs, braces, `_` private fields, no redundant access modifiers.

Use the implement skill to start working on the plan or request changes.
