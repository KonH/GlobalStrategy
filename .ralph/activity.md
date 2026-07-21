# Ralph Activity Journal

Append-only log written by each loop iteration. Newest entry last.

---

## 2026-07-22 — Add presentation-order and seed-target resource metadata

Task: "Add presentation-order and seed-target metadata to the resource configuration model and loaders."

**What I changed:**
- Confirmed `src/Game.Configs/ResourceConfig.cs` provides `DisplayWhitelist`, the exact
  `ResourceSeedTarget` values, the legacy `Country` default, target-filtered
  `FindResources`, and retained `FindResource` presentation lookup.
- Confirmed `src/Core.Configs.IO/FileConfig.cs` registers `JsonStringEnumConverter`.
- Confirmed `src/Game.Tests/ResourceConfigTests.cs` covers named enum deserialization,
  target filtering, whitelist loading, and the legacy `Country` default.
- Marked the task passed after rerunning its verification gate.

**Gate:** With `DOTNET_ROLL_FORWARD=Major`, `dotnet test src/GlobalStrategy.Core.sln`
exited 0. Evidence: `ECS.Tests.dll` passed 34/34, `ECS.Viewer.Tests.dll` passed
16/16, and `Game.Tests.dll` passed 322/322 (372 total, 0 failed).

The next iteration should begin with the resource-initialization task. This machine has
.NET 10 installed for net8 test hosts, so retain `DOTNET_ROLL_FORWARD=Major` for gates.

---

## 2026-07-22 — Route resource initialization through seed targets

Task: "Route config-backed resource initialization through seed targets without changing specialized values or effects."

**What I changed:**
- Updated `src/Game.Main/InitSystem.cs` so country, province, organization, and both
  character creation paths enumerate only definitions for their `ResourceSeedTarget`.
- Made collector-backed country population/score/recruits, province population, and
  organization score attach their existing effects to the singular dispatched resource.
- Preserved country initial-resource overrides, province/config and character/config value
  sources, explicit organization gold from `OrganizationEntry.InitialGold`, and documented
  dynamic `opinion_<orgId>` creation as an explicit runtime exception.
- Added contextual fail-fast handling for unsupported resource ID/target pairings.
- Updated resource-config fixtures in `src/Game.Tests/InitSystemTests.cs`,
  `src/Game.Tests/ProvinceOwnershipTests.cs`, `src/Game.Tests/CharacterInitTests.cs`, and
  `src/Game.Tests/CharacterVisualStateTests.cs` so their expected resources are explicitly
  targeted under the new initialization contract.
- Bumped `ProjectSettings/ProjectSettings.asset` bundle version from 1.22 to 1.23 per the
  repository commit rules.

**Gate:** With `DOTNET_ROLL_FORWARD=Major`, `dotnet test src/GlobalStrategy.Core.sln --no-restore`
exited 0. Evidence: `ECS.Tests.dll` passed 34/34, `ECS.Viewer.Tests.dll` passed
16/16, and `Game.Tests.dll` passed 322/322 (372 total, 0 failed).

The next iteration should configure the static seed targets and ordered display catalog in
`Assets/Configs/resource_config.json`. Keep the explicit organization-gold exception; its
shared gold definition remains country-targeted.

---

## 2026-07-22 — Configure static resource seed targets and ordered display catalog

Task: "Configure static resource seed targets and the ordered display catalog."

**What I changed:**
- Updated `Assets/Configs/resource_config.json` with the exact ordered display whitelist:
  `gold`, `country_population`, `country_score`, and `org_score`.
- Added complete presentation definitions and stable icon keys for all four displayed
  resources.
- Added explicit seed targets for country, province, organization, and character resources.
- Preserved gold's 100 default value and monthly base-income effect; collector-backed
  resources use zero defaults and no generic effects, while character skill definitions
  contain only their target metadata.
- Bumped `ProjectSettings/ProjectSettings.asset` bundle version from 1.23 to 1.24 per the
  repository commit rules.

**Gate:** With `DOTNET_ROLL_FORWARD=Major`, `dotnet test src/GlobalStrategy.Core.sln`
exited 0. Evidence: `ECS.Tests.dll` passed 34/34, `ECS.Viewer.Tests.dll` passed
16/16, and `Game.Tests.dll` passed 322/322 (372 total, 0 failed).

The next iteration should implement whitelist filtering, ordering, icon selection, and
localized descriptions in `Assets/Scripts/Unity/UI/ResourcesView.cs`. Its PRD gate states
that manual Unity Editor compilation is required; follow the Ralph blocker rules if that
gate cannot be executed.

---

## 2026-07-22 — Implement ordered resource presentation in ResourcesView (blocked)

Task: "Implement config-whitelist filtering, ordering, icon selection, and localized descriptions in ResourcesView."

**What I changed:**
- Updated `Assets/Scripts/Unity/UI/ResourcesView.cs` to walk
  `ResourceConfig.DisplayWhitelist`, omit missing state entries, and retain configured
  presentation order.
- Applied the base `resource-icon` class and the configured icon-key modifier only when
  icon metadata exists, while leaving values and tooltips functional without definitions
  or images.
- Replaced raw gold ID comparisons with `ResourceDefinitions.Gold` without changing value,
  effect, instant, control-income, or refresh behavior.
- Added the localized resource description immediately below the localized tooltip name,
  retaining the raw resource-ID fallback when no definition exists.
- Bumped `ProjectSettings/ProjectSettings.asset` bundle version from 1.24 to 1.25 for the
  required commit.

**Gate:** BLOCKED. The required gate is Unity Editor compilation via `refresh_unity`, then
`read_console(types=["error"])`. This session exposes neither Unity MCP tool (the available
tool catalog contains no Unity Editor, `refresh_unity`, or `read_console` entry), so the
gate could not be run. `git diff --check` exited 0, but that is not a substitute for the
required Unity gate. The task remains `passes: false` and needs manual visual checking after
Unity compilation succeeds.

The next iteration should rerun the Unity compilation/error-console gate first. If it is
clean, mark this task passed and record the output; do not redo the implementation.

---
