# Plan: War Progress

## Spec

As a player, I want wars to advance through recurring province battles that commit
recruits, inflict casualties, change war progress, and update occupation, so a
declared war becomes an ongoing territorial conflict.

The owner approved `spec.md` and selected:

- shared-border option A: count attacker/defender neighboring province pairs, using
  final generated geometry where a shared boundary segment counts and point-only
  contact does not;
- nearest-fallback option A: rank enemy provinces by straight-line centroid distance
  to the closest province currently owned or occupied by the initiating country.

The full acceptance contract remains in `spec.md`. This plan does not broaden it to
allies, natural war termination, battle UI, ownership transfer, or player-issued
battle orders.

## Goal

Extend issue #69's persisted ECS war model with deterministic, config-driven battle
capacity, initiation, hourly combat, casualty settlement, progress movement, troop
release, and occupation resolution. Preserve active and finished battle state through
save/load and retain finished battles until their war is stopped.

Issue #71 is an implementation prerequisite. Its country `damage` and `durability`
resources do not exist on `main` yet, and issue #71 is still at its specification
gate. Implementation of this plan must first integrate the approved #71 result, use
the resource ids and lifecycle it establishes, and run battle progression after those
resources are available/recalculated. This feature consumes those resources; it must
not introduce a competing damage/durability model.

## Approach

### Data and configuration shape

- Extend `ProvinceEntry` with generated centroid coordinates and an ordinally sorted
  neighbor-id list. Build a single immutable `ProvinceTopology` lookup from
  `ProvinceConfig` in `GameLogic`; do not use country-level `ProximityMapData` and do
  not save the rebuildable lookup.
- Compose `[Savable] WarBattleCapacity { MaxConcurrentBattleCount }` onto the existing
  `War` entity. Capture it once in `Wars.DeclareWar` from current
  `ProvinceOwnership` and generated topology.
- Add `[Savable] Battle` entities keyed by deterministic `BattleId`, with `WarId`,
  target province, `BattleState`, and winner. Add one `[Savable] BattleForce` relation
  entity per participating country with battle id, country id, side, whole-valued
  troops, and cumulative whole-valued casualties. Retain both finished battles and
  their force rows until `Wars.StopWar`.
- Store `war_initiative` in the existing country-owned `Resource` shape. Seed one
  zero-valued resource per country from `resource_config.json`; reset the two
  participants to the configured attacker/defender initial values on every successful
  declaration.
- Group the battle constants under `GameSettings.WarBattles` /
  `game_settings.json`'s `warBattles` object so `Wars` and `WarBattleSystem` receive
  one immutable settings object instead of a long scalar parameter list.

### Deterministic processing

- Invoke one `WarBattleSystem.Update` from `GameLogic` after the debug declare/stop
  loops and after issue #71's combat-resource recalculation point. Build every
  war/country/province/battle candidate list in ordinal id order before consuming the
  shared seeded `Random`.
- Fill available battle slots once per game update. For every configured hourly
  boundary crossed between `previousTime` and `currentTime`, fill slots before taking
  an ordinal snapshot of active battles, then process exactly one round for every
  battle in that snapshot. Refill before the next crossed boundary. A battle created
  for a boundary therefore fights immediately, while no battle fights twice on the
  same boundary.
- Use global configured-hour buckets, derived from `DateTime.Ticks`, rather than
  `previousTime.Hour != currentTime.Hour`; this preserves cadence when the round
  interval is changed and processes every elapsed boundary at high game speed.
- Treat `BattleState.Finished` as the persisted idempotence marker. Finish side
  effects and the state transition occur synchronously in one `Update`; later updates
  and loaded saves skip finished battles.

### Battle and resolution rules

- Capacity counts each current attacker-owned/defender-owned neighboring pair once,
  applies floor division by `SharedBorderPairsPerAdditionalBattle`, and adds
  `BaseConcurrentBattleCount`. Occupation does not change ownership for this initial
  capacity calculation.
- Initiative selection compares all participants' `war_initiative`; tied maxima are
  resolved by seeded selection. The initiator pays the configured cost before target
  selection.
- Primary targets are enemy-owned provinces neighboring any province currently owned
  or occupied by the initiator. Targets already occupied by the initiator or carrying
  an active battle are excluded. If no primary target exists, rank all otherwise
  eligible enemy provinces by minimum squared centroid distance to current
  owned/occupied initiator territory, break distance ties by province id, take up to
  the configured fallback count, and select with the seeded RNG. With no origin or no
  eligible target, leave the slot empty.
