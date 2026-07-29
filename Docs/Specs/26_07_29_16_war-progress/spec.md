# Spec: War Progress

## Feature Intent

As a player, I want wars to advance through recurring province battles that consume recruits, cause population losses, and change occupation, so that a declared war produces an ongoing territorial conflict rather than remaining only a static relationship.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

- A war starts between its attacker and defender.
  - The war starts => each attacker-province/defender-province pair that shares a generated boundary segment is counted once; point-only contacts are excluded, and the maximum concurrent battle count is calculated once as `1 + floor(shared-border-pair-count / 5)`, using the configured values for the additive base and provinces-per-slot divisor.
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
  - No primary candidate exists => eligible enemy provinces are ranked by straight-line centroid distance to the closest province currently owned or occupied by the initiator, and the target is chosen randomly from the configured top three nearest provinces.
  - Fewer than three fallback candidates exist => every eligible fallback candidate participates in the random selection.
  - No target province is eligible => no targetless battle is created and the free battle slot remains empty until targeting is processed again.
- A target province has been selected for a new battle.
  - Each participating country commits troops => its base commitment ratio is `1 / (maximum concurrent battles + 1)`, multiplied independently by a random value from 0.9 through 1.1; that amount is rounded up, clamped to the country's available recruits, removed from recruits, and recorded as its troops in this battle.
  - A participating country has no available recruits => its force is still created with zero troops; the battle is not suppressed.
  - The battle is created => it records its war, target province, Active state, and each participating country's side, committed troops, and casualties starting at zero.
  - The battle is created while an in-game hourly boundary is being processed => its first round is processed on that same boundary.
- An active battle reaches an in-game hourly boundary.
  - Its round starts => the attacker or defender side is selected randomly to strike first.
  - A country strikes an opposing country => potential casualties equal the striking country's battle troops multiplied by its country damage divided by 300.
  - Casualties are calculated => the opposing country's durability coefficient is its country durability divided by 300, and real casualties are the greatest of: potential casualties divided by that coefficient and multiplied by a random value from 0.9 through 1.1; 1% of the opposing country's current battle troops; or 1; the result is rounded up and clamped to the opposing country's current battle troops.
  - The first strike is resolved => real casualties are added to the struck country's battle casualties and removed from its battle troops, then the opposing side performs its answering strike by the same rules using its remaining troops.
  - More than one in-game hour elapses in one simulation update => each elapsed hourly round is processed, so changing game speed does not skip battle rounds.
- A battle round has resolved both strikes.
  - Every country on one side has no battle troops remaining => the battle finishes and the opposite side is recorded as the Attacker or Defender winner.
  - Both sides still have battle troops and one side inflicted more casualties in that round => the battle remains active, that round-winning side's country gains 0.5 initiative using the configured gain, and the battle waits for its next hourly round.
  - Both sides still have battle troops and inflicted equal casualties in that round => the battle remains active and neither country gains round-winner initiative.
  - A battle finishes and frees a concurrent-battle slot => the war may initiate another battle until its active-battle limit is filled again.
- A battle finishes.
  - The attacker side wins => the war's progress moves 10 points toward the attacker, using the configured battle-progress value and clamping the total to 100.
  - The defender side wins => the war's progress moves 10 points toward the defender, using the configured battle-progress value and clamping the total to -100.
  - A participating country suffered casualties => its total province population is reduced by its rounded cumulative casualties, clamped to its available population and distributed proportionally across its provinces without changing the clamped total.
  - A participating country has surviving battle troops => those troops are returned to its recruits.
  - The target is owned by the winning country and is not occupied by the losing country => its occupation is unchanged.
  - The target is owned by the winning country and is occupied by the losing country => that occupation is cleared.
  - The target is owned by the losing country and is not occupied by the winning country => the winning country occupies it.
  - The target is owned by the losing country and is already occupied by the winning country => its occupation is unchanged.
  - Resolution completes => the Finished battle and all of its force/casualty records remain attached to the war for war history and result inspection; because only Active battles exclude a target, the province may be selected for a later battle when otherwise eligible.
