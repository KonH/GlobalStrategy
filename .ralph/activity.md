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
