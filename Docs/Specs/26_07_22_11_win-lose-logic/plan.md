# Plan: Win / Lose Logic

## Spec summary

Source: `Docs/Specs/26_07_22_11_win-lose-logic/spec.md`.

End an in-progress game when one participating organization satisfies the configured composite objective: at least 80% of the total control capacity across all available ECS countries, or full control in at least 15 distinct ECS countries. Select exactly one winner in initialization order, mark every other participant as a loser, publish `InProgress`/`Win`/`Lose` for the player, preserve the complete final tick, and freeze subsequent simulation and gameplay commands. The terminal result must live in savable ECS state so new games, loaded games, headless callers, and Unity consumers share one source of truth.

## Goal

Add a config-driven completion-condition tree, ECS-only condition evaluation and terminal state, deterministic winner selection, final visual-state projection, post-completion freezing, and save/load support without adding presentation UI or embedding game rules in Unity code.

## Approach

### 1. Config-driven completion-condition tree

- Add `src/Game.Configs/CompletionConditionConfig.cs` with a recursive `CompletionConditionConfig` (`Type`, numeric `Value`, and `Members`) shaped like the existing `ExpressionNode` tree, but specific to world-level completion inputs.
- Add `GameSettings.CompletionCondition` in `src/Game.Configs/GameSettings.cs` and configure `Assets/Configs/game_settings.json` as an `any` node containing `total_control` with value `0.8` and `full_control_countries` with value `15`. The values and OR composition therefore come from config rather than `GameLogic.Update` constants.
- Do not extend action-specific `ExpressionContext` with aggregate world queries. `ExpressionNode` evaluates numeric action-local inputs such as control/opinion, while completion leaves require the ECS world, organization identity, country set, and control-pool capacity. Reuse its recursive config/composition pattern, but keep the incompatible evaluation contexts separate.

### 2. Shared completion-condition contract and leaves

- Add `src/Game.Systems/ICompletionCondition.cs` with a shared `ICompletionCondition.IsMet(CompletionConditionContext context)` contract. The context carries `IReadOnlyWorld`, the candidate organization ID, an ordinal set of available ECS country IDs, and `MaxControlPool`.
- Add `AnyCompletionCondition`, `TotalControlCondition`, and `FullControlCondition` under `src/Game.Systems/`, plus a `CompletionConditionFactory` that validates and builds the recursive runtime tree from `CompletionConditionConfig`. Unknown types, empty `any` groups, non-positive control capacity, and invalid thresholds fail fast with contextual errors during `GameLogic` construction.
- Extend `OrgMetrics` with one shared aggregation path that accepts the available-country set. `TotalControlCondition` sums only matching-country control and divides it by `availableCountryIds.Count * MaxControlPool`; it returns false for zero countries/capacity and uses `>=` so exactly 80% qualifies. `FullControlCondition` uses that same filtered per-country aggregation, counts distinct available countries whose organization total is at least `MaxControlPool`, and uses `>=` so exactly 15 qualifies. Contributions from other organizations and effects naming unavailable/nonexistent countries are excluded, while multiple effects for the same organization/country are summed once.

### 3. Savable ECS completion and organization outcomes

- Add `src/Game.Components/GameCompletion.cs` with `[Savable] GameCompletion { bool IsCompleted; string WinnerOrganizationId; }`.
- Add `src/Game.Components/OrganizationGameOutcome.cs` with `[Savable] OrganizationGameOutcome { int ParticipationOrder; OrganizationGameResult Result; }`, where `OrganizationGameResult` is `InProgress`, `Winner`, or `Loser`. Attach it directly to each `Organization` entity rather than creating ID-indexed parallel entities.
- In `src/Game.Main/InitSystem.cs`, create one in-progress `GameCompletion` entity and attach an in-progress `OrganizationGameOutcome` to each participant using the index from `ResolveParticipatingOrgs`. This explicitly preserves the configured initialization order across archetype moves and save/load round trips.
- Add `src/Game.Systems/GameCompletionSystem.cs`. Called only by the top-level orchestrator, it returns immediately when already complete, counts ECS `Country` entities, evaluates every participating organization ordered by `ParticipationOrder` (ordinal organization ID only as a defensive fallback for malformed duplicate orders), chooses the first qualifying organization, writes the singleton winner, and assigns `Winner`/`Loser` to every organization atomically. With no countries, no organizations, or no qualifier, it leaves every result in progress.