- Allocate each participant independently from its current recruits:
  `ceil(available * (1 / (maxConcurrent + denominatorOffset)) * random(min, max))`,
  clamped to `[0, available]`. Create zero-troop force rows rather than suppressing
  the battle.
- Resolve first strike and answering strike sequentially from remaining troops.
  Casualties use the approved damage/durability formula, configured divisors/bounds,
  ceiling, and a final `[0, current troops]` clamp. Award non-final-round initiative
  only to the side that inflicted more casualties during that round; a tie awards
  neither side.
- Check exhaustion in strike order after both required strikes: an exhausted
  responding side loses; otherwise an exhausted first-striking side loses. This also
  gives a seeded, non-stalling result to the explicitly required zero-versus-zero
  battle: the randomly selected first-striking side wins.
- On finish, clamp progress to `[-100, 100]`, apply each country's cumulative
  casualties once to its currently owned province populations, return surviving
  troops to recruits, and resolve occupation through a shared plain mutation helper
  that bumps `ProvinceOccupationVersion`.
- Population deduction uses ordinal province order and a fixed remaining-casualty
  budget. Compute proportional shares, round each share upward, clamp it to both the
  province population and remaining budget, and continue until the clamped country
  casualty total is fully assigned. This keeps the aggregate deduction exact and
  deterministic despite per-province rounding.
- `Wars.StopWar` releases only surviving troops from active battles, applies no
  accumulated casualties or occupation/progress result, then removes every battle,
  force, participant, and war entity. Finished-battle survivors were already
  released and are not released twice.

This is a headless ECS/config change. There is no Unity scene, prefab, UI Toolkit, or
manual Editor work, so every implementation item is an Agent Step.

## Agent Steps

- [ ] **Add battle enums** — create `src/Game.Common/BattleState.cs` with
  `Active`/`Finished`. Reuse `WarParticipantKind` for force side and finished winner,
  with winner read only when `BattleState == Finished`.

- [ ] **Add persisted battle components** — add
  `src/Game.Components/WarBattle.cs` with:
  `WarBattleCapacity { int MaxConcurrentBattleCount }`,
  `Battle { string BattleId, string WarId, string TargetProvinceId, BattleState State,
  WarParticipantKind Winner }`, and
  `BattleForce { string BattleId, string CountryId, WarParticipantKind Side,
  double Troops, double Casualties }`. Mark every struct `[Savable]`; troops and
  casualties remain doubles only to match `Resource.Value`, but all writes must be
  non-negative whole values.

- [ ] **Add grouped war-battle settings** — add `WarBattleSettings` and
  `GameSettings.WarBattles` in `src/Game.Configs/GameSettings.cs`, plus the matching
  `warBattles` object in `Assets/Configs/game_settings.json`. Use fields/defaults
  `BaseConcurrentBattleCount = 1`,
  `SharedBorderPairsPerAdditionalBattle = 5`,
  `AttackerInitialInitiative = 1.0`,
  `DefenderInitialInitiative = 0.0`, `InitiationCost = 1.0`,
  `RoundWinnerInitiativeGain = 0.5`, `BattleProgressGain = 10`,
  `FallbackCandidateCount = 3`, `TroopDenominatorOffset = 1`,
  `TroopRandomMin = 0.9`, `TroopRandomMax = 1.1`,
  `RoundIntervalHours = 1`, `DamageDivisor = 300`,
  `DurabilityDivisor = 300`, `CasualtyRandomMin = 0.9`,
  `CasualtyRandomMax = 1.1`, `MinimumCasualtyFraction = 0.01`, and
  `MinimumAbsoluteCasualties = 1`. Validate positive counts/divisors/intervals and
  ordered random bounds once when `GameLogic` loads the config, with descriptive
  fail-fast errors. Keep the existing core war range `[-100, 100]` and issue #69's
  monthly decay setting unchanged.

- [ ] **Generate province adjacency and centroids** — update
  `scripts/utils/generate_provinces.py` after mapshaper simplification, so topology
  matches the geometry actually shipped. Compute each final geometry's centroid and
  use Shapely's spatial index to compare candidate boundaries. Add two provinces as
  neighbors only when their boundary intersection has non-zero length; exclude
  self-links and point-only intersections, write symmetric ordinal neighbor lists,
  and emit centroid coordinates plus `neighborProvinceIds` into the intermediate
  feature properties. Add focused Python tests under
  `scripts/utils/tests/test_generate_provinces_topology.py` for shared segments,
  point-only contact, symmetry, and stable ordering.