- A war is stopped through the existing unconditional debug command while it has Active battles.
  - The war is stopped => every active battle's surviving troops are returned to its countries' recruits, no accumulated casualties are applied to population, no battle occupation result is applied, and the war's battle records are removed with the war.
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
  - Count each unordered attacker-owned/defender-owned neighboring province pair once when declaring the war. Generated adjacency exists only when the final simplified province boundaries share a non-zero-length segment; a point-only contact is not adjacency. Apply integer floor division by the configured provinces-per-slot divisor before adding the configured base count.
  - For fallback targeting, rank an eligible enemy province by the minimum straight-line distance from its generated centroid to the centroid of any province currently owned or occupied by the initiator. Break equal-distance ranks by ordinal province id, take up to the configured candidate count, then use the seeded RNG to select within that set.
  - `ProximityMapData` is country-level, is built from legacy country feature geometry in `src/Game.Main/InitSystem.cs`, and cannot answer province-border or province-nearness queries for this feature.
  - Static topology/distance data that is rebuilt from config at startup must be held in a non-`[Savable]` ECS component or plain immutable lookup. Runtime `ProvinceOwnership` and `ProvinceOccupation` components remain the authority for determining friendly/enemy territory, occupied approach provinces, and target occupation.
- **Country initiative and combat attributes**:
  - Add `war_initiative` to `ResourceDefinitions` in `src/Game.Configs/ResourceDefinitions.cs` and represent it with the existing persisted `Resource` + `ResourceOwner` country-resource shape. A country is currently allowed in at most one war, so country ownership does not make initiative ambiguous in this slice.
  - Country damage and durability are country resources supplied by issue #71. This feature reads those resources rather than introducing independent combat-attribute storage; issue #71 must establish valid positive values before battle calculations run.
  - Extend or add a plain resource mutation helper beside `ResourceQuery` so battle logic can atomically decrease recruits, return survivors, and update initiative without duplicating resource-entity scans.
- **Persisted battle state**:
  - Add a battle component model under `src/Game.Components/` with one `[Savable]` battle entity containing a deterministic battle id, `WarId`, target province id, `Active`/`Finished` state, and winner side (winner is meaningful only when state is `Finished`).
  - Store each country's `[Savable]` battle force/casualty values on separate `BattleId`-keyed relation entities: country id, `WarParticipantKind` side, troops, and cumulative casualties. Do not collapse these into one attacker scalar and one defender scalar.
  - Add `BattleState` under `src/Game.Common/`; reuse `WarParticipantKind` for Attacker/Defender side and winner semantics unless serialization constraints require a battle-specific equivalent.
- **Filling free slots and selecting targets**:
  - Add a dedicated ECS battle progression system under `src/Game.Systems/` whose single public `Update` entry point is invoked only by `GameLogic.Update`; it owns battle initiation, hourly rounds, and finish resolution without calling another system's `Update`/`Seed` entry point.
  - Count only `Active` battles for concurrency and for excluding target provinces. Use the per-war capacity captured at declaration. Retain `Finished` battle entities until their war is removed.
  - Build candidate collections in ordinal province-id order before seeded random selection. The stable ordering is required because `_rng` in `src/Game.Main/GameLogic.cs` is seedable through `GameLogicContext`, while ECS/config iteration order must not become an additional source of nondeterminism.
  - Primary targeting must query runtime ownership and occupation: an approach province qualifies when the initiator currently owns it or currently occupies it; a target qualifies only when it belongs to the enemy side, is not already occupied by the initiator, and has no Active battle.
- **Committing recruits as battle troops**:
  - Read and mutate country `recruits` through the existing country-owned `Resource` identified by `ResourceDefinitions.Recruits`.
  - Calculate each current participant's allocation independently from its recruits at the moment that battle is created, round it upward to a whole person, clamp it to available recruits, then persist troops and zero casualties on its battle-force entity. No side-wide aggregate may replace the per-country records.
  - Create force records even when their allocation is zero. A missing target is the only targeting condition that suppresses battle creation.
