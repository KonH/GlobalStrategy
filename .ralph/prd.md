# Ralph PRD — discoverAndControl threshold parameter

Make the `discoverAndControl` bot feature's discover-vs-control priority configurable via a
`discoveredCountriesAvailableControl` parameter: once the org has discovered at least that many
countries, the bot should prefer playing a control card in an already-discovered country over
playing a further discovery card. Below the threshold, discovery stays prioritized (matching the
feature's original always-discover-first behavior, which is also the default when the parameter is
omitted). The eval batch searches a grid of threshold values and must land on one that beats the
feature-disabled baseline.

## How this file works

- The loop implements the first task with `"passes": false`, verifies it via its `gate`, flips the flag, commits, and repeats.
- Tasks must be **atomic** (one logical change), **verifiable** (the `gate` decides pass/fail — a shell command, or a Unity MCP check: `refresh_unity` + empty error console), and **ordered** (dependencies first).
- When every task has `"passes": true`, the loop stops.

## Tasks

```json
[
	{
		"category": "bot-feature",
		"description": "Add a discoveredCountriesAvailableControl threshold parameter to DiscoverAndControlFeature.",
		"steps": [
			"Add a constructor overload on DiscoverAndControlFeature accepting IReadOnlyDictionary<string, double> parameters, reading 'discoveredCountriesAvailableControl' (default double.MaxValue so omitting the parameter preserves the original always-discover-first order).",
			"In Tick, when obs.DiscoveredCountryIds.Count >= threshold, try the control-card scan first and fall back to the discover-card scan; otherwise keep discover-first, control-fallback.",
			"Add unit tests in src/Game.Tests/DiscoverAndControlFeatureTests.cs covering: below-threshold keeps discover-first (default params), and at/above-threshold prefers control."
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": true
	},
	{
		"category": "bot-feature",
		"description": "Wire parameters through BotFeatureRegistry so eval profiles can set the threshold.",
		"steps": [
			"Change BotFeatureRegistry.CreateDefault()'s discoverAndControl registration from 'parameters => new DiscoverAndControlFeature()' to 'parameters => new DiscoverAndControlFeature(parameters)'."
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": true
	},
	{
		"category": "bot-feature-eval",
		"description": "Run the discoverAndControl eval batch (grid search over discoveredCountriesAvailableControl in Docs/BotFeatures/discoverAndControl/eval_config.json) and iterate until it beats the feature-disabled baseline.",
		"gate": "dotnet run --project src/Game.Evals -- --feature discoverAndControl",
		"steps": [
			"Run the gate command.",
			"If it passes (exit 0): mark this task's passes: true and stop iterating.",
			"If it fails: read Docs/BotFeatures/discoverAndControl/eval_history.json (latest entry) and .ralph/activity.md. Pick one concrete improvement - a logic change, different targeting, or an adjusted threshold grid in eval_config.json - journal the change and why in .ralph/activity.md, then re-run the gate.",
			"If Docs/BotFeatures/discoverAndControl/eval_history.json has reached attempt 5 or more and the gate still fails: journal budget exhaustion in .ralph/activity.md, leave passes: false, and end the iteration - do not keep retrying past the budget."
		],
		"passes": false
	},
	{
		"category": "bot-feature",
		"description": "Adopt the winning discoveredCountriesAvailableControl value as the feature's effective default.",
		"steps": [
			"Read the latest passing entry in Docs/BotFeatures/discoverAndControl/eval_history.json and note its winning parameter set's discoveredCountriesAvailableControl value.",
			"Pin that value into Docs/BotFeatures/discoverAndControl/eval_config.json's candidateFeatures parameters (add a candidateFeatures entry for discoverAndControl with that value if not already present) so future eval runs default to the validated threshold.",
			"Leave the C# default (double.MaxValue, i.e. discover-first) unchanged - callers that want the validated threshold opt in via profile parameters; this task only pins the eval config's own candidate default."
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": false
	}
]
```
