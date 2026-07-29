# Spec: War Progress

## Feature Intent

As a player, I want wars to advance through recurring province battles that consume recruits, cause population losses, and change occupation, so that a declared war produces an ongoing territorial conflict rather than remaining only a static relationship.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

- A war starts between its attacker and defender.
  - The war starts => its maximum concurrent battle count is calculated once as `1 + shared-border-provinces / 5`, using the configured values for the additive base and provinces-per-slot divisor.
  - The war starts => it has no active battles before battle initiation is processed.
  - The war starts => the attacker country's initiative is 1.0 and the defender country's initiative is 0.0, using the configured initial values.
- A war has fewer active battles than its maximum concurrent battle count.
  - A new battle is initiated => the participating country with the greatest initiative initiates it; a random participating country is chosen when the greatest values are tied.
  - A country initiates a battle => its initiative decreases by 1.0, using the configured initiation cost.
  - More than one battle slot is free => initiation continues until the active-battle limit is reached or no valid target remains.
- A country is initiating a battle and enemy provinces are available.
  - An enemy province is already occupied by the initiator, or already has an active battle => that province is excluded as a target.
  - An otherwise eligible enemy province borders a province owned by the initiator or occupied by the initiator => it is a primary target candidate.
  - One or more primary candidates exist => the target province is chosen randomly from those candidates.
  - No primary candidate exists => the target is chosen randomly from the configured top three nearest enemy provinces that do not already have an active battle.
- A target province has been selected for a new battle.
  - Each participating country commits troops => its base commitment ratio is `1 / (maximum concurrent battles + 1)`, multiplied independently by a random value from 0.9 through 1.1; that amount is removed from the country's available recruits and recorded as its troops in this battle.
  - The battle is created => it records its war, target province, Active state, and each participating country's side, committed troops, and casualties starting at zero.
- An active battle reaches an in-game hourly boundary.
  - Its round starts => the attacker or defender side is selected randomly to strike first.
  - A country strikes an opposing country => potential casualties equal the striking country's battle troops multiplied by its country damage divided by 300.
  - Casualties are calculated => the opposing country's durability coefficient is its country durability divided by 300, and real casualties are the greatest of: potential casualties divided by that coefficient and multiplied by a random value from 0.9 through 1.1; 1% of the opposing country's current battle troops; or 1.
  - The first strike is resolved => real casualties are added to the struck country's battle casualties and removed from its battle troops, then the opposing side performs its answering strike by the same rules.
  - More than one in-game hour elapses in one simulation update => each elapsed hourly round is processed, so changing game speed does not skip battle rounds.
- A battle round has resolved both strikes.
  - Every country on one side has no battle troops remaining => the battle finishes and the opposite side is recorded as the Attacker or Defender winner.
  - Both sides still have battle troops => the battle remains active, the country on the winning side of that round gains 0.5 initiative using the configured gain, and the battle waits for its next hourly round.
  - A battle finishes and frees a concurrent-battle slot => the war may initiate another battle until its active-battle limit is filled again.
- A battle finishes.
  - A participating country suffered casualties => its total province population is reduced by those casualties, distributed proportionally across its provinces.
  - A participating country has surviving battle troops => those troops are returned to its recruits.
  - The target is owned by the winning country and is not occupied by the losing country => its occupation is unchanged.
  - The target is owned by the winning country and is occupied by the losing country => that occupation is cleared.
  - The target is owned by the losing country and is not occupied by the winning country => the winning country occupies it.
  - The target is owned by the losing country and is already occupied by the winning country => its occupation is unchanged.
- The same starting state, configuration, and random seed are used for two simulations.
  - Both simulations advance through the same elapsed hours => initiative ties, target choices, troop ratios, strike order, casualty variance, battle outcomes, and occupation results are identical.
- A game is saved while a war has active or finished battle state.
  - The save is loaded => battle capacity, initiatives, battle state, per-country troops and casualties, target provinces, and winners resume without being reset or applied twice.
- War battle processing uses numeric constants.
  - The game configuration changes one of those values => subsequent wars and battle calculations use the configured value rather than a hard-coded equivalent.

## Tech Notes

- **War start and concurrent-battle capacity**:
  - Extend the live ECS war model in `src/Game.Components/War.cs` with persisted per-war battle-capacity state composed onto the existing `War` entity; the maximum is captured when `Wars.DeclareWar` succeeds and is not recomputed as ownership or occupation changes.
  - The current dependency creates exactly one `WarParticipantKind.Attacker` and one `WarParticipantKind.Defender`. This feature's current acceptance behavior remains one country per side, while all battle troop/casualty state is keyed per country so a later allied-war slice is not forced into side-wide scalar state.
  - `Wars.DeclareWar` in `src/Game.Systems/Wars.cs` must initialize the two country initiative values and the persisted maximum battle count as part of the successful declaration transaction.