- [ ] **Carry topology through the config loader** — extend
  `ProvinceEntry` in `src/Game.Configs/ProvinceConfig.cs` with `double CentroidX`,
  `double CentroidY`, and `List<string> NeighborProvinceIds`; update
  `src/Game.Configs.Loader/ProvinceProcessor.cs` to read them. Extend
  `src/Game.Tests/ProvinceProcessorTests.cs` for extraction/default handling and
  `src/Game.Tests/ProvinceConfigTests.cs` to validate the real generated config:
  finite centroids, known neighbor ids, no self-neighbors, ordinal uniqueness, and
  symmetric adjacency.

- [ ] **Regenerate committed province assets and pipeline documentation** — run the
  documented Python province generator followed by `src/Game.Configs.Loader`, commit
  the resulting `Assets/Configs/province_config.json` and
  `Assets/Configs/provinces_1880.json`, and update
  `.claude/rules/unity/province_config_generator.md` so its Stage 1/Stage 2 property
  lists and rerun instructions describe centroid/adjacency generation.

- [ ] **Build an immutable runtime topology lookup** — add
  `src/Game.Systems/ProvinceTopology.cs`, constructed once from `ProvinceConfig`, with
  ordinal province ids, neighbor lookup, centroid lookup, and squared-distance
  helpers. Construct and retain it in `src/Game.Main/GameLogic.cs` after loading
  `ProvinceConfig`; do not add it to ECS or serialization. Add
  `src/Game.Tests/ProvinceTopologyTests.cs` for neighbor lookup, distance, unknown ids,
  and stable ordering.

- [ ] **Seed and expose war initiative** — add
  `ResourceDefinitions.WarInitiative = "war_initiative"` and a zero-valued
  country-seeded entry in `Assets/Configs/resource_config.json`. Update
  `InitSystem.CreateCountryResourceEntities` to allow this no-effect country resource
  without treating it as unsupported. Do not add it to the display whitelist or
  `ResourceIdUpdateOrder`.

- [ ] **Add plain resource mutations** — add
  `src/Game.Systems/ResourceMutations.cs` with owner/resource lookup plus set and
  clamped-delta operations that return whether the resource was found and the actual
  applied delta. Add a `ResourceQuery.TryGetValue` form so a missing combat resource
  is distinguishable from a valid zero. Cover country recruits/initiative and
  province population with `src/Game.Tests/ResourceMutationsTests.cs`; do not call
  `ResourceSystem.Update` from battle code.

- [ ] **Extract shared occupation mutation** — add a plain
  `src/Game.Systems/ProvinceOccupationMutations.cs` for set/clear plus the scoped
  `ProvinceOccupationVersion` bump. Make `ProvinceOccupationSystem`'s existing
  command-facing methods delegate to it, and let battle resolution call the plain
  helper. Preserve all behavior in `src/Game.Tests/ProvinceOccupationTests.cs` and
  add overwrite/clear/version assertions needed by battle resolution.

- [ ] **Initialize battle state in `Wars.DeclareWar`** — extend its signature to
  receive `ProvinceTopology` and `WarBattleSettings`. After the existing declaration
  guards, count current attacker/defender owned neighbor pairs from
  `ProvinceOwnership`, calculate/capture `WarBattleCapacity`, and set both country
  initiative resources to the configured initial values as part of the successful
  declaration. A zero-pair or disconnected war still receives the configured base
  capacity. Update direct and `GameLogic` declaration tests in
  `src/Game.Tests/WarsTests.cs`.

- [ ] **Add battle queries and deterministic ids** — add a plain
  `src/Game.Systems/WarBattles.cs` helper for collecting a war's participants,
  active/finished battles, forces, active target ids, and side/country mappings in
  ordinal order. Generate `BattleId` from `WarId` plus the retained battle count, so
  ids remain deterministic and collision-free across save/load without a global
  mutable singleton.

- [ ] **Implement slot filling and target selection** — add
  `src/Game.Systems/WarBattleSystem.cs` with one public `Update` entry point. For each
  war in ordinal order, fill to captured capacity: choose the maximum-initiative
  country (seeded tie), charge initiative, build primary targets from runtime
  ownership/occupation/topology, fall back to centroid-ranked candidates, and stop
  filling when no valid target exists. Sort before every random choice and exclude
  only active battle targets, so a finished battle's province may be selected again.

