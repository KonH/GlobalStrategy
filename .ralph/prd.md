# Ralph PRD — Resources Visual Update

Make the shared country and organization resource summaries consume a config-ordered presentation catalog while preserving existing ownership, initialization, values, effects, persistence, and update behavior. Headless automation will implement the core target-aware resource model, configuration, Unity-side view script, and regression coverage; Unity asset, image, styling, import, and visual work remains explicitly deferred. Source: [approved spec and plan](../Docs/Specs/26_07_21_18_resources-visual-update/).

## How this file works

- The loop implements the first task with `"passes": false`, verifies it via its `gate`, flips the flag, commits, and repeats.
- Tasks must be **atomic** (one logical change), **verifiable** (the `gate` decides pass/fail — a shell command, or a Unity MCP check: `refresh_unity` + empty error console), and **ordered** (dependencies first).
- When every task has `"passes": true`, the loop stops.

## Tasks

```json
[
	{
		"category": "resource-config",
		"description": "Add presentation-order and seed-target metadata to the resource configuration model and loaders.",
		"steps": [
			"Update src/Game.Configs/ResourceConfig.cs with DisplayWhitelist, a ResourceSeedTarget enum containing exactly Character, Province, Country, and Org, and a backward-compatible ResourceDefinition.SeedTarget defaulting to Country.",
			"Add a target-filtered lookup or enumeration helper while retaining FindResource for presentation lookup.",
			"Add JsonStringEnumConverter to the shared System.Text.Json options in src/Core.Configs.IO/FileConfig.cs so readable seedTarget names load consistently in the headless path.",
			"Add focused loader/config tests for named enum deserialization and the legacy Country default."
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": true
	},
	{
		"category": "resource-initialization",
		"description": "Route config-backed resource initialization through seed targets without changing specialized values or effects.",
		"steps": [
			"Update src/Game.Main/InitSystem.cs so country, province, organization, and both character creation paths consume only definitions for their ResourceSeedTarget.",
			"Refactor collector-backed initialization to attach the existing effects and collectors to singular resources while retaining target-specific initial value sources and CountryEntry.InitialResources overrides.",
			"Keep organization gold based on OrganizationEntry.InitialGold and runtime opinion_<orgId> resources explicit and documented.",
			"Fail fast with the resource ID and target when a statically configured target/resource pairing has no supported initialization strategy."
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": true
	},
	{
		"category": "resource-config",
		"description": "Configure static resource seed targets and the ordered display catalog.",
		"steps": [
			"Update Assets/Configs/resource_config.json with displayWhitelist ordered exactly as gold, country_population, country_score, org_score.",
			"Add complete displayed definitions and stable icon keys coin, country-population, country-score, and org-score.",
			"Set explicit targets for gold, country_population, country_score, recruits, population, org_score, power, charm, stinginess, and intrigue as specified by the plan.",
			"Preserve existing gold defaults and effects; use zero defaults and no generic effects for collector-backed definitions; keep character skill ranges and localization authoritative in CharacterConfig."
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": false
	},
	{
		"category": "unity-headless",
		"description": "Implement config-whitelist filtering, ordering, icon selection, and localized descriptions in ResourcesView.",
		"steps": [
			"Update Assets/Scripts/Unity/UI/ResourcesView.cs to iterate ResourceConfig.DisplayWhitelist, find matching current-state entries, omit absent entries, and preserve whitelist order.",
			"Apply resource-icon and resource-icon--<configured icon key> classes when metadata exists, while continuing to render values and tooltips when definitions or optional images are missing.",
			"Use ResourceDefinitions.Gold for gold formatting and preserve all existing numeric, effect, instant, control-income, and refresh behavior.",
			"Add the configured localized description immediately below the localized name in the tooltip when available, retaining raw-ID fallback for a missing definition.",
			"Unity-side compilation is unverified in full-env-headless and must be checked by a human with the Unity Editor open."
		],
		"gate": "No headless gate available; manual Unity Editor compilation required",
		"passes": false
	},
	{
		"category": "resource-tests",
		"description": "Add regression tests for target-correct, singular resource initialization and preserved special cases.",
		"steps": [
			"Extend src/Game.Tests/InitSystemTests.cs or add a focused adjacent test file covering target-correct ownership across countries, provinces, organizations, and both character sources.",
			"Assert country_population, country_score, recruits, population, and org_score are singular and retain their target-specific collector effects.",
			"Assert organization gold retains OrganizationEntry.InitialGold and its existing effect shape, and dynamic opinion resources remain unaffected.",
			"Assert unsupported static target/resource pairings fail with contextual resource ID and target information."
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": false
	},
	{
		"category": "verification",
		"description": "Run the full headless core verification suite.",
		"steps": [
			"Run the focused initialization tests after all implementation and regression-test changes are complete.",
			"Run the complete src/GlobalStrategy.sln test suite and resolve any regressions within the approved plan scope."
		],
		"gate": "dotnet test src/GlobalStrategy.sln",
		"passes": false
	}
]
```
