# Plan: Multi-Org Headless Simulation Foundation

## Spec

Source: `Docs/Specs/50_multi-org-headless-simulation/spec.md`.

**Intent.** Part 1 of 3 of the bot-org initiative: make the game logic support multiple simultaneously active organizations, a seedable deterministic RNG, and a fast non-interactive console simulation runner that emits structured JSON results — so later parts (bot API in part 2, eval harness in part 3) can run reproducible headless competitions between orgs. No bot decision logic, no scoring formula, no eval statistics in this part.

**Scoring reference.** Per the spec's note, this part emits only raw per-org metrics (total control, gold). Any later derived score must build on the country-scoring plan (`Docs/Specs/47_country-scoring/`, branch `claude/country-scoring-spec-tpf83x`): derived non-`[Savable]` component, month-boundary-gated static system with forced `Recompute`, global coefficient in `game_settings.json`, `GetScore`-style query. Nothing in this plan contradicts those patterns; the `OrgMetrics` helper introduced here is a raw-aggregation query in the same static-system style, deliberately not a score.

**Key acceptance criteria (design targets):**
- `GameLogicContext` gains a participating-org-ids input; when unspecified it defaults to exactly the existing single `InitialOrganizationId` — Unity behaviour byte-for-byte preserved, no `Assets/` change. For each participating org (validated against `organizations.json`), `InitSystem` creates the full entity set the player org gets today: `Organization` entity, `gold` `Resource` with `InitialGold`, base `ControlEffect` (`base_{orgId}`) in `HqCountryId`, org character slots (`master` + `InitialAgentSlots` agents, filled like the player org's, **including `IsAvailable`** per the resolved decision), org `CardDeck`/`CardHand` with initial hand drawn from the org's pool, and per-country `CardDeck`s with country action cards — all keyed by that org's id.
- An unknown participating org id fails fast: logged via `IGameLogger` and thrown (headless exits non-zero) — never silently continuing with fewer orgs.
- Two orgs each pushing `PlayCardActionCommand` resolve the full card pipeline independently (condition, cost against own gold, effect creation, hand removal, redraw) with no cross-org interference; `ControlSystem`'s proportional monthly income split for ≥2 orgs in one country gets a regression test; `ChangeControlCommand`/`DebugChangeGoldCommand` mutate only the named org; global commands stay runner-owned; `VisualState` keeps reflecting only the single view org.
- Discovery becomes per-org: `DiscoverCountryEffect` carries the acting org's id; each org has its own discovered set; proximity weighting anchors per-org (view org → selected player country as today; other orgs → own `HqCountryId`; no anchor → uniform); `VisualState`'s discovered-countries data keeps its shape, sourced from the view org's set; single-org Unity behaviour observably unchanged. **No backward compatibility with pre-change save files is required** — old saves may lose discovery progress or fail to load, and no migration/attribution logic is written (resolved decision, superseding the earlier compatibility criterion). Saves written by the *new* code must still round-trip; the save system (header, storage, serializer, autosave) is otherwise untouched.
- Optional integer RNG seed on `GameLogicContext`; seeded `Random` in `GameLogic` when provided, unseeded otherwise. Paired same-seed runs must produce identical end states (per-org total control, per-org gold, per-country control breakdown, game date, hand contents) **and** identical per-month timelines — verified by xunit tests, which are the acceptance gate; any other nondeterminism the tests expose is in scope to fix. `_sessionId` and `SaveSystem`'s `DateTime.UtcNow` are excluded (save-only; headless never saves).
- ConsoleRunner is fixed (it currently crashes: `data/` lacks `game_settings.json`/`resource_config.json`/`organizations.json`) and loads the **full** config set from a configurable directory defaulting to `Assets/Configs/`; the drifted `data/` snapshot is removed. Headless mode: tight `Update` loop, fixed `deltaTime`, no `Console.ReadLine`, no sleeps, no `ViewerServer`; default 1 game day per tick; a guard rejects any tick configuration that could advance more than one month per tick (month-boundary systems detect only a single crossing); stop conditions: end game-date (`dateReached`), max ticks (`maxTicks`), always-active wall-clock timeout (`timeout`, results still written); exit 0 on completion, non-zero with stderr message on startup/config failure. Interactive mode preserved as the default behaviour. Default org set: all orgs in `organizations.json`; CLI flag narrows it.
- Results: single camelCase JSON document with seed, full session parameters, `tickCount`, `endReason`, final game date, per-org end metrics (total control = sum of that org's `ControlEffect` values, gold), and a once-per-game-month timeline of per-org control/gold. No derived score. Same seed + params → identical metric values across runs; no wall-clock fields in the file.

**Out of scope:** `VisualState` changes (no new fields), save-system changes beyond the discovery component's shape, backward compatibility with pre-change save files, bot logic / observation facade / command enforcement (part 2), eval harness / scoring formula (part 3), any `Assets/` change (DLL rebuild into `Assets/Plugins/Core/` excepted), win conditions, removing the interactive runner mode.

## Goal

Extend `GameLogicContext`/`InitSystem`/`GameLogic` so a world can be initialized with N fully-equipped organizations from an explicit participating-org list (defaulting to today's single org, preserving Unity behaviour exactly), replace the global `IsDiscovered` country flag with per-org `DiscoveredCountry` entities (new saves round-trip; pre-change saves are deliberately abandoned per the resolved decision), seed `GameLogic`'s `Random` from an optional context seed, and rebuild `src/Game.ConsoleRunner` into a dual-mode tool — the existing interactive/ViewerServer mode plus a new `--headless` mode that loads configs from `Assets/Configs/`, drives `GameLogic.Update` in a tight one-game-day-per-tick loop under date/tick/timeout stop conditions, and writes a deterministic camelCase JSON results document with per-org end metrics and a monthly timeline. All work is in `src/`; the paired same-seed determinism tests in `src/Game.Tests` are the acceptance gate.

## Approach

### 1. Context inputs (`src/Game.Main/GameLogicContext.cs`)

Append two optional constructor parameters (after `province`, keeping every existing call site source-compatible):

```csharp
int? rngSeed = null,
IReadOnlyList<string>? participatingOrganizationIds = null
```

exposed as `public int? RngSeed { get; }` and `public IReadOnlyList<string>? ParticipatingOrganizationIds { get; }`. Unity's `GameLifetimeScope` is untouched — it simply never passes them, so defaults preserve current behaviour exactly.

### 2. Seeded RNG (`src/Game.Main/GameLogic.cs`)

Constructor: `_rng = context.RngSeed.HasValue ? new Random(context.RngSeed.Value) : new Random();`. No other RNG exists in the simulation path (verified: `InitSystem`, `DrawCardSystem`, `DiscoverCountrySystem`, and `GameLogic`'s debug-cycle helpers all consume the single `_rng`).

**Known nondeterminism audit** (from reading `GameLogic`, `InitSystem`, `TimeSystem`, `ResourceSystem`, `ControlSystem`, `DrawCardSystem`, `DiscoverCountrySystem`, `CreateActionEffectSystem`):
- `new Random()` — fixed by the seed (above).
- `_sessionId = Guid.NewGuid()` and `SaveSystem`'s `DateTime.UtcNow` — cosmetic/save-only, explicitly excluded by the spec; headless runs never save (`Storage`/`Serializer` stay null in ConsoleRunner).
- `Dictionary`/`HashSet` iteration (`ControlSystem.byCountry`, `CreateActionEffectSystem.entityCountry`, `InitSystem.CreateCountryActionEntities.charsByCountryAndRole`) — .NET dictionary enumeration order is reproducible for identical insertion/removal sequences, and insertion order here derives from deterministic archetype iteration, so paired runs with identical inputs should already match. The determinism tests are the gate: **if** they expose divergence, the in-scope fix is to sort the affected iteration ordinally by key (e.g. sort `byCountry` keys in `ControlSystem` before applying gains) in that specific system only. The plan does not pre-emptively reorder working code.
- Multi-org RNG consumption order is pinned by iterating participating orgs in their declared list order everywhere (see §3).

Also in `GameLogic`:
- Constructor retains `readonly Dictionary<string, string> _hqCountryByOrgId` built once from `context.Organization.Load()` (`OrganizationId → HqCountryId`), for per-org discovery anchors — this avoids changing the `[Savable]` `Organization` component's shape (the spec allows only the discovery component's savable shape to change).
- `RefreshSingletonEntities()`: replace `_orgEntity = FindEntityWith<Organization>()` with a view-org-aware lookup — prefer the `Organization` entity whose `OrganizationId == _context.InitialOrganizationId` (when non-empty), falling back to the first found. Single-org worlds are unchanged; multi-org worlds get a well-defined view org instead of "whichever archetype comes first". The headless runner passes `initialOrganizationId` = first participating org so `VisualState`/`SaveSystem.BuildSnapshot` stay coherent.

### 3. Multi-org initialization (`src/Game.Main/InitSystem.cs`)

At the top of `Run`, resolve the effective participating list:

```csharp
static List<OrganizationEntry> ResolveParticipatingOrgs(GameLogicContext context, OrganizationConfig orgConfig)
```

- If `context.ParticipatingOrganizationIds` is non-null and non-empty: for each id, `orgConfig.FindById(id)`; on a miss, `context.Logger?.LogError(...)` **then `throw new InvalidOperationException($"Organization '{id}' not found in organizations config.")`** — fail fast per the spec. (The exception surfaces from the first `GameLogic.Update`; the headless runner catches it, prints to stderr, exits non-zero.)
- Else: the legacy path — `InitialOrganizationId` if non-empty, resolved leniently exactly as today (log-and-continue on a miss), yielding a 0- or 1-entry list. This keeps the current Unity flow's observable behaviour identical.

Refactor the org-scoped creation into per-org form, iterating `participating` **in list order** (pinning RNG consumption order):

- The single-org block in `Run` (org entity + gold `Resource` + base `ControlEffect`) becomes a `foreach (var orgEntry in participating)` loop — unchanged field values, `EffectId = $"base_{orgEntry.OrganizationId}"`.
- `CreateOrgCharacterEntities` → takes `IReadOnlyList<OrganizationEntry> participating`; loops orgs, calling the existing `CreateOrgSlots` per org with **`isPlayerOrg: true` for every org** (resolved decision: every participating org is initialized like the player org, including `IsAvailable` on unfilled slots — so future bots can fill agent slots identically to the player).
- `CreateActionEntities` → takes `participating`; per org creates the `ActionOwner`, org `CardDeck`/`CardHand`, `ActionCard`s from `actionConfig.GetOrgPool(orgId)`, and draws the initial hand via the existing shuffle. The stray `DiscoverInitialCountries` call currently nested at the end of this method moves out to `Run` as its own step (see §4).
- `CreateCountryActionEntities` → builds the `charsByCountryAndRole` lookup once, then wraps the per-country card/deck/hand creation in a per-org loop (cards get `ActionCard.OwnerId`/`OrgContext.OrgId`/`CardDeck.OrgId` = that org; initial-hand eligibility uses that org's control via the existing `GetOrgControlInCountry`).

No changes needed in `DrawCardSystem`, `ControlSystem`, `ResourceSystem`, `OpinionSystem`, `InitActionFromPlayCardSystem`, `CheckActionConditionSystem`, `DeductActionCostSystem`, `RemoveCardFromHandSystem`, `CheckHandSizeSystem` — all are already keyed by `OrgId`/`OwnerId` (verified by reading them); the multi-org gameplay criteria are covered by regression tests, not code changes.

### 4. Per-org discovery

**New component** `src/Game.Components/DiscoveredCountry.cs`:

```csharp
namespace GS.Game.Components {
	[Savable]
	public struct DiscoveredCountry {
		public string OrgId;
		public string CountryId;
	}
}
```

One entity per (org, country) pair. With save backward compatibility explicitly waived (resolved decision), the shape is chosen purely on design merit, and the per-pair entity wins over the alternative (extending `IsDiscovered` with an `OrgIds string[]` on the country entity) because: it matches the established org-keyed-entity pattern (`ControlEffect` is exactly `{OrgId, CountryId, ...}` entities queried by linear scan); "org X's discovered set" and "add a discovery" are a filter and an entity-create instead of in-place `string[]` reallocation inside a struct component; it avoids array-mutation-in-`[Savable]`-struct churn; and it round-trips through the reflection-based `SaveSystem`/`LoadSystem` as two plain string fields with no special casing.

**`IsDiscovered` is deleted** (`src/Game.Components/IsDiscovered.cs` removed; all references replaced). Consequence for pre-change saves: `LoadSystem.Apply` silently skips component type names not in its `[Savable]` type map, so an old save containing `GS.Game.Components.IsDiscovered` entries loads without error but drops those entries — discovery progress is lost, everything else is preserved. That is exactly the behaviour the spec permits ("old saves may lose discovery progress or fail to load"); no migration or attribution logic is written, and no legacy type is kept.

**Effect attribution**: `DiscoverCountryEffect` (in `src/Game.Components/ResourceChangeEffect.cs`) gains `public string OrgId;`. `CreateActionEffectSystem` (line ~41) stamps it: `world.Add(e, new DiscoverCountryEffect { EffectId = effectId, OrgId = orgId });` — the `orgId` is already in scope in that loop today and simply discarded.

**`src/Game.Systems/DiscoverCountrySystem.cs`** — rewritten per-org:

```csharp
public static void Update(World world, int proximityEntity, Random rng,
	string viewOrgId, IReadOnlyDictionary<string, string> hqCountryByOrgId)
```

- Collect pending `DiscoverCountryEffect`s and group by `OrgId`, preserving first-encounter (archetype-iteration) order — deterministic. At most one discovery roll per org per tick (parity with today's one-roll-per-tick for the single org; effects are still swept by `CleanupActionEffectsSystem` next tick, unchanged).
- Per org: discovered set = `DiscoveredCountry` entities with that `OrgId`; candidates = countries not in that set; anchor country = `FindPlayerCountryId(world)` **if** `orgId == viewOrgId` and a `Player` country exists (today's behaviour for the view org), else `hqCountryByOrgId[orgId]` if present and non-empty, else `""` → the existing uniform-weight fallback. Weighting/floor/roll logic is unchanged.
- On success, create a new entity with `DiscoveredCountry { OrgId = orgId, CountryId = chosen }` (guard against duplicates by construction — candidates exclude the org's set). Note: `IsDiscovered` is no longer added to the country entity.

`GameLogic.Update` call site passes `viewOrgId` resolved from the current `_orgEntity` (`_orgEntity >= 0 ? _world.Get<Organization>(_orgEntity).OrganizationId : ""` — world truth, correct after load too) and `_hqCountryByOrgId`.

**Initial discovery** — `InitSystem.DiscoverInitialCountries` becomes per-org and writes `DiscoveredCountry` entities: each participating org discovers its own `HqCountryId`; the view org (`context.InitialOrganizationId`, when it is among the participants) additionally discovers `context.InitialPlayerCountryId`. For the real Unity configs (org has an action pool and hand size > 0) the single-org result is exactly today's set {player country, HQ} — just stored per-org. Note one deliberate delta: today the call sits behind `CreateActionEntities`' early returns, so a configured org with no cards gets *no* initial discovery; after the hoist it gets {player country, HQ}. This only affects card-less configs (e.g. the `GameLogicOrgTests` harness), not Unity. (The unsupported "no org at all" flow simply creates no discovery entities; it also has no gold/cards today.)

**Debug cheat** — `GameLogic.ApplyDebugDiscoverAllCountries` creates `DiscoveredCountry` entities for the **view org** for every country not already in its set (behaviour-equivalent for Unity's single-org use).

**VisualState sourcing** — `VisualStateConverter.UpdateDiscoveredCountries` (line ~432) changes its query from `Country + IsDiscovered` to: resolve the view org id from the `orgEntity` parameter already passed into `Update(...)`, then collect `CountryId`s of `DiscoveredCountry` entities with that `OrgId`. The `VisualState.DiscoveredCountries` shape (`Set(ids, recently)`) and the recently-discovered diff logic are untouched — same shape, view-org-sourced, per the spec.

**Save round-trip** — new-format saves need no code: `DiscoveredCountry` flows through the generic reflection-based `SaveSystem.BuildSnapshot`/`LoadSystem.Apply` pipeline automatically (it is `[Savable]` with two string fields). `GameLogic.LoadState` is not modified. Header, storage, serializer, autosave — all untouched, per the spec.

### 5. Raw metrics helper (`src/Game.Systems/OrgMetrics.cs`)

New static query class (netstandard2.1, ships in the DLL, reused by the runner, the tests, and part 3):

```csharp
public static class OrgMetrics {
	public static int GetTotalControl(IReadOnlyWorld world, string orgId);       // sum of ControlEffect.Value where OrgId matches
	public static double GetGold(IReadOnlyWorld world, string orgId);            // Resource "gold" where ResourceOwner.OwnerId == orgId
	public static Dictionary<string, int> GetControlByCountry(IReadOnlyWorld world, string orgId); // for the determinism test's per-country breakdown
}
```

Same archetype-iteration style as `ControlSystem`. Deliberately raw sums — no coefficient, no derived score (part 3 owns that, building on the country-scoring plan's patterns).

### 6. Headless console runner (`src/Game.ConsoleRunner/`)

**CLI syntax (concrete decision).** Invocation from the repo root:

```
dotnet run --project src/Game.ConsoleRunner -- [--headless] [options]
```

| Flag | Meaning | Default |
|---|---|---|
| `--headless` | run the non-interactive simulation | off → interactive mode |
| `--seed <int>` | RNG seed | **required** in headless |
| `--output <path>` | results JSON path | **required** in headless |
| `--orgs <id[,id...]>` | participating org ids (comma-separated) | all orgs in `organizations.json`, config order |
| `--config-dir <path>` | config directory | `Assets/Configs` (relative to cwd; run from repo root) |
| `--end-date <yyyy-MM-dd>` | stop when game date ≥ this | — |
| `--max-ticks <int>` | stop after N `Update` calls | — |
| `--timeout-seconds <int>` | wall-clock safety timeout, always active | `300` |
| `--hours-per-tick <int>` | game hours advanced per tick | `24` (one game day) |

Validation (all failures → usage message on stderr, exit code 1, before any simulation): headless requires `--seed`, `--output`, and **at least one of `--end-date` / `--max-ticks`** (the timeout is a safety net, not a primary stop condition); `--hours-per-tick` must be in `[1, 672]` — 672 h = 28 days, the shortest month, which is the **month-skip guard**: `ResourceSystem`/`ControlSystem`/`OpinionSystem` detect only a single month-boundary crossing per update, so any coarser tick silently skips monthly income. The runner never pushes `ChangeTimeMultiplierCommand`; it leaves `MultiplierIndex` at 0 and computes `deltaTime = hoursPerTick / (float)settings.SpeedMultipliers[0]` (with the shipped `[1, 24, 720]` config: `24 / 1 = 24.0` → exactly one game day per tick), making the tick contract independent of the config's multiplier table.

**New files** in `src/Game.ConsoleRunner/`:
- `HeadlessOptions.cs` — argument parsing + validation as a pure testable class (`static HeadlessOptions Parse(string[] args)` throwing `ArgumentException` with a descriptive message on invalid input; carries all fields above plus `IsHeadless`). `Game.Tests` gains a `ProjectReference` to `Game.ConsoleRunner.csproj` so this is unit-testable (net8.0 exe referenced from net8.0 test project — supported).
- `MapGeometryFileConfig.cs` — `IConfigSource<List<GS.Core.Map.MapFeature>>` reading `{configDir}/geojson_world.json` and calling `GS.Core.Map.GeoJsonParser.Parse(text)`, mirroring Unity's `Assets/Scripts/Unity/Common/MapGeometryConfig.cs`. This gives headless runs the same proximity-weighted discovery as Unity instead of silently falling back to uniform. (Add a `Core.Map` `ProjectReference` to the csproj if it isn't already transitive through `Game.Main`.)
- `HeadlessRunner.cs` — builds the `GameLogicContext` (all ten config sources via `FileConfig<T>` from `--config-dir`: `geojson_world`, `map_entry_config`, `country_config`, `game_settings`, `resource_config`, `organizations`, `character_config`, `action_config`, `effect_config`, `province_config`, plus `mapGeometry` — fixing today's silent empty-config fallbacks that would leave a run with no cards; `storage`/`serializer` stay null so autosave is inert and no `SaveGameCommand` source exists), `rngSeed` from `--seed`, `participatingOrganizationIds` from `--orgs` (or all `organizations.json` ids in config order when omitted), `initialOrganizationId` = first participating org, then runs the loop:
  - `logic.Update(deltaTime)` in a tight loop — no `Console.ReadLine`, no sleeps, no `ViewerServer`, no commands pushed (part 1: the runner is the only command source and pushes none).
  - After each tick: read the game date from `logic.VisualState.Time.CurrentTime`; when the (month, year) differs from the last sample's, append a timeline sample (game date + per-org `OrgMetrics.GetTotalControl`/`GetGold`); one sample is also taken at t0 before the loop (start date, initial gold/control) so the timeline always has a baseline.
  - Stop checks in priority order: date ≥ `--end-date` → `dateReached`; ticks ≥ `--max-ticks` → `maxTicks`; `Stopwatch` elapsed ≥ timeout → `timeout` (results still written with whatever was sampled). Checked every tick; the timeout check may be amortized (e.g. every 256 ticks) for speed — a wall-clock safety net needs no per-tick precision.
  - Writes the results JSON, prints a one-line summary to stdout, returns exit code 0. First-`Update` init failures (e.g. unknown org id thrown by `InitSystem`) and config-load failures are caught in `Program.Main`, printed to stderr, exit code 1.
- `SimulationResult.cs` — result DTOs serialized with `System.Text.Json` (fine here: ConsoleRunner is net8.0 and never ships to `Assets/Plugins/`; `FileConfig` in `Core.Configs.IO` already uses it) using `JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }`.

**Results JSON schema (concrete):**

```json
{
	"seed": 42,
	"parameters": {
		"orgIds": ["Illuminati", "Masons"],
		"configDir": "Assets/Configs",
		"hoursPerTick": 24,
		"deltaTime": 24.0,
		"endDate": "1890-01-01",
		"maxTicks": null,
		"timeoutSeconds": 300
	},
	"tickCount": 3653,
	"endReason": "dateReached",
	"finalDate": "1890-01-01",
	"orgs": [
		{ "orgId": "Illuminati", "totalControl": 42, "gold": 1234.56 },
		{ "orgId": "Masons", "totalControl": 17, "gold": 987.5 }
	],
	"timeline": [
		{ "date": "1880-01-01", "orgs": [ { "orgId": "Illuminati", "totalControl": 10, "gold": 1000.0 }, { "orgId": "Masons", "totalControl": 10, "gold": 1000.0 } ] },
		{ "date": "1880-02-01", "orgs": [ "..." ] }
	]
}
```

Dates are `yyyy-MM-dd` strings; `orgs` arrays are always in participating-list order; `endReason` ∈ `dateReached | maxTicks | timeout`. **No wall-clock timestamps anywhere in the file** — same seed + params ⇒ byte-identical metric values (the JSON-equality criterion).

**`Program.cs`** — parses args; `--headless` → `HeadlessRunner`; otherwise the preserved interactive mode (ViewerServer + `Console.ReadLine` step loop, exactly as today) except its config sources now come from the same `--config-dir` default `Assets/Configs` — which also un-breaks it, since `data/` never contained the files it referenced.

**`data/` removal** — all three files in `src/Game.ConsoleRunner/data/` (`geojson_world.json`, `map_entry_config.json`, `country_config.json`) have their canonical versions in `Assets/Configs/` (verified — including `geojson_world.json`, which lives in `Assets/Configs/`, not `Assets/Map/`; `Assets/Map/` holds only `world_1880.json` + tiles, which the runner does not consume). Delete the `data/` directory entirely and remove the `<Content Include="data/**" ...>` item group from `Game.ConsoleRunner.csproj`. No config file content changes are needed in `Assets/Configs/`.

### 7. What deliberately does NOT change

- `SaveSystem`/`LoadSystem`/`WorldSnapshot`/`SaveFileManager`/`AutoSaveSystem` — untouched. `SaveSystem.BuildSnapshot`'s header org remains "first `Organization` found"; Unity worlds stay single-org and headless never saves, so this is safe (noted, not fixed — save-system changes are out of scope).
- `VisualState` — no new fields; `VisualStateConverter` still runs every tick (including headless; its per-tick cost is accepted for part 1 — bypassing it is a part-2/3 optimization if ever needed).
- `SelectOrgLogic`, `StaticGameLogic`, all Unity-side scripts, scenes, prefabs, configs under `Assets/` — untouched.
- `game_settings.json` / `organizations.json` — no shape or content changes required by this plan.

## Steps

### Agent Steps

- [ ] **Add context inputs** — `src/Game.Main/GameLogicContext.cs`: append optional `int? rngSeed = null` and `IReadOnlyList<string>? participatingOrganizationIds = null` ctor params, exposed as get-only properties. No existing call site changes.

- [ ] **Seed the RNG and add view-org/HQ plumbing in `GameLogic`** — `src/Game.Main/GameLogic.cs`: seeded `_rng` from `context.RngSeed`; build `_hqCountryByOrgId` from `context.Organization.Load()` in the ctor; make `RefreshSingletonEntities` prefer the `Organization` entity matching `_context.InitialOrganizationId` (fallback: first found); update the `DiscoverCountrySystem.Update` call to pass the resolved view org id and `_hqCountryByOrgId`.

- [ ] **Multi-org `InitSystem`** — `src/Game.Main/InitSystem.cs`: add `ResolveParticipatingOrgs` (fail-fast throw + `Logger.LogError` for unknown ids in an explicit list; legacy lenient single-org fallback otherwise); loop org entity/gold/base-`ControlEffect` creation over participants; convert `CreateOrgCharacterEntities` (`isPlayerOrg: true` for all orgs), `CreateActionEntities` (org deck/hand per org; hoist the `DiscoverInitialCountries` call out into `Run`), and `CreateCountryActionEntities` (per-org card/deck/hand inside the shared country loop) to per-org form iterating participants in list order.

- [ ] **Add `DiscoveredCountry`, delete `IsDiscovered`, attribute `DiscoverCountryEffect`** — new `src/Game.Components/DiscoveredCountry.cs` (`[Savable]`, `OrgId` + `CountryId`); delete `src/Game.Components/IsDiscovered.cs` (its reference sites — `DiscoverCountrySystem`, `VisualStateConverter.UpdateDiscoveredCountries`, `InitSystem.DiscoverInitialCountries`, `GameLogic.ApplyDebugDiscoverAllCountries` — are all rewritten by the adjacent steps; no migration logic, per the resolved decision); add `public string OrgId;` to `DiscoverCountryEffect` in `src/Game.Components/ResourceChangeEffect.cs`; stamp `OrgId = orgId` at the creation site in `src/Game.Systems/CreateActionEffectSystem.cs`.

- [ ] **Per-org `DiscoverCountrySystem`** — rewrite `src/Game.Systems/DiscoverCountrySystem.cs` per the Approach: group effects by org (first-encounter order), per-org discovered-set/candidates, per-org anchor (view org → `Player` country; others → HQ from the passed dictionary; else uniform), unchanged weighting/roll, result written as a `DiscoveredCountry` entity.

- [ ] **Per-org initial discovery and debug cheat** — `InitSystem.DiscoverInitialCountries` creates `DiscoveredCountry` entities per participant (own HQ; view org additionally the initial player country); `GameLogic.ApplyDebugDiscoverAllCountries` fills the view org's set with all countries.

- [ ] **View-org-sourced `VisualState` discovery** — `src/Game.Main/VisualStateConverter.cs` `UpdateDiscoveredCountries`: resolve view org id from the `orgEntity` parameter, collect that org's `DiscoveredCountry` country ids; `Set(ids, recently)` shape and diff logic unchanged. While in the file, also fix `UpdateCharacters`' org resolution: it currently derives `playerOrgId` from the first `Organization` archetype found (lines ~77–85) instead of `orgEntity`, so in a multi-org world selected-country character opinions could read the wrong org's `opinion_{orgId}` resource. Pass the same `orgEntity`-resolved view org id in; single-org behaviour is identical and no `VisualState` shape changes.

- [ ] **Add `OrgMetrics`** — new `src/Game.Systems/OrgMetrics.cs` with `GetTotalControl`, `GetGold`, `GetControlByCountry` (raw sums, no score).

- [ ] **Runner: options and validation** — new `src/Game.ConsoleRunner/HeadlessOptions.cs` implementing the CLI table above as a pure `Parse(string[])`, including the required-args rule (seed + output + one of end-date/max-ticks) and the `--hours-per-tick` ∈ [1, 672] month-skip guard.

- [ ] **Runner: config loading and geometry** — new `src/Game.ConsoleRunner/MapGeometryFileConfig.cs` (GeoJSON → `List<MapFeature>` via `GeoJsonParser`); rewrite `Program.cs` to build the full ten-source `GameLogicContext` from `--config-dir` (default `Assets/Configs`) for **both** modes, keeping the interactive ViewerServer/`ReadLine` loop as the no-`--headless` default; add a `Core.Map` project reference if not transitive.

- [ ] **Runner: headless loop and results** — new `src/Game.ConsoleRunner/HeadlessRunner.cs` + `SimulationResult.cs` DTOs: seed/orgs/`initialOrganizationId`=first-participant context, `deltaTime = hoursPerTick / multipliers[0]`, tight `Update` loop, t0 + per-month-boundary timeline sampling via `OrgMetrics`, stop conditions (`dateReached`/`maxTicks`/`timeout`) with results always written, camelCase `System.Text.Json` output with no wall-clock fields, exit 0 on completion; `Program.Main` catches startup/init exceptions → stderr + exit 1.

- [ ] **Remove the drifted `data/` snapshot** — delete `src/Game.ConsoleRunner/data/` (all three files are canonical in `Assets/Configs/`) and drop the `<Content Include="data/**">` group from `Game.ConsoleRunner.csproj`.

- [ ] **Tests** — implement the Tests section below, including adding the `Game.ConsoleRunner` project reference to `src/Game.Tests/Game.Tests.csproj` and adding `typeof(DiscoveredCountry)` to `SavableDiscoveryTests.ExpectedSavable` (note: `IsDiscovered` was never listed there, so nothing is removed).

- [ ] **Run the test suite** — `dotnet test src/GlobalStrategy.Core.sln`; the paired-run determinism tests are the acceptance gate — if they fail, fix the specific nondeterminism source they expose (ordinal key sort in the offending system) and re-run.

- [ ] **Rebuild the Core DLLs** — `dotnet build src/GlobalStrategy.Core.sln -c Release` so `Assets/Plugins/Core/` picks up the changed `Game.Components`/`Game.Systems`/`Game.Main` assemblies (ConsoleRunner is net8.0 and stays out of Plugins).

- [ ] **Headless smoke run** — from the repo root: `dotnet run --project src/Game.ConsoleRunner -c Release -- --headless --seed 42 --output .tmp/sim_result.json --end-date 1881-01-01`; verify exit code 0, both orgs in the results with 12 monthly samples plus the t0 baseline; run twice with the same seed and diff the two files for byte-identical metrics; run once with `--orgs Illuminati,DoesNotExist` and verify non-zero exit + stderr error.

### User Steps

### 1. Confirm a clean Unity import

Let Unity reload the rebuilt `Assets/Plugins/Core/*.dll` and check `read_console(types=["error"])` — no `Assets/Scripts` source changed, so only the DLL swap should be visible.

### 2. Verify single-org play is unchanged

Enter Play mode through the normal flow (select country/org). Confirm: initial discovered countries are the player country + org HQ as before; playing a discover-type card still reveals a proximity-weighted country; gold/control/cards behave as before.

### 3. Verify new-format save round-trip

Start a new game, discover at least one extra country (play a discover card or use the debug cheat), save, and reload the save. Confirm the discovered set is intact after load. (Pre-change saves are expected to lose discovery progress — that is by design per the resolved decision; if you keep any around, everything except discovery should still load.)

### 4. Eyeball a headless run

Optionally re-run the smoke command from the last agent step and skim `.tmp/sim_result.json` — sanity-check that per-org gold/control trajectories look plausible (both orgs earn HQ income monthly).

## Tests

Test project: `src/Game.Tests/` (xunit, snake_case `[Fact]` names, `StaticConfig<T>`/`BuildLogic` harness per `GameLogicOrgTests.cs`, `MemoryStorage`/`CapturingSerializer` per `InitSystemTests.cs`). New multi-org tests need a richer shared builder (two orgs, two+ countries, a minimal `ActionConfig`/`EffectConfig` with an org pool, a discover effect, and a control effect, and a `ResourceConfig` with a monthly `gold` income) — model it on `GameLogicOrgTests.BuildLogic` with extra optional params.

- **New `src/Game.Tests/MultiOrgInitTests.cs`:**
  - `each_participating_org_gets_org_entity_gold_base_control_slots_deck_and_hand` — two participating orgs; assert per org: `Organization` entity, gold `Resource` = `InitialGold`, `base_{orgId}` `ControlEffect` in its HQ, `master` + `InitialAgentSlots` `CharacterSlot`s, org `CardDeck`/`CardHand` with a drawn hand, per-country `CardDeck`s keyed to it.
  - `unknown_participating_org_id_fails_fast` — explicit list containing a bogus id → first `Update` throws `InvalidOperationException` and the logger captured an error (spec's fail-fast criterion).
  - `default_context_initializes_single_org_exactly_as_today` — no participating list, `initialOrganizationId` set → entity set matches current behaviour (regression for the Unity path; reuse/extend existing `GameLogicOrgTests` asserts). Use a config with an org action pool, or assert discovery via the new hoisted rule — the card-less harness intentionally gains initial `DiscoveredCountry` entities relative to today (see Initial discovery note in §4).
  - `non_view_org_slots_are_initialized_with_is_available` — second org's unfilled agent slots have `IsAvailable == true` (resolved decision).

- **New `src/Game.Tests/MultiOrgGameplayTests.cs`:**
  - `two_orgs_play_cards_independently_without_cross_interference` — both orgs push `PlayCardActionCommand` (same tick); assert each org's gold was deducted its own cost, each org's hand lost/redrew its own card, created effects carry the right `OrgId`, and the other org's deck/hand/gold are untouched.
  - `change_control_and_debug_change_gold_affect_only_named_org` — push both commands for org A; org B's control/gold unchanged.

- **Extend `src/Game.Tests/ControlSystemTests.cs`:**
  - `two_orgs_in_same_country_split_monthly_income_proportionally` — org A 30 / org B 20 control in one country with base monthly gold; cross a month boundary; A gains 0.30×, B gains 0.20×, country loses the sum (the spec's ≥2-org regression criterion).

- **New `src/Game.Tests/DiscoveryPerOrgTests.cs`:**
  - `discover_effect_carries_acting_org_id` — after `CreateActionEffectSystem` processes org A's succeeded discover action, the `DiscoverCountryEffect.OrgId == "A"`.
  - `discovery_adds_country_to_acting_org_only` — resolve a discovery for org A; a `DiscoveredCountry` exists for A and none for B.
  - `view_org_anchors_to_player_country_and_other_orgs_anchor_to_hq` — with a proximity map where anchors produce disjoint near-certain picks, run rolls for the view org and a second org; each discovers a country near its own anchor.
  - `initial_discovery_is_per_org_hq_plus_player_country_for_view_org` — after init: view org's set = {player country, its HQ}; other org's set = {its HQ}.
  - `visual_state_discovered_countries_sourced_from_view_org_set` — org B discovers a country; `VisualState.DiscoveredCountries` (view org A) does not include it; A's discoveries do appear (single-org observable-behaviour + sourcing criteria).

- **Extend `src/Game.Tests/SaveLoadRoundTripTests.cs`:**
  - `round_trip_preserves_per_org_discovery` — build a world with `DiscoveredCountry` entities for two orgs → `SaveSystem.BuildSnapshot` → `LoadSystem.Apply` into a fresh world → both org sets intact and separate (the new-shape round-trip that stays in scope; no pre-change-save tests exist, per the resolved decision).

- **Extend `src/Game.Tests/SavableDiscoveryTests.cs`:** add `typeof(DiscoveredCountry)` to `ExpectedSavable`. (`IsDiscovered` is not currently in either list, so its deletion requires no test-list removal.)

- **New `src/Game.Tests/DeterminismTests.cs`** (the acceptance gate):
  - `same_seed_and_commands_produce_identical_end_state` — two `GameLogic` instances, same seed/configs (2 orgs, cards, monthly income, proximity map), identical `Update(deltaTime)` sequence (~13+ game months at one day per call) with an identical scripted command sequence (each org plays a card on fixed ticks); assert equality of: per-org `OrgMetrics.GetTotalControl`, per-org `GetGold`, per-org `GetControlByCountry` (per-country breakdown), game date, and both org hand contents (sorted `(ActionId, SlotIndex)` lists).
  - `same_seed_and_commands_produce_identical_monthly_timeline` — same paired setup; snapshot per-org control/gold at every month boundary during the run; assert the full sample sequences are element-wise identical (not just end states).

- **New `src/Game.Tests/HeadlessOptionsTests.cs`** (requires the new `Game.ConsoleRunner` project reference):
  - `hours_per_tick_above_672_is_rejected` and `hours_per_tick_zero_is_rejected` — the month-skip guard.
  - `headless_requires_seed_output_and_a_stop_condition` — missing `--seed`, `--output`, or both of `--end-date`/`--max-ticks` → `ArgumentException`.
  - `orgs_flag_parses_comma_separated_list` and `defaults_are_applied` (`hoursPerTick == 24`, `timeoutSeconds == 300`, `configDir == "Assets/Configs"`, headless off without the flag).

Run: `dotnet test src/GlobalStrategy.Core.sln`.

## Constitution Check

Checked against `Docs/Constitution.md`. **No conflicts found — plan aligns with all principles.**

- *ECS for all game logic, living in `src/`.* Every gameplay change (multi-org init, per-org discovery components/systems, seeded RNG, metrics) lives in `src/Game.Components`, `src/Game.Systems`, `src/Game.Main`, `src/Game.ConsoleRunner`. No MonoBehaviour or `Assets/Scripts` change; Unity consumes the rebuilt DLLs only.
- *VContainer is the sole DI mechanism.* No Unity-side registration changes; `GameLifetimeScope` is untouched (the new context params are optional). The ConsoleRunner composes plain objects in `Main` as it already does today — it is outside the Unity composition root by design and predates this plan.
- *UI Toolkit only.* No UI is added or modified; `VisualState` shape is unchanged.
- *Unity 6 + URP only.* No rendering, shader, or camera change.
- *Plan before implement / Spec before plan.* This plan implements the approved `Docs/Specs/50_multi-org-headless-simulation/spec.md`, including its user-resolved decisions; no code changes precede it.
- *File organisation.* Plan lives at `Docs/Specs/50_multi-org-headless-simulation/plan.md`, paired with its spec under the shared incrementing index. Originally indexed 48 (following 47); reindexed to 50 to make room for `Docs/Specs/48_score-component-composition.md`/`Docs/Specs/49_org-scoring/` on `main` — an org-scoring prerequisite this initiative's later parts consume, extracted and merged independently of multi-org.
- *One `.asmdef` per feature folder under `Assets/Scripts/`.* No `Assets/Scripts` folders or asmdefs are created or modified; new `src/` files land in existing csproj projects (`Game.Components`, `Game.Systems`, `Game.Main`, `Game.ConsoleRunner`), and the only csproj edits are the ConsoleRunner `data/` content removal, an optional `Core.Map` reference, and the test project's ConsoleRunner reference.
- *C# code style.* All new/edited code uses tabs, braces always, `_`-prefixed privates, no redundant access modifiers — matching the surrounding files quoted throughout this plan.

Use /implement to start working on the plan or request changes.
