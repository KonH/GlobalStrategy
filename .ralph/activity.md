# Ralph Activity Journal

Append-only log written by each loop iteration. Newest entry last.

---

## 2026-07-21 — Add presentation-order and seed-target resource metadata

Task: "Add presentation-order and seed-target metadata to the resource configuration model and loaders."

**What I changed:**
- `src/Game.Configs/ResourceConfig.cs` — added the ordered `DisplayWhitelist`, the
  `ResourceSeedTarget` enum (`Character`, `Province`, `Country`, and `Org`), a
  backward-compatible `ResourceDefinition.SeedTarget` default of `Country`, and
  `FindResources(ResourceSeedTarget)` while retaining `FindResource(string)`.
- `src/Core.Configs.IO/FileConfig.cs` — added `JsonStringEnumConverter` to the shared
  System.Text.Json options so readable `seedTarget` names load in the headless path.
- `src/Game.Tests/ResourceConfigTests.cs` — added focused coverage for named enum
  deserialization, target filtering, display-whitelist loading, and the legacy Country
  default for both programmatic and deserialized definitions.

**Gate:** The first literal `dotnet test src/GlobalStrategy.Core.sln` attempt compiled
successfully but its test hosts could not start because only Microsoft.NETCore.App 10.0.9
is installed and roll-forward was disabled. Re-ran the same gate with
`DOTNET_ROLL_FORWARD=Major`: `dotnet test src/GlobalStrategy.Core.sln` exited 0 —
`ECS.Tests.dll` passed 34/34, `ECS.Viewer.Tests.dll` passed 16/16, and
`Game.Tests.dll` passed 322/322 (372 total, 0 failed).

Marked this task's `passes` flag true. The next iteration should begin with the
resource-initialization task and use `DOTNET_ROLL_FORWARD=Major` when running net8 test
hosts on this machine.

---