- **Province borders, shared-border counting, and nearest-target fallback**:
  - `ProvinceConfig` / `ProvinceEntry` in `src/Game.Configs/ProvinceConfig.cs` and the generated `Assets/Configs/province_config.json` currently carry no adjacency, centroid, or distance data. Add deterministic province topology and distance inputs through the province generation path (`scripts/utils/generate_provinces.py` and `src/Game.Configs.Loader/ProvinceProcessor.cs`) rather than using `ProximityMapData`.
  - `ProximityMapData` is country-level, is built from legacy country feature geometry in `src/Game.Main/InitSystem.cs`, and cannot answer province-border or province-nearness queries for this feature.
  - Static topology/distance data that is rebuilt from config at startup must be held in a non-`[Savable]` ECS component or plain immutable lookup. Runtime `ProvinceOwnership` and `ProvinceOccupation` components remain the authority for determining friendly/enemy territory, occupied approach provinces, and target occupation.
- **Country initiative and combat attributes**:
  - Add `war_initiative` to `ResourceDefinitions` in `src/Game.Configs/ResourceDefinitions.cs` and represent it with the existing persisted `Resource` + `ResourceOwner` country-resource shape. A country is currently allowed in at most one war, so country ownership does not make initiative ambiguous in this slice.
  - Country damage and durability need country-owned data readable by combat calculation. Their exact resource/config source and initial values cannot be finalized until the corresponding ambiguity is resolved; they do not exist in the current resource definitions or country config.
  - Extend or add a plain resource mutation helper beside `ResourceQuery` so battle logic can atomically decrease recruits, return survivors, and update initiative without duplicating resource-entity scans.
- **Persisted battle state**:
  - Add a battle component model under `src/Game.Components/` with one `[Savable]` battle entity containing a deterministic battle id, `WarId`, target province id, `Active`/`Finished` state, and winner side (winner is meaningful only when state is `Finished`).
  - Store each country's `[Savable]` battle force/casualty values on separate `BattleId`-keyed relation entities: country id, `WarParticipantKind` side, troops, and cumulative casualties. Do not collapse these into one attacker scalar and one defender scalar.
  - Add `BattleState` under `src/Game.Common/`; reuse `WarParticipantKind` for Attacker/Defender side and winner semantics unless serialization constraints require a battle-specific equivalent.
- **Filling free slots and selecting targets**:
  - Add a dedicated ECS battle progression system under `src/Game.Systems/` whose single public `Update` entry point is invoked only by `GameLogic.Update`; it owns battle initiation, hourly rounds, and finish resolution without calling another system's `Update`/`Seed` entry point.
  - Count only `Active` battles for concurrency and for excluding target provinces. Use the per-war capacity captured at declaration.
  - Build candidate collections in ordinal province-id order before seeded random selection. The stable ordering is required because `_rng` in `src/Game.Main/GameLogic.cs` is seedable through `GameLogicContext`, while ECS/config iteration order must not become an additional source of nondeterminism.
  - Primary targeting must query runtime ownership and occupation: an approach province qualifies when the initiator currently owns it or currently occupies it; a target qualifies only when it belongs to the enemy side, is not already occupied by the initiator, and has no Active battle.
- **Committing recruits as battle troops**:
  - Read and mutate country `recruits` through the existing country-owned `Resource` identified by `ResourceDefinitions.Recruits`.
  - Calculate each current participant's allocation independently from its recruits at the moment that battle is created, then persist troops and zero casualties on its battle-force entity. No side-wide aggregate may replace the per-country records.
- **Hourly rounds and battle completion**:
  - `GameLogic.Update` must pass `_world`, `_previousTime`, `currentTime`, the shared `_rng`, static province topology/config, and the combat settings to the battle progression system. Process every crossed one-hour boundary rather than only comparing `previousTime.Hour` with `currentTime.Hour`.
  - Resolve the seeded random first side, first strike casualties, answering strike, and second casualties in explicit sequence. Clamp troop removal so battle troops cannot become negative once the casualty rounding/clamping rule is clarified.
  - Determine side exhaustion only after the round's required strike sequence has been applied. The data model should evaluate all per-country force records on a side even though there is exactly one today.
  - Existing `WarSystem.Update` keeps responsibility for the dependency's monthly `WarProgress` decay. Battle progression must not call `WarSystem.Update`.
