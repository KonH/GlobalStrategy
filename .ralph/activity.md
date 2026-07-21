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