### 4. Tick ordering, final publication, and freeze

- In `src/Game.Main/GameLogic.cs`, build the condition tree once from `GameSettings`, cache the `GameCompletion` entity alongside the existing singleton entity IDs, and invoke `GameCompletionSystem.Update` after all time, resource, control, debug, action, discovery, hand, and cleanup mutations for the tick, but before `_commandAccessor.Clear()` and the final `VisualStateConverter.Update`. Thus a winning action's entire tick is present in the terminal world and in the one final published state.
- At the start of later `Update` calls, detect persisted `GameCompletion.IsCompleted`. Skip time and every gameplay/debug/action system, discard queued gameplay commands, and do not rerun the visual converter or animatable ticks, leaving time, resources, control, actions, logs, winner, losers, and the published result unchanged.
- Permit only pending `SaveGameCommand` handling in the terminal branch before clearing the command buffer. Saving is persistence rather than gameplay, and this makes the completed ECS snapshot reachable without allowing any simulation mutation; manual save success/failure may still update `SaveResult`, but completion data and gameplay state remain frozen.
- Expose a read-only `GameLogic.IsCompleted` query for non-Unity orchestrators. Update `src/Game.Bots/BotSession.cs` to skip bot decision ticks once terminal while still calling `GameLogic.Update` so terminal save commands can be handled and other queued commands discarded. Defensively make `GameLogic.RecordBotAction` a no-op after completion so bot callbacks cannot mutate the savable action log outside the guarded update loop.

### 5. Player-facing final state

- In `src/Game.Main/VisualState.cs`, add `GameResult` (`InProgress`, `Win`, `Lose`) and a `GameCompletionState` exposing `IsCompleted`, `WinnerOrganizationId`, and `Result`; add it as `VisualState.Completion`.
- Extend `src/Game.Main/VisualStateConverter.cs` to receive the completion singleton entity and project its ECS state relative to the current player organization: incomplete is `InProgress`, matching winner ID is `Win`, and a different winner is `Lose`. This is a read-only projection; winner selection and outcome assignment remain in ECS.

### 6. Initialization, loading, and snapshots

- `[Savable]` discovery in `SaveSystem`/`LoadSystem` automatically includes `GameCompletion` and `OrganizationGameOutcome`, including the winner, all loser assignments, and participation order; no result fields are duplicated into `SaveHeader`.
- In `GameLogic.LoadState`, clear commands queued against the pre-load world immediately after `LoadSystem.Apply`, refresh all singleton/entity IDs, set `_previousTime` from the restored `GameTime`, and reconcile legacy snapshots before evaluation. Create a missing in-progress completion singleton. For missing organization outcomes, reconstruct order from `GameLogicContext.ParticipatingOrganizationIds` when supplied (or `InitialOrganizationId` for the single-player fallback), then append unmatched loaded organizations in ordinal ID order; preserve valid saved orders and fail fast on duplicate organization IDs or irreconcilable duplicate orders. Evaluate the configured condition once against the restored world. Existing terminal snapshots are a no-op because `GameCompletionSystem` is idempotent; legacy/in-progress snapshots already at a threshold become terminal before any simulation advances.
- Still inside `LoadState`, run `VisualStateConverter.Update(0f, ...)` once after completion evaluation so a loaded terminal result is immediately observable. Subsequent `Update` calls take the terminal branch and cannot resume it. A loaded non-terminal game continues normally from its restored time and outcomes.

## Steps

### Agent Steps