- [ ] **Commit recruits into per-country forces** — within battle creation, read each
  current participant's recruits at that moment, calculate/ceil/clamp the independent
  randomized commitment, subtract exactly the committed amount, and create a
  zero-casualty force row even when commitment is zero. Add force rows before marking
  the battle active so no observable battle lacks its participant state.

- [ ] **Implement elapsed-boundary combat rounds** — in
  `WarBattleSystem.Update`, enumerate every crossed configured-hour bucket. Before
  each boundary, fill slots; snapshot active battles by `BattleId`; select the first
  side with the shared RNG; resolve both strikes sequentially; ceil and clamp
  casualties; compare per-round inflicted totals; and add initiative only for a
  non-final strict winner. Check responding-side then first-side exhaustion to resolve
  zero-troop and normal battles without stalling.

- [ ] **Implement atomic battle finish resolution** — when a side is exhausted:
  adjust the composed `WarProgress` by configured ±gain with `[-100, 100]` clamp;
  distribute each force's cumulative casualties across its country's currently owned
  province-population resources with exact aggregate accounting; return surviving
  troops to recruits; resolve the target's four owner/occupier cases through
  `ProvinceOccupationMutations`; then persist winner and `Finished` state. Finished
  rows are skipped forever after this synchronous transition.

- [ ] **Extend debug stop cleanup** — update `Wars.StopWar` to find the war's active
  forces, return their surviving troops once, and then destroy all force entities,
  all active/finished battle entities, the two participant entities, and the composed
  war entity. Do not apply active-battle casualties, progress, or occupation. Extend
  `WarsTests` for active survivor release, no population/occupation side effects,
  finished-history removal, and stop idempotence.

- [ ] **Wire progression into `GameLogic`** — retain issue #69's monthly
  `WarSystem.Update`, update debug declaration calls with topology/settings, and call
  `WarBattleSystem.Update(_world, _previousTime, currentTime, _rng, _provinceTopology,
  GameSettings.WarBattles)` after declare/stop handling and after issue #71's
  damage/durability recalculation. This ordering lets a newly declared battle be
  created and fight on the same crossed boundary while a same-tick debug stop releases
  troops before any further round.

- [ ] **Integrate issue #71's combat resources** — after #71 lands, use its canonical
  damage/durability resource ids through `ResourceDefinitions`. Require a present,
  positive durability value before dividing and fail with a clear invariant error if
  the prerequisite state is absent; do not silently substitute battle-specific
  defaults. Add integration fixtures that seed the same resource entities/lifecycle
  #71 uses.

- [ ] **Add focused battle-system tests** — create
  `src/Game.Tests/WarBattleSystemTests.cs` covering: pair-count capacity/floor/base;
  initiative max/tie/cost; primary and centroid fallback targeting; ownership,
  occupation, active-target, fewer-than-three, and no-target cases; multi-slot fill;
  force allocation ceiling/random bounds/clamps; zero-recruit and both-zero battles;
  same-boundary first round; multiple elapsed boundaries; strike order and remaining
  troops; casualty formula/floor/ceiling/clamp; round-winner/tie initiative; exhaustion
  and winner; ±10 progress and ±100 clamp; finished target reuse/history retention;
  all four occupation outcomes/version bump; exact proportional population loss;
  survivor return; and fixed-seed reproducibility.

- [ ] **Add save/load and `GameLogic` integration tests** — extend
  `src/Game.Tests/SaveLoadRoundTripTests.cs` with active and finished war/battle
  snapshots, asserting capacity, initiative resources, target, force troops,
  casualties, state, and winner survive and do not resolve twice. Add
  `src/Game.Tests/GameLogicWarBattleTests.cs` to drive declare, elapsed hours, finish,
  and debug stop through `GameLogic.Commands`/`Update`, including a high-speed update
  that crosses several rounds.

- [ ] **Run validation** — run the topology Python tests, regenerate province assets
  twice and verify no second diff, run `dotnet build src/GlobalStrategy.Core.sln`, run
  the full `dotnet test src/GlobalStrategy.Core.sln` suite, and run
  `git diff --check`. Unity Editor validation is not required because this plan
  changes no scene, prefab, MonoBehaviour, UXML, or USS asset.

## Tests

- `scripts/utils/tests/test_generate_provinces_topology.py` (new): geometry-level
  shared-segment versus point-contact adjacency, symmetry, stable ordering, centroid
  output.