- **Population, recruits, and occupation resolution**:
  - Province population is the persisted `population` `Resource` owned by province id. Use current runtime `ProvinceOwnership` to select the defeated country's provinces for proportional casualty distribution; occupation alone does not transfer ownership.
  - Apply each country's cumulative casualties once at finish, preserve the total deduction subject to available population and the clarified rounding rule, then return that country's surviving troops to its `recruits` resource.
  - Resolve the four occupation cases against `ProvinceOwnership` and `ProvinceOccupation`, and bump `ProvinceOccupationVersion` whenever occupation changes so the existing `VisualStateConverter` and map coloring update naturally. Put shared occupation mutation in a plain ECS helper if needed to preserve the constitution's no-system-to-system-call rule.
  - Persist an explicit resolution-applied marker or transition the battle atomically so save/load and later ticks cannot deduct population or return troops more than once.
- **Configuration**:
  - Add the war-battle numeric settings to `src/Game.Configs/GameSettings.cs` and `Assets/Configs/game_settings.json`: concurrent-battle additive base and shared-border divisor; attacker/defender initial initiative; initiation cost; non-final-round initiative gain; nearest-fallback candidate count; troop-ratio denominator offset and random bounds; battle-round interval; damage and durability divisors; casualty random bounds; minimum casualty fraction; and minimum absolute casualties.
  - The issue-specified defaults are respectively `1`, `5`, `1.0`, `0.0`, `1.0`, `0.5`, `3`, `1`, `0.9`/`1.1`, one hour, `300`, `300`, `0.9`/`1.1`, `0.01`, and `1`. Damage/durability initial values remain unresolved.
- **Regression and deterministic coverage**:
  - Add focused tests under `src/Game.Tests/` for capacity calculation, initiative choice/ties, primary and fallback targeting, exclusion rules, slot refill, recruit transfer, both-strike ordering, casualty floors, side exhaustion, all four occupation cases, proportional population loss, save/load idempotence, multiple elapsed hours, and reproducibility with a fixed seed.
  - Preserve the existing issue #69 tests for declaration guards, debug stop, and monthly `WarProgress` decay.

## Out of Scope

- War declaration rules, diplomacy requirements, peace negotiation, and natural war termination; this feature builds on issue #69's existing declaration and stop surfaces.
- Allies or more than one country on either side of a war. Per-country battle state is intentionally shaped for later extension, but participant creation and multi-country troop/target/damage distribution are not added here.
- Battle UI, animations, notifications, combat logs, or player-issued battle orders; battles progress automatically in ECS game logic.
- Ownership transfer of a province; battle outcomes change occupation only.
- New rendering or map-lens behavior beyond the existing response to `ProvinceOccupation` changes.

## Ambiguities

- [NEEDS CLARIFICATION: What exactly counts as `shared-border-provinces` for `1 + shared-border-provinces / 5`: distinct provinces on one side, distinct provinces across both sides, or attacker/defender neighboring province pairs; is division floored; and what geometry source/tolerance defines a shared border (especially point contacts, islands, and simplified-geometry gaps)?]
- [NEEDS CLARIFICATION: For the fallback "top 3 nearest enemy provinces," nearest to which origin (the initiating country, its nearest owned/occupied province, its capital, or the active front), which distance metric is intended, and what happens when fewer than three or zero enemy provinces remain eligible?]
- [NEEDS CLARIFICATION: When a round does not end the battle, how is its "battle winner" chosen for the +0.5 initiative gain—greater casualties inflicted in that round, greater remaining troops, the side that struck first, or another rule; what happens on a tie?]
- [NEEDS CLARIFICATION: Where do each country's damage and durability values come from, what are their initial/default values, and may durability be zero or negative?]
- [NEEDS CLARIFICATION: Do battle or occupation results change the existing `WarProgress.Value`; if so, by what configured formula, in which direction, and with what clamping? Issue #80 names war progress but specifies no battle-to-progress rule.]
- [NEEDS CLARIFICATION: Are `Finished` battles retained for war history/save inspection, removed immediately after resolution, or retained only up to a configured limit; if retained, can their target provinces immediately be selected for later battles?]
- [NEEDS CLARIFICATION: Are troops and casualties continuous doubles or rounded whole people; at which steps are values rounded; must allocated troops and real casualties be clamped to available recruits/current battle troops; and how is any proportional-population rounding remainder assigned?]
- [NEEDS CLARIFICATION: If a country has zero recruits, no valid target, or insufficient remaining population, should its free battle slot remain empty, should a zero-troop battle be created and immediately lost, or should another participant/target be attempted?]
- [NEEDS CLARIFICATION: When the existing unconditional debug stop command ends a war that still has Active battles, should it release committed troops, apply accumulated casualties, resolve no occupation, or discard all battle state without consequences?]
- [NEEDS CLARIFICATION: Does a battle created while processing an hourly boundary fight its first round on that same boundary or begin on the next in-game hour?]