- **Hourly rounds and battle completion**:
  - `GameLogic.Update` must pass `_world`, `_previousTime`, `currentTime`, the shared `_rng`, static province topology/config, and the combat settings to the battle progression system. Process every crossed one-hour boundary rather than only comparing `previousTime.Hour` with `currentTime.Hour`.
  - Fill battle slots before processing each crossed hourly boundary, so a battle created for that boundary participates immediately. Resolve the seeded random first side, first strike casualties, answering strike, and second casualties in explicit sequence.
  - Round calculated casualties upward to a whole person and clamp troop removal so battle troops cannot become negative. Compare the two sides' casualties inflicted during that round to award initiative; an equal amount awards neither side.
  - Determine side exhaustion only after the round's required strike sequence has been applied. The data model should evaluate all per-country force records on a side even though there is exactly one today.
  - Existing `WarSystem.Update` keeps responsibility for the dependency's monthly `WarProgress` decay. Battle progression must not call `WarSystem.Update`.
- **Population, recruits, and occupation resolution**:
  - Province population is the persisted `population` `Resource` owned by province id. Use current runtime `ProvinceOwnership` to select the defeated country's provinces for proportional casualty distribution; occupation alone does not transfer ownership.
  - Apply each country's cumulative whole-person casualties once at finish, preserve the clamped total deduction while distributing it proportionally and deterministically across current owned provinces, then return that country's surviving troops to its `recruits` resource.
  - Resolve the four occupation cases against `ProvinceOwnership` and `ProvinceOccupation`, and bump `ProvinceOccupationVersion` whenever occupation changes so the existing `VisualStateConverter` and map coloring update naturally. Put shared occupation mutation in a plain ECS helper if needed to preserve the constitution's no-system-to-system-call rule.
  - Persist an explicit resolution-applied marker or transition the battle atomically so save/load and later ticks cannot deduct population or return troops more than once.
  - Move `WarProgress.Value` by the configured battle-progress amount: positive and clamped to `100` for an attacker win, negative and clamped to `-100` for a defender win.
  - Extend the existing debug-stop cleanup to return surviving troops from Active battles without applying their casualties or occupation outcomes before battle and war entities are removed.
- **Configuration**:
  - Add the war-battle numeric settings to `src/Game.Configs/GameSettings.cs` and `Assets/Configs/game_settings.json`: concurrent-battle additive base and shared-border divisor; attacker/defender initial initiative; initiation cost; non-final-round initiative gain; battle-progress gain; nearest-fallback candidate count; troop-ratio denominator offset and random bounds; battle-round interval; damage and durability divisors; casualty random bounds; minimum casualty fraction; and minimum absolute casualties.
  - The issue-specified defaults are respectively `1`, `5`, `1.0`, `0.0`, `1.0`, `0.5`, `10`, `3`, `1`, `0.9`/`1.1`, one hour, `300`, `300`, `0.9`/`1.1`, `0.01`, and `1`. Damage/durability resource defaults belong to issue #71.
- **Regression and deterministic coverage**:
  - Add focused tests under `src/Game.Tests/` for capacity calculation, initiative choice/ties, primary and fallback targeting, exclusion rules, slot refill, zero-troop battles, recruit transfer, same-boundary first rounds, both-strike ordering, casualty rounding/floors/clamps, round-winner initiative/ties, side exhaustion, battle-driven progress/clamps, finished-battle retention, all four occupation cases, proportional population loss, save/load idempotence, multiple elapsed hours, and reproducibility with a fixed seed.
  - Preserve the existing issue #69 tests for declaration guards and monthly `WarProgress` decay; extend debug-stop tests to cover survivor release without casualty or occupation resolution.

## Out of Scope

- War declaration rules, diplomacy requirements, peace negotiation, and natural war termination; this feature builds on issue #69's existing declaration and stop surfaces.
- Allies or more than one country on either side of a war. Per-country battle state is intentionally shaped for later extension, but participant creation and multi-country troop/target/damage distribution are not added here.
- Battle UI, animations, notifications, combat logs, or player-issued battle orders; battles progress automatically in ECS game logic.
- Ownership transfer of a province; battle outcomes change occupation only.
- New rendering or map-lens behavior beyond the existing response to `ProvinceOccupation` changes.

## Ambiguities

None — the owner selected neighboring attacker/defender province pairs for shared-border counting and closest owned-or-occupied territorial centroid distance for fallback targeting. Fewer than the configured three eligible fallback provinces means selecting among all remaining candidates; zero leaves the battle slot empty.