- [ ] Add `CompletionConditionConfig` and the default/configured `completionCondition` tree in `GameSettings.cs` and `game_settings.json`.
- [ ] Add the completion-condition contract, context, factory, `AnyCompletionCondition`, `TotalControlCondition`, and `FullControlCondition` in `src/Game.Systems/`.
- [ ] Add savable `GameCompletion`, `OrganizationGameOutcome`, and `OrganizationGameResult` components in `src/Game.Components/`.
- [ ] Update `InitSystem` to create the completion singleton and initialize each participant's ordered outcome.
- [ ] Implement `GameCompletionSystem` with available-country-filtered aggregate control queries, zero-country safety, stable single-winner selection, and idempotent winner/loser assignment.
- [ ] Add `GameResult`/`GameCompletionState` to `VisualState` and project ECS completion state in `VisualStateConverter`.
- [ ] Wire condition construction, completion evaluation, final publication, terminal command filtering/freeze, singleton refresh, and load-time reconciliation in `GameLogic`; gate `BotSession` and direct bot-log mutation on completion.
- [ ] Extend config, condition, bot-session integration, savable-discovery, and save/load tests; run `dotnet test src/GlobalStrategy.Core.sln`, then `dotnet build src/GlobalStrategy.Core.sln -c Release` to refresh the Unity-consumed assemblies under `Assets/Plugins/Core/`.

### User Steps

None. This feature changes only headless-testable `src/` logic and JSON configuration; the spec explicitly excludes new UI and other Unity asset work.

## Tests

- Extend `src/Game.Tests/ExpressionNodeTests.cs` only if a shared composition helper is extracted; otherwise leave action-expression behavior untouched and cover recursive `any` composition in a new `src/Game.Tests/CompletionConditionTests.cs`.
- In `CompletionConditionTests`, cover below-threshold states, both individual OR leaves, exact 0.8 and 15 equality, multiple control effects in one country, exclusion of other organizations' control, exclusion of effects naming unavailable/nonexistent countries from both leaves, zero-control countries in total capacity, zero-country safety, and configured threshold/composition changes.
- Add `src/Game.Tests/GameCompletionSystemTests.cs` for no participants/no qualifier, stable simultaneous qualifiers in participation order, exactly one winner, every other organization losing, and repeated evaluation preserving the first terminal result.
- Add `src/Game.Tests/GameCompletionLogicTests.cs` using `MultiOrgTestSupport` for player `Win`/`Lose`/`InProgress`, completion after all mutations in the winning tick, final visual publication, and subsequent updates/queued gameplay commands leaving time, resources, control, actions, logs, and outcomes unchanged. Add a `BotSession` integration case proving a post-completion tick emits no bot commands/callbacks or `BotActionLog` entries.
- Extend `src/Game.Tests/SavableDiscoveryTests.cs` for both new savable components and `src/Game.Tests/SaveLoadRoundTripTests.cs` (or focused completion persistence tests) for in-progress and terminal snapshots, winner/loser/order preservation, immediate loaded projection, terminal loaded-game freeze, and legacy snapshots without completion components. Include player/bot entities restored through different archetypes plus a queued-preload-command case to verify reconstructed ordering and command isolation.
- Test the real camelCase completion tree through both production-equivalent deserializers: `FileConfig<GameSettings>` (`System.Text.Json`) and Newtonsoft as used by `TextAssetConfig<GameSettings>`. Cover recursive members, numeric values, explicit `any`, the absent-key default, and contextual failure for an explicit null or invalid tree.
- Run `dotnet test src/GlobalStrategy.Core.sln`, then `dotnet build src/GlobalStrategy.Core.sln -c Release` so all dependent tests pass and the tracked Unity-consumed DLLs are refreshed. No Unity Editor or visual verification is required.

## Constitution Check

- **ECS game logic:** Completion conditions, winner selection, terminal state, organization outcomes, and freezing are implemented in `src/Game.Components`, `src/Game.Systems`, and the `GameLogic` orchestrator. `VisualStateConverter` only projects decided ECS state; no MonoBehaviour owns game rules.
- **Dependency injection:** No new global service or mutable static singleton is introduced. The immutable runtime condition tree is owned by each `GameLogic` instance and built from its injected config source.
- **UI:** No UI is added; the result is exposed through existing `VisualState` for future consumers.
- **Planning/spec discipline:** This plan implements the approved `spec.md` in the same `Docs/Specs/26_07_22_11_win-lose-logic/` folder before source changes.
- **Assembly/file organization:** New types stay in the existing `Game.Configs`, `Game.Components`, `Game.Systems`, and `Game.Main` projects; no Unity feature folder or `.asmdef` is needed.
- **C# style:** Implementation will use tabs, same-line braces, `_`-prefixed private fields, no redundant access modifiers, and fail-fast configuration errors.

Use the implement skill to start working on the plan or request changes.
