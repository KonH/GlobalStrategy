# Plan: Peace Resolution

## Spec

A player (or tester driving the simulation) needs wars that have drifted far enough toward one side to resolve automatically into a peace outcome — transferring some occupied territory, clearing occupation, moving gold, and shifting control — so wars end with observable conquest and spoils rather than only disappearing via an unconditional debug stop.

Depends on war-mechanics-core (`War` / `WarProgress` / `WarParticipant`, monthly attacker decay, debug declare/stop) plus existing `ProvinceOwnership` / `ProvinceOccupation`, org gold / country gold resources, and `ControlEffect` with shared `maxControlPool` of 100. Relations (Rival / Friend) are deliberately unchanged on peace.

Acceptance criteria (condensed):
- **Monthly peace chance** — on a month boundary, before attacker decay: if progress is strictly inside `(-100 + MinLose, 100 - MinWin)`, no roll; at lose/win band edges (`-100 + MinLose` / `100 - MinWin`) roll **1%**; at `-100` / `+100` roll **100%**; inside either band, chance grows **linearly** from 1% at the edge to 100% at the extreme. Failed roll: war continues, no side effects. Successful roll: full peace resolution then war destroyed (participants free). All band/chance endpoints configurable.
- **Winner / loser** — progress `> 0` → attacker wins; progress `< 0` → defender wins. Progress `0` never fires on the monthly path (outside both bands).
- **Province transfer** — eligible set = loser-owned provinces occupied by **any non-loser** country; draw one uniform fraction in `[PeaceProvinceTransferMin, PeaceProvinceTransferMax]` (defaults 10%–30%); transfer count = ceiling of `eligible × fraction`, capped by eligible count; prefer provinces closer to **winner-owned province centroid**; then clear occupation on **every** province owned by either participant.
- **Gold spoils** — amount `D × G` where `D` = calendar month boundaries crossed since declaration and `G` = gold-per-month (default 100); same-month peace → `D = 0`; collect from loser-side orgs proportional to control in loser country (debt allowed); payout to winner-side orgs proportional to control in winner; remainder (including full amount when no controlling orgs) to **country gold**.
- **Control shifts** — winner country: each controlling org `+peaceWinnerControlIncreaseFraction` of its own control (default 0.05), top-control-first, clamp org to `[0, 100]` and country total to `maxControlPool`; loser country: each controlling org `−peaceLoserControlDecreaseFraction` of its own control (default 0.10), same ordering/clamps; no orgs → no-op.
- **Lifecycle** — after consequences, destroy war + participants (same end-state as today's stop). Debug `StopWar` routes through **full** peace resolution (winner by progress sign). Debug `StopWar` at progress `0`: clear occupation + destroy war only (skip transfer/gold/control). Relations unchanged. All tunables on `game_settings.json` / `GameSettings`.

Full detail lives in `spec.md`; this plan follows it rather than re-deriving design.

## Goal

Add backend peace resolution — monthly chance + shared outcome application — wired so both the month-boundary path and debug `StopWar` use one resolution helper, without UI and without changing declare-war rules or relations.

## Approach

### Shared resolution helper (not a System)

Extract outcomes into `Wars.ResolvePeace(...)` (or a sibling static helper `PeaceResolution` in `src/Game.Systems/` called only from `Wars` / `GameLogic`). It applies province transfer → occupation clear → gold → control, then destroys the war entity and both `WarParticipant`s. Neither path calls another `*System.Update`.

`WarSystem` stays **decay-only**. Month-boundary orchestration lives in `GameLogic.Update`:

1. `Wars.TryResolvePeaceByChance(_world, previousTime, currentTime, _rng, settings, provinceCenters, maxControlPool)` — on month boundary only: for each active war, compute chance from **current** progress, roll `_rng`, on success call `ResolvePeace` (war destroyed → naturally skipped by decay).
2. `WarSystem.Update(...)` — decay remaining wars.

Debug `StopWar` becomes `Wars.StopWar(..., currentTime, rng, settings, provinceCenters, maxControlPool)` and always routes through `ResolvePeace` (with the progress-`0` short path).

### Declared-at timestamp

`War` currently has only `WarId`. Add `[Savable] DateTime DeclaredAt`, set in `DeclareWar` from `currentTime`. **Breaking for existing saves** — acceptable at this game stage; note in implement PR. Duration `D = (current.Year - declared.Year) * 12 + (current.Month - declared.Month)` (count of calendar month-index steps since declaration; same-month → `0`).

### RNG

Reuse `GameLogic`'s `System.Random _rng` (seeded via `GameLogicContext.RngSeed`), same pattern as `DiscoverCountrySystem` / `SetCountryRelationSystem` / `DrawCardSystem`. Pass `Random` into chance + transfer-fraction draws. Tests construct `new Random(fixedSeed)` for determinism.

### Config (`GameSettings` + `game_settings.json`)

| Property | JSON key | Default |
|---|---|---|
| `PeaceMinLoseBand` | `peaceMinLoseBand` | `20` |
| `PeaceMinWinBand` | `peaceMinWinBand` | `20` |
| `PeaceChanceMinPercent` | `peaceChanceMinPercent` | `1` |
| `PeaceChanceMaxPercent` | `peaceChanceMaxPercent` | `100` |
| `PeaceProvinceTransferMinPercent` | `peaceProvinceTransferMinPercent` | `10` |
| `PeaceProvinceTransferMaxPercent` | `peaceProvinceTransferMaxPercent` | `30` |
| `PeaceGoldPerMonth` | `peaceGoldPerMonth` | `100` |
| `PeaceWinnerControlIncreaseFraction` | `peaceWinnerControlIncreaseFraction` | `0.05` |
| `PeaceLoserControlDecreaseFraction` | `peaceLoserControlDecreaseFraction` | `0.10` |

Chance formula (lose band, `MinLose > 0`, progress in `[-100, -100 + MinLose]`):  
`edge = -100 + MinLose`, `t = (edge - progress) / MinLose`, `chance = MinPercent + t * (MaxPercent - MinPercent)`.  
Win band symmetric from `edge = 100 - MinWin` toward `+100`. Outside bands → no roll. Roll succeeds when `rng.NextDouble() * 100 < chance` (or equivalent `< chance/100`).

### Province centroid

`ProvinceEntry` has no coordinates today. Add `CenterLon` / `CenterLat` to `ProvinceEntry`; extend `ProvinceProcessor.Process` to compute a simple lon/lat centroid for relative ranking:
- `Polygon`: average vertices of the exterior ring (`coordinates[0]`).
- `MultiPolygon`: average exterior-ring vertices across **all** polygons (or average per-polygon centroids), never only the first polygon.

Handle both types in `ProvinceProcessorTests`. Regenerate shipped `province_config.json` only (see Agent Steps) — do not run full `Game.Configs.Loader` `Program.Main`. `GameLogic` builds a `Dictionary<string, (double Lon, double Lat)>` once from `ProvinceConfig` and passes it into peace helpers. If the winner owns zero provinces, skip distance preference and pick by stable `provinceId` ordinal order.

### Gold

Org and country gold already exist as `Resource` + `ResourceOwner` with `ResourceDefinitions.Gold` (orgs seeded in `InitSystem`; countries via initial resources). No new treasury component. Peace gold mutation must **allow negative** (debt) — do not use `ApplyDebugChangeGold`'s `Max(0, …)` clamp. Prefer a small private adjust helper that mutates an existing `ResourceDefinitions.Gold` row or creates one if missing:
- orgs → `new ResourceOwner(orgId, OwnerType.Org)` (default Org is fine if explicit)
- country remainder → `new ResourceOwner(countryId, OwnerType.Country)` (must not use the Org default)

Proportional org shares use exact doubles; floating remainder after org attribution goes to the country.

### Control

Reuse `ControlQuery.GetOrgControlInCountry` / `GetTotalControlInCountry` and `ControlQuery.ReduceOrgControlInCountry` for loser cuts.
For winner boosts: snapshot org totals, sort descending (tie-break `string.CompareOrdinal` on orgId), and for each org compute
`desired = Round(orgTotal * fraction, AwayFromZero)`, then
`delta = Min(desired, maxControlPool - GetTotalControlInCountry(...), 100 - orgTotal)` using **live** country total after each prior boost.
Only then apply `delta` (via `ApplyChangeControl` is fine for storage, but **pre-clamp** as above — do not trust its internal room math when `base_*` / multi-effect control exists). Skip `delta == 0`. Do not invent control rows when a country has no controlling orgs.
Add a PeaceResolution test with `base_*` present and country total near `maxControlPool` that asserts total stays ≤ 100 after winner boosts.

### Existing tests

`WarsTests.stop_war_hard_deletes_war_and_both_participants` and the GameLogic debug stop test use empty worlds (no provinces/control/gold) or minimal `GameLogic` fixtures — they must still pass: progress stays `0` on declare, so StopWar takes the clear-occupation + destroy path with no transfer/gold/control work. Update signatures/call sites; keep assertions on war deletion. `WarSystemTests` unchanged in behavior (decay-only).

## Agent Steps

- [x] **Add `DeclaredAt` to `War`** — in `src/Game.Components/War.cs`, add `[Savable] public DateTime DeclaredAt;` on `War`. Set it in `Wars.DeclareWar` from `currentTime`. Document that existing saves without the field are incompatible (acceptable at this stage).
- [x] **Add peace config knobs** — add the nine properties from the Approach table to `src/Game.Configs/GameSettings.cs` (defaults as tabled) and matching keys to `Assets/Configs/game_settings.json` beside `attackerWarProgressDecayPerMonth`.
- [x] **Add province centers to config** — add `CenterLon` / `CenterLat` (`double`) to `ProvinceEntry`; extend `ProvinceProcessor.Process` (Polygon + MultiPolygon); update `ProvinceProcessorTests`.
- [x] **Regenerate shipped `province_config.json` only** — invoke `ProvinceProcessor.Process` on the geometry FeatureCollection already at `Assets/Configs/provinces_1880.json` (same schema as the intermediate) and write **only** `Assets/Configs/province_config.json`. Do **not** run full `Game.Configs.Loader` `Program.Main` for this change (it rebuilds country/map outputs from `world_1880.json`). Update `.claude/rules/unity/province_config_generator.md` so Stage 2’s written fields include `centerLon` / `centerLat`.
- [x] **Implement peace chance pure helper** — e.g. `Wars.ComputePeaceChancePercent(double progress, GameSettings settings) : double` returning `0` outside bands, else the linear 1%→100% (config endpoints) formula; unit-testable with no world.
- [x] **Implement `Wars.ResolvePeace`** — signature takes `World`, war id (or country id resolved to war), `DateTime currentTime`, `Random rng`, `GameSettings`, province-center lookup, `maxControlPool`. Steps in order: (1) resolve attacker/defender/progress/`DeclaredAt`; (2) if progress `== 0`, clear occupation for both participants' owned provinces and destroy war entities, return; (3) determine winner/loser by progress sign; (4) eligible occupied-loser provinces → draw `percent` uniformly in inclusive `[PeaceProvinceTransferMinPercent, PeaceProvinceTransferMaxPercent]` → `fraction = percent / 100.0` → `count = Min(eligible.Count, Ceiling(eligible.Count * fraction))` → sort by distance to winner-owned centroid (tie-break `CompareOrdinal(provinceId)`; if winner owns zero provinces or all centers missing, stable id order only) → `ChangeOwner` to winner; (5) clear occupation on all provinces owned by either participant via `ProvinceOccupationSystem.ClearOccupier`; (6) gold `D × G` proportional collect/payout + country remainder (debt allowed; country remainder uses `OwnerType.Country`); (7) control +/− fractions top-first with live pool/org pre-clamps; (8) destroy war + participants. Never touches `CountryRelation`. Never calls another `*System.Update`.
- [x] **Implement `Wars.TryResolvePeaceByChance`** — month-boundary gate identical to `WarSystem`/`ControlSystem`; for each war with progress in a band, roll chance on **pre-decay** progress; on success call `ResolvePeace`. Collect war ids to resolve first if mutating while iterating archetypes.
- [x] **Retarget `Wars.StopWar` through resolution** — change `StopWar` to accept `currentTime`, `rng`, `settings`, province centers, `maxControlPool` and call `ResolvePeace` (progress-`0` short path included). Keep `bool` return (false if country not in a war).
- [x] **Wire `GameLogic`** — cache province-center dictionary from `ProvinceConfig` (constructor or first init). Before `WarSystem.Update`, call `Wars.TryResolvePeaceByChance(...)`. Update the `DebugStopWarCommand` loop to pass `_rng`, `GameSettings`, centers, `MaxControlPool`, and `currentTime`. Leave `WarSystem.Update` decay-only immediately after the chance pass.
- [x] **Add / update unit tests** — see Tests section; keep existing empty-world StopWar deletion tests green under the progress-`0` short path; add focused peace chance, transfer, gold, control, and monthly orchestration tests.
- [x] **Run the full test suite** — use the `dotnet-test` skill against `src/GlobalStrategy.Core.sln`.

## User Steps

None. Backend-only (`src/` + config JSON); no Unity Editor scene/asset/visual verification required for this feature.

## Tests

- **`WarsTests.cs` (update)** — `StopWar` / GameLogic debug stop still delete war entities on progress `0` with empty worlds; update call signatures. Optionally assert occupation clear when provinces/occupation rows exist at progress `0`.
- **`PeaceChanceTests.cs` (new)** — band exterior → 0; lose/win edges → `PeaceChanceMinPercent`; ±100 → `PeaceChanceMaxPercent`; mid-band linear interpolation; progress `0` → 0.
- **`PeaceResolutionTests.cs` (new)** — winner/loser by progress sign; eligible occupied-loser transfer with ceiling + centroid preference (fixture centers); zero eligible → no ownership change but occupation clear still runs; gold `D × G` with org proportions + country remainder + debt; control +5%/−10% top-first with pool clamp; progress-`0` StopWar skips transfer/gold/control; relations untouched.
- **`WarPeaceMonthTests.cs` (new or extend `WarSystemTests`)** — on month boundary, chance evaluated before decay; failed roll leaves war and then decay applies; successful roll destroys war and that war is not decayed; non-boundary tick does nothing for chance or decay.
- **`ProvinceProcessorTests.cs` (update)** — regenerated entries expose non-default `CenterLon`/`CenterLat` for a fixture polygon.
- Existing `WarSystemTests` remain valid (decay-only contract unchanged).

## Constitution Check

No conflicts found — plan aligns with all principles:
- **ECS for all game logic** — all new behavior (`Wars` peace helpers, `DeclaredAt`, config) lives under `src/`; no MonoBehaviour touched.
- **VContainer is the sole DI mechanism** — no new service registration; helpers stay static and are called from `GameLogic`, same as existing `Wars` / `WarSystem`.
- **UI Toolkit only** — no UI; feature is backend-only per spec Out of Scope.
- **Plan before implement** — this plan is the gate; no code changes until approved.
- **Spec before plan for feature work** — `spec.md` already exists in this folder.
- **File organisation** — plan lives at `Docs/Specs/26_07_29_16_peace-resolution/plan.md` beside the existing spec.
- **One asmdef per feature folder** — no new `Assets/Scripts/` feature folder; `src/` assemblies are unaffected by that rule.
- **C# code style** — tabs, brace-on-same-line, `_`-prefixed private fields, no redundant access modifiers, matching existing `Wars.cs` / `WarSystem.cs` / `ControlQuery.cs` precedent.
- **No system-to-system calls** — `WarSystem` remains decay-only; peace chance + resolution are `Wars` helpers orchestrated from `GameLogic` (or called from `StopWar`), never `*System.Update` from another system.

Use the implement skill to start working on the plan or request changes.
