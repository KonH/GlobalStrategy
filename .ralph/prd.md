# Ralph PRD — Win / Lose Logic

End an in-progress game when one participating organization satisfies the configured composite control objective, select one deterministic winner, publish the player's result, preserve the complete final tick, freeze later gameplay, and persist the terminal ECS state across save/load. Source: [approved spec and plan](../Docs/Specs/26_07_22_11_win-lose-logic/).

## How this file works

- The loop implements the first task with `"passes": false`, verifies it via its `gate`, flips the flag, commits, and repeats.
- Tasks must be **atomic** (one logical change), **verifiable** (the `gate` decides pass/fail — a shell command, or a Unity MCP check: `refresh_unity` + empty error console), and **ordered** (dependencies first).
- When every task has `"passes": true`, the loop stops.

## Tasks

```json
[
	{
		"category": "completion-config",
		"description": "Add the recursive completion-condition configuration model and configured default objective.",
		"steps": [
			"Add src/Game.Configs/CompletionConditionConfig.cs with Type, numeric Value, and recursive Members fields shaped for world-level completion conditions.",
			"Add GameSettings.CompletionCondition with the approved backward-compatible default tree.",
			"Configure Assets/Configs/game_settings.json as an any node containing total_control at 0.8 and full_control_countries at 15.",
			"Keep completion inputs separate from the action-specific ExpressionContext."
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": true
	},
	{
		"category": "completion-conditions",
		"description": "Implement the completion-condition contract, leaves, recursive composition, and validation.",
		"steps": [
			"Add ICompletionCondition and CompletionConditionContext carrying IReadOnlyWorld, candidate organization ID, the ordinal available-country set, and MaxControlPool.",
			"Add AnyCompletionCondition, TotalControlCondition, FullControlCondition, and CompletionConditionFactory under src/Game.Systems/.",
			"Extend OrgMetrics with one available-country-filtered aggregation path shared by both leaves, summing multiple matching contributions once per organization and country.",
			"Use inclusive thresholds, include zero-control available countries in total capacity, exclude unavailable countries and other organizations, and fail safely for zero countries or capacity.",
			"Fail fast with contextual errors for unknown types, empty any groups, non-positive capacity, and invalid thresholds."
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": false
	},
	{
		"category": "completion-state",
		"description": "Add savable ECS completion and per-organization outcome components.",
		"steps": [
			"Add the savable GameCompletion component with IsCompleted and WinnerOrganizationId.",
			"Add OrganizationGameResult values InProgress, Winner, and Loser and the savable OrganizationGameOutcome component with ParticipationOrder and Result.",
			"Keep outcomes attached directly to Organization entities."
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": false
	},
	{
		"category": "completion-initialization",
		"description": "Initialize the completion singleton and ordered participant outcomes.",
		"steps": [
			"Update InitSystem to create exactly one in-progress GameCompletion entity.",
			"Attach an in-progress OrganizationGameOutcome to every participant using its ResolveParticipatingOrgs index as ParticipationOrder.",
			"Preserve configured initialization order independently of later archetype moves."
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": false
	},
	{
		"category": "completion-system",
		"description": "Implement deterministic, idempotent winner selection and outcome assignment.",
		"steps": [
			"Add GameCompletionSystem and return immediately when the singleton is already complete.",
			"Evaluate participating organizations against the ECS Country set in ParticipationOrder, using ordinal organization ID only as a defensive fallback for duplicate malformed orders.",
			"Choose the first qualifier, write the singleton winner, and atomically mark it Winner and every other participant Loser.",
			"Leave all results in progress when there are no countries, no organizations, or no qualifier."
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": false
	},
	{
		"category": "completion-projection",
		"description": "Expose the player-facing completion result through VisualState.",
		"steps": [
			"Add GameResult values InProgress, Win, and Lose plus GameCompletionState with IsCompleted, WinnerOrganizationId, and Result.",
			"Add the completion state to VisualState.",
			"Update VisualStateConverter to project the completion singleton relative to the current player without deciding game rules."
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": false
	},
	{
		"category": "completion-orchestration",
		"description": "Wire completion evaluation, final publication, terminal freezing, and bot guards into the game loop.",
		"steps": [
			"Build and cache the configured condition tree and completion singleton in GameLogic.",
			"Evaluate completion after all tick mutations but before command clearing and the final VisualState conversion so the whole winning tick is published.",
			"On later terminal updates, process only pending SaveGameCommand work, discard gameplay commands, and skip simulation, animation ticks, and visual republication.",
			"Expose GameLogic.IsCompleted, make RecordBotAction a no-op after completion, and update BotSession to skip terminal decision ticks while still calling GameLogic.Update."
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": false
	},
	{
		"category": "completion-loading",
		"description": "Reconcile completion state, participant order, and immediate projection when loading snapshots.",
		"steps": [
			"After LoadSystem.Apply, clear pre-load commands, refresh singleton/entity IDs, and restore _previousTime from GameTime.",
			"Create a missing in-progress completion singleton and reconstruct missing organization outcomes from participating context, the single-player fallback, then unmatched loaded organizations in ordinal order.",
			"Preserve valid saved orders and fail fast on duplicate organization IDs or irreconcilable duplicate orders.",
			"Evaluate the configured condition once against the restored world and immediately run VisualStateConverter.Update with zero delta so loaded terminal results are observable without advancing simulation."
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": false
	},
	{
		"category": "completion-condition-tests",
		"description": "Add configuration and condition-tree regression coverage.",
		"steps": [
			"Add CompletionConditionTests for recursive any composition, configured threshold changes, both leaves, inclusive 0.8 and 15 boundaries, and below-threshold states.",
			"Cover multiple same-country contributions, exclusion of other organizations and unavailable countries, zero-control countries in capacity, and zero-country safety.",
			"Test the camelCase tree through FileConfig and Newtonsoft production-equivalent deserializers, including recursive members, numeric values, explicit any, absent-key default, and contextual errors for null or invalid trees."
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": false
	},
	{
		"category": "completion-system-tests",
		"description": "Add winner-selection and game-loop completion integration coverage.",
		"steps": [
			"Add GameCompletionSystemTests for no participants, no qualifier, simultaneous qualifiers in stable participation order, exactly one winner, all other participants losing, and repeated evaluation preserving the terminal result.",
			"Add GameCompletionLogicTests for player Win, Lose, and InProgress projection, completion after all mutations in the winning tick, and publication of the complete final state.",
			"Verify later updates and queued gameplay commands freeze time, resources, control, actions, logs, and outcomes.",
			"Add BotSession integration coverage proving terminal ticks emit no bot commands, callbacks, or BotActionLog entries."
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": false
	},
	{
		"category": "completion-persistence-tests",
		"description": "Add savable discovery and save/load completion regression coverage.",
		"steps": [
			"Extend SavableDiscoveryTests for GameCompletion and OrganizationGameOutcome.",
			"Cover in-progress and terminal round trips, winner/loser/order preservation, immediate loaded projection, and terminal loaded-game freeze.",
			"Cover legacy snapshots without completion components, participant entities restored through different archetypes, reconstructed order, and isolation from commands queued before load."
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": false
	},
	{
		"category": "verification",
		"description": "Run the full core test suite and refresh the Unity-consumed release assemblies.",
		"steps": [
			"Run dotnet test src/GlobalStrategy.Core.sln and resolve regressions within the approved plan scope.",
			"Run the Release build after tests so the tracked Unity-consumed assemblies under Assets/Plugins/Core are refreshed."
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": false
	}
]
```