- `src/Game.Tests/ProvinceProcessorTests.cs` and
  `src/Game.Tests/ProvinceConfigTests.cs`: topology metadata extraction and committed
  config invariants.
- `src/Game.Tests/ProvinceTopologyTests.cs` (new): immutable lookup and distance
  behavior.
- `src/Game.Tests/ResourceMutationsTests.cs` (new): exact set/clamped changes for
  recruits, initiative, and province population.
- `src/Game.Tests/ProvinceOccupationTests.cs`: shared mutation/version behavior.
- `src/Game.Tests/WarsTests.cs`: declaration capacity/initiative and stop cleanup.
- `src/Game.Tests/WarBattleSystemTests.cs` (new): battle initiation, targeting,
  commitment, rounds, finish effects, history, and deterministic RNG.
- `src/Game.Tests/SaveLoadRoundTripTests.cs`: active/finished persistence and
  idempotence.
- `src/Game.Tests/GameLogicWarBattleTests.cs` (new): command/update ordering and
  elapsed-hour processing.
- Full solution: `dotnet build src/GlobalStrategy.Core.sln` then
  `dotnet test src/GlobalStrategy.Core.sln`.

## Constitution Check

No conflicts found:

- **Rendering** — no rendering pipeline or material changes.
- **ECS for all game logic** — every live war/battle/force/resource/occupation change
  remains ECS state and every rule lives under `src/`; generated topology is immutable
  config input.
- **VContainer as sole DI** — no service or singleton is introduced. `GameLogic`
  constructs the immutable topology from already injected config and owns the shared
  seeded RNG.
- **UI Toolkit only** — no UI is in scope.
- **Plan before implement** — this plan is the approval gate; no implementation files
  are changed by this pass.
- **Spec before plan** — the owner approved the colocated `spec.md`, including the two
  selected topology decisions, before this plan was written.
- **File organisation** — the plan is colocated at
  `Docs/Specs/26_07_29_16_war-progress/plan.md`.
- **Assembly structure** — no `Assets/Scripts/` feature folder or asmdef is added;
  core `src/` project inclusion follows the existing SDK-style projects.
- **C# code style** — planned files use tabs, braces, `_`-prefixed private fields, and
  no redundant access modifiers.

Use the implement skill after this plan is approved and issue #71's combat-resource
contract is available.

## Addendum: post-implementation review (2026-07-30)

Owner code review asked whether the battle code needs its own resource-mutation
concept given the existing Instant `ResourceEffect`/`ResourceSystem` pipeline, then
requested splitting the 442-line `WarBattleSystem` for readability and wiring battle
resource changes into the animation pipeline (option B: keep `ResourceMutations`,
add `ResourceChange`).

- **`ResourceMutations` is kept.** Instant `ResourceEffect` is not equivalent: it is
  processed by `ResourceSystem.Update`, which runs once per tick before
  `WarBattleSystem` and is not called again afterward, so an effect enqueued during
  slot filling would not be visible to that same tick's later allocations/initiator
  choices or to the same-boundary round. `ResourceMutations.TryApplyClampedDelta`
  keeps the required synchronous read-then-write semantics.
- **Split as C# `partial` files, not separate GameLogic-invoked systems.** The
  `Update` loop alternates `FillSlots`/`ProcessRound` per crossed hour bucket so a
  battle finishing mid-loop frees its slot for the same bucket's next fill; if fill
  and round were independent systems each invoked once per `GameLogic.Update`, this
  interleaving would break for multi-hour catch-up (e.g. a large time skip). The
  single `WarBattleSystem.Update` entry point (still the only one `GameLogic` calls)
  now delegates to `WarBattleFill.cs`, `WarBattleRounds.cs`, `WarBattleSettlement.cs`,
  `WarBattleSupport.cs` — plain `partial` pieces of the same type, not systems calling
  systems.
- **`ResourceChange` wiring.** Every `RequireDelta` call (initiative charge, recruit
  commit, round-initiative award, survivor return, population casualties) now also
  emits a `ResourceChange` with a `war_`-prefixed `EffectId`, mirroring
  `DeductActionCostSystem`'s mutate-then-notify pattern, skipped when the applied
  delta is zero. `GameLogic.Update` was reordered so `WarBattleSystem.Update` runs
  after `CleanupActionEffectsSystem.Update` (previously it ran before) — otherwise
  cleanup would sweep the same tick's newly created battle `ResourceChange` entities
  before `VisualStateConverter` ever saw them.
