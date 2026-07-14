# Ralph Activity Journal

Append-only log written by each loop iteration. Newest entry last.

---

## 2026-07-14 — Add OwnerType.Province enum case

Task: "Add OwnerType.Province enum case" (src).

Change: Added `Province` as a fourth case to `OwnerType` enum in
`src/Game.Components/OwnerType.cs`, after `Character`.

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` → Build succeeded, 0 Warning(s), 0 Error(s).

Notes for next iteration: Next task is "Add ProvinceEntry.Population field" in
`src/Game.Configs/ProvinceConfig.cs`. No blockers encountered.
