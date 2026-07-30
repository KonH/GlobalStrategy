# Plan: Damage and Durability at War

## Owner amendments (2026-07-30) — applied

1. Bundle version: stay on milestone major — `1.99` → `1.100` (not `2.00`).
2. `ResourceSeedTarget.None` removed: it was only an escape hatch for wartime-only InitSystem exclusion. Always-on resources use `seedTarget: "Country"` + InitSystem Instant+Daily attachment.
3. Config bases cached as one `CountryCombatBases` value per country id (`IReadOnlyDictionary<string, CountryCombatBases>`), shared by `DamageCollector` and `DurabilityCollector`.
4. Lifecycle is always-on for available countries (not create/destroy on war). `Wars` no longer touches these resources. Settle on war-relevant character cycle/drop and on every `LoadState` (before VisualState).

Historical sections below retain the original wartime-only plan for audit; treat the amendments above + updated `spec.md` as authoritative.

## Spec

Source: `Docs/Specs/26_07_29_16_damage-durability-at-war/spec.md`.

**Intent.** Every country that is currently a war participant exposes two country-scoped wartime resources — `damage` and `durability` — derived from a per-country config baseline (1–100, reflecting 1880-era war technology) plus the relevant ruler and advisor character skills, kept correct whenever those characters or skills change, so a later battle/combat slice can read stable offensive and defensive war strength without re-deriving skill math.

**Dependency.** `Docs/Specs/26_07_25_06_war-mechanics-core` (GitHub #69 — **merged** via PR #79; `Wars.cs` / `WarParticipant` already on tree) and the resource-collector pipeline (`Docs/Specs/26_07_18_17_resource-collector-pipeline`). Branch `feature/damage-durability-at-war` can implement against current main; no further war-mechanics merge gate.

**Key acceptance criteria (design targets):**
- Peacetime countries have **no** live country `Resource` entities with ids `damage` / `durability`.
- On `WarParticipant` create (any declare-war path): each participant immediately gets `[Savable]` country `Resource` entities for `damage` and `durability`, plus Instant + recurring collector-driven `ResourceEffect`/`ResourceCollector` pairs that absolute-set `target = base + skillA + skillB` via `delta = target - currentValue`.
- `damage = baseDamage + ruler.power + military_advisor.power` (missing character/skill → `0`). Theoretical max `300`.
- `durability = baseDurability + ruler.stinginess + economic_advisor.stinginess` (same missing→0 / max-300 shape).
- Bases live on `CountryEntry` (`baseDamage` / `baseDurability`, ints `[1, 100]`) in `country_config.json`; proposed table for every `isAvailable: true` country; all others default **40/40**; preserved across GeoJSON regen like `historicalFriends`.
- Character cycle (and in-place skill mutation) while at war re-derives affected war resource(s) **without waiting for a month boundary**.
- On war end: destroy both countries' `damage`/`durability` resources and their linked effect/collector entities.
- Each side computes independently from *that* country's bases and characters.
- Queryable as ordinary country `Resource` values; no battle consumer in this feature.
- Save/load while at war: resources persist; collectors re-sync from live config + skills.
- `VisualState` / `BuildResources`: no whitelist work; dedicated War UI out of scope.

**Out of scope:** battle usage, recruits interaction, war model changes beyond lifecycle hooks, UI, new roles/skills, bot strategy.

## Goal

Add wartime-only country `damage` and `durability` resources driven by Instant+Daily absolute collectors (org_score dual-effect pattern), created/destroyed with war participation, with per-country config bases and immediate mid-war recalculation via the existing `ForceResourceRecompute` / `ResourceSystem.Update` settle path — no InitSystem Country seed, no second pipeline.

## Approach

### 1. Config — `CountryEntry` bases + GeoJSON preservation

- **`src/Game.Configs/CountryConfig.cs`** — on `CountryEntry` add:
  ```csharp
  public int BaseDamage { get; set; } = 40;
  public int BaseDurability { get; set; } = 40;
  ```
  Defaults `40` cover every `isAvailable: false` country and any newly-regenerated id until authored.

- **`Assets/Configs/country_config.json`** — for every country entry set `baseDamage` / `baseDurability`:
  - Each of the 20 `isAvailable: true` countries: values from the spec Tech Notes table (exact `CountryId`s: `United_Kingdom_of_Great_Britain_and_Ireland`, `Germany`, `France`, `United_States_of_America`, `Austria_Hungary`, `Russian_Empire`, `Italy`, `Imperial_Japan`, `Netherlands`, `Belgium`, `Ottoman_Empire`, `SwedenNorway`, `Spain`, `Argentina`, `Kingdom_of_Brazil`, `Egypt`, `Portugal`, `Ethiopia`, `Manchu_Empire`, `Persia`).
  - All other countries: `40` / `40`.

- **`src/Game.Configs.Loader/Program.cs`** — extend `ApplyPreservedFields` to copy `BaseDamage` / `BaseDurability` from the existing entry onto the rebuilt entry (sibling to `HistoricalFriends` / `HistoricalRivals`).

- **`src/Game.Tests/LoaderCountryPreservationTests.cs`** — add a fact that bases survive `ApplyPreservedFields` (and that unmatched / null-existing leave the C# defaults).

### 2. Resource catalog — definitions **without** Country seeding

**Exact InitSystem finding:** `CreateCountryResourceEntities` iterates `resourceConfig.FindResources(ResourceSeedTarget.Country)` and for any Country-seeded id other than gold / `country_population` / `country_score` / `recruits` calls `ThrowUnsupportedResource`. Putting `damage`/`durability` under `seedTarget: "Country"` would either crash init or require teaching InitSystem to create them for **every** country — both forbidden by the wartime-only lifecycle.

**Choice:** add `ResourceSeedTarget.None` to `src/Game.Configs/ResourceConfig.cs`:

```csharp
public enum ResourceSeedTarget {
	Character,
	Province,
	Country,
	Org,
	None,
}
```

No `Create*ResourceEntities` path calls `FindResources(None)`, so catalog entries with `seedTarget: "None"` are naming/icon-only and never auto-seeded.

- **`src/Game.Configs/ResourceDefinitions.cs`** — add `Damage = "damage"`, `Durability = "durability"`.

- **`Assets/Configs/resource_config.json`** — append two entries (do **not** add to `displayWhitelist` — spec: no whitelist work; `BuildResources` already returns every country-owned `Resource`):
  ```json
  {
    "resourceId": "damage",
    "nameKey": "resource.damage.name",
    "descriptionKey": "resource.damage.description",
    "icon": "damage",
    "seedTarget": "None",
    "defaultInitialValue": 0.0,
    "defaultEffects": []
  },
  {
    "resourceId": "durability",
    "nameKey": "resource.durability.name",
    "descriptionKey": "resource.durability.description",
    "icon": "durability",
    "seedTarget": "None",
    "defaultInitialValue": 0.0,
    "defaultEffects": []
  }
  ```

- **Do not** touch `InitSystem.CreateCountryResourceEntities` / `AttachCollectorDrivenCountryEffects` for these ids — wartime create/destroy owns that lifecycle.

- Update any `ResourceSeedTarget` exhaustive tests / `ResourceConfigTests` that assume the prior four enum members.

### 3. Collectors — absolute formula + per-country bases

- **`src/Game.Systems/DamageCollector.cs`**:
  ```csharp
  public sealed class DamageCollector : IResourceCollector {
  	public const string Id = "damage_formula";
  	readonly IReadOnlyDictionary<string, int> _baseDamageByCountryId;

  	public DamageCollector(IReadOnlyDictionary<string, int> baseDamageByCountryId) {
  		_baseDamageByCountryId = baseDamageByCountryId;
  	}

  	public double Compute(string ownerId, double currentValue, IReadOnlyWorld world) {
  		int baseDamage = _baseDamageByCountryId.TryGetValue(ownerId, out int b) ? b : 40;
  		double rulerPower = SkillOf(world, ownerId, "ruler", "power");
  		double militaryPower = SkillOf(world, ownerId, "military_advisor", "power");
  		double target = baseDamage + rulerPower + militaryPower;
  		return target - currentValue;
  	}
  	// SkillOf: CharacterQuery.GetTargetCharacterByCountryAndRole → empty ⇒ 0;
  	// else ResourceQuery.GetValue(world, characterId, skillId). Private static helper
  	// shared via a small WartimeSkillQuery helper or duplicated once in each collector.
  }
  ```

- **`src/Game.Systems/DurabilityCollector.cs`** — same shape, `Id = "durability_formula"`, bases dict for durability, skills `ruler`/`stinginess` + `economic_advisor`/`stinginess`.

- Prefer a tiny shared helper in the same files or `src/Game.Systems/WartimeSkillQuery.cs`:
  ```csharp
  public static class WartimeSkillQuery {
  	public static double GetSkill(IReadOnlyWorld world, string countryId, string roleId, string skillId) {
  		string characterId = CharacterQuery.GetTargetCharacterByCountryAndRole(world, countryId, roleId);
  		if (string.IsNullOrEmpty(characterId)) {
  			return 0;
  		}
  		return ResourceQuery.GetValue(world, characterId, skillId);
  	}
  }
  ```

- **No clamp** above 300 — arithmetic only (spec locked). Missing skills leave final at base or base+one skill.

### 4. `ResourceCollectorRegistry.CreateDefault` — single overload, new params

Extend the **one** existing `CreateDefault` (do not add a second overload); update the sole call site in `GameLogic`:

```csharp
public static ResourceCollectorRegistry CreateDefault(
	double populationGrowthPercentPerMonth, double countryScoreCoefficient,
	double recruitsInitialPercent, double recruitsCapPercent, double recruitsMonthlyIncreasePercent,
	IReadOnlyDictionary<string, int> baseDamageByCountryId,
	IReadOnlyDictionary<string, int> baseDurabilityByCountryId) {
	// ...existing Register calls...
	registry.Register(DamageCollector.Id, new DamageCollector(baseDamageByCountryId));
	registry.Register(DurabilityCollector.Id, new DurabilityCollector(baseDurabilityByCountryId));
	return registry;
}
```

`GameLogic` constructor builds the dictionaries from loaded `CountryConfig` (every entry's `BaseDamage` / `BaseDurability`) and passes them in. Still plain C# construction inside `GameLogic` — no new VContainer registration (matches recruits / org_score).

**Also update `Game.Benchmarks/GameWorldFixture.cs`** — it calls `CreateDefault` for benchmarks; pass empty or config-derived base dictionaries so the fixture still compiles/runs.

### 5. `resourceIdUpdateOrder`

- **`GameSettings.ResourceIdUpdateOrder` default** and **`Assets/Configs/game_settings.json`**: append `"damage"`, `"durability"` after `"org_score"`:
  `[..., "org_score", "damage", "durability"]`.

Collectors only resolve for resourceIds listed in the ordered pass (see `ResourceSystem` NOTE) — omitting them would leave Instant/Daily effects applying a static zero forever.

### 6. Wartime lifecycle helper — create/destroy (not InitSystem)

Keep `Wars` as a non-system helper. Entity create/destroy for wartime resources lives either **inside `Wars`** (preferred — declare/stop already own the participation lifecycle) or a sibling non-system helper (e.g. `WartimeCountryResources`) called **from `Wars.DeclareWar` / `Wars.StopWar`**, never from another `*System.Update`.

**Create** (called once per participant when `DeclareWar` succeeds, for attacker and defender):

For each of `ResourceDefinitions.Damage` / `Durability`:
1. `Resource` entity: `ResourceOwner(countryId, OwnerType.Country)`, `Resource { ResourceId, Value = 0 }` (`[Savable]` via existing `Resource` component).
2. Instant effect: `ResourceOwner(countryId, OwnerType.Country)` — **must** pass `OwnerType.Country` (do not copy org_score's single-arg Org default or destroy will miss orphans) — plus `ResourceLink(resourceId)` + `ResourceEffect { EffectId = $"{resourceId}_seed_{countryId}", PayType = Instant }` + `ResourceCollector { CollectorId = DamageCollector.Id | DurabilityCollector.Id }`.
3. Daily effect (org_score dual-effect pattern — **not** Monthly): same owner/link/collector with explicit `OwnerType.Country`, `EffectId = $"{resourceId}_daily_{countryId}", PayType = Daily`.

**Why Daily not Monthly:** skill / character changes must not wait a month. Instant self-destructs after first resolve; Daily + `ForceResourceRecompute` keep mid-war values correct (same contract as `AttachOrgScoreEffects`).

**Destroy** (on `StopWar`, for **both** former participants — capture both `CountryId`s from matching `WarParticipant` rows before destroying them):

Destroy every entity that is either:
- a country-owned `Resource` with `ResourceId` in `{damage, durability}` for that country, or
- a country-owned `ResourceEffect` (with or without `ResourceCollector` / `ForceResourceRecompute`) whose `ResourceLink.ResourceId` is `damage` or `durability` for that country.

Idempotent: if somehow absent, no-op. `DeclareWar` already refuses when either side is in a war, so double-create cannot happen through the public API.

**Do not** change `War` / `WarParticipant` / `WarProgress` shape or `WarSystem` decay.

### 7. `GameLogic` — tick order, declare settle, character-cycle settle, load settle

**Verified `GameLogic.Update` order today:**
1. `TimeSystem.Update`
2. **`ResourceSystem.Update`** (line ~112) — first systems pass
3. `ControlSystem` / `WarSystem`
4. … UI / save …
5. **`ApplyDebugCycleCharacter`** (debug cycle) — **after** ResourceSystem
6. … later …
7. **`Wars.DeclareWar` / `Wars.StopWar`** command loops — **also after** ResourceSystem

Therefore: marking effects for recompute *during* cycle/declare in the same tick does **not** reach this tick's already-finished `ResourceSystem.Update`. Absolute Instant+Daily collectors cannot share one settle `Update` while Instant still exists: `ResolveCollectors` reads the same pre-apply `currentValue` for both, then `GatherAndApply` sums both deltas (2× target). Do **not** copy `SettleOrgScores`' mark-then-single-Update shape for declare (there Instant is already consumed).

**Wiring:**

- After the declare-war command loop: if any `DeclareWar` returned `true` (track a local `bool`), call a private `SettleWartimeResources()` that:
  - `ResourceSystem.Update(_world, now, now, …)` — Instant seed applies and self-destroys
  - `MarkResourceEffectsForRecompute(Damage)` / `Durability` — Daily only (`MarkResourceEffectsForRecompute` skips Instant)
  - `ResourceSystem.Update(_world, now, now, …)` — forced Daily absolute-sets against the post-Instant value
  (Cycle/drop/load settles use the same helper; Instant is usually already gone, so pass 1 is a no-op for wartime and pass 2 still refreshes Daily.)

- After `CycleCountryCharacter` returns (inside `ApplyDebugCycleCharacter` country branch, or at end of cycle method): if `Wars.IsInWar(_world, countryId)` and `roleId` is one of `ruler` / `military_advisor` / `economic_advisor`, call the same `SettleWartimeResources()` (or mark only the affected resourceId: ruler→both; military_advisor→damage only; economic_advisor→durability only — optional optimization; marking both is fine and simpler).

- **Also after country `DebugDropCharacter`** while at war: if the dropped role is war-relevant (`ruler` / `military_advisor` / `economic_advisor`) and `Wars.IsInWar`, call `SettleWartimeResources()` so missing skill contributes `0` immediately.

- **In-place skill mutation:** no current gameplay path mutates character skill `Resource.Value` mid-war except character replace via cycle. Document that future mutations should call the same mark+settle (or wait for the next real Daily boundary). Prefer Instant+Daily over Instant+Monthly so a natural day tick also refreshes without a forced settle.

- **`LoadState`:** after `RefreshSingletonEntities` / completion reconcile (and `_previousTime` refresh), **before** `_visualStateConverter.Update`, if any `WarParticipant` exists, `SettleWartimeResources()` so the first post-load VisualState is not stale. Persisted Daily effects re-sync absolute values from current bases + live skills even when the Instant seed was destroyed before save. Resources themselves load via `[Savable]`.

- **Stop-war:** destroy path inside `Wars.StopWar` is enough; no ResourceSystem settle required (entities gone).

### 8. Localization

If `nameKey` / `descriptionKey` are added (Approach §2), add matching keys to `Assets/Localization/en.asset` and real Russian translations to `ru.asset` via the **localization** skill (not English placeholders in `ru.asset`):

- `resource.damage.name` / `resource.damage.description`
- `resource.durability.name` / `resource.durability.description`

### 9. VisualState

No changes. `BuildResources` already emits every `Resource` owned by the selected country id. Dedicated War UI is out of scope. Do not add `damage`/`durability` to `displayWhitelist` unless a separate UI plan requires it.

### 10. Tests / rebuild

See Tests section. After Core changes: `dotnet build src/GlobalStrategy.Core.sln -c Release` so `Assets/Plugins/Core/` picks up DLLs.

## Agent Steps

- [x] **Confirm war-mechanics-core on tree** — `Wars.IsInWar` / `DeclareWar` / `StopWar` and `WarParticipant` exist (PR #79 merged). Proceed; no extra merge step.
- [x] **Add `CountryEntry.BaseDamage` / `BaseDurability`** — defaults `40`; preserve in `ApplyPreservedFields`; author `country_config.json` per spec table + 40/40 for unavailable; extend `LoaderCountryPreservationTests`.
- [x] **Add `ResourceSeedTarget.None` + catalog entries** — `ResourceDefinitions.Damage`/`Durability`; `resource_config.json` with `seedTarget: "None"` and name/description keys; **do not** Country-seed or InitSystem-create them.
- [x] **Append `resourceIdUpdateOrder`** — `GameSettings` default + `game_settings.json`: `damage`, `durability`.
- [x] **Add `WartimeSkillQuery` + `DamageCollector` + `DurabilityCollector`** — absolute `target - currentValue`; missing skill/character → 0; bases from constructor dictionaries.
- [x] **Extend `ResourceCollectorRegistry.CreateDefault`** — two `IReadOnlyDictionary<string, int>` params; register both collectors; update `GameLogic` constructor call site to build dicts from `CountryConfig`.
- [x] **Extend `Wars.DeclareWar` / `Wars.StopWar`** — create Instant+Daily resource/effect/collector pairs for both participants on declare; destroy those entities for both countries on stop (sibling helper OK if kept non-system and called only from `Wars`).
- [x] **Wire `GameLogic` settle path** — private `SettleWartimeResources` using existing `MarkResourceEffectsForRecompute` + `ResourceSystem.Update(now, now, …)`; call after successful declare-war, after war-relevant `CycleCountryCharacter` / `DebugDropCharacter` while `IsInWar`, and after `LoadState` (before VisualState update) when any war participant exists. Document tick-order rationale in a short comment (cycle/declare run after the first ResourceSystem pass). Update `GameWorldFixture.CreateDefault` call site.
- [x] **Add localization keys** — en + real ru via localization skill for the four resource name/description keys.
- [x] **Add/extend tests** — per Tests section below (richer GameLogic fixture; OwnerType.Country on effects).
- [x] **Rebuild Core DLLs** — `dotnet build src/GlobalStrategy.Core.sln -c Release`.

## User Steps

### 1. Confirm a clean Unity import

After the DLL rebuild, let Unity finish domain reload and check the console for errors — expected: updated `Assets/Plugins/Core/*.dll`, new `country_config` / `resource_config` / `game_settings` keys, and new locale entries. No scene or prefab edits in this feature.

### 2. Play-mode wartime smoke (optional)

Enter Play mode, `DebugDeclareWar` between two available countries, select one, confirm `damage`/`durability` via VisualState / ResourceQuery (not ResourcesView HUD — `displayWhitelist` stays unchanged); cycle ruler/military/economic advisor and confirm immediate update; `DebugStopWar` and confirm both resources disappear.

## Tests

Test project: `src/Game.Tests/` (xUnit, snake_case names, matching existing files).

- **New `src/Game.Tests/DamageCollectorTests.cs`** (mirror `CountryScoreCollectorTests` / `RecruitsSeedCollectorTests`):
  - `compute_sums_base_plus_ruler_and_military_power` — known base + both `power` skills → delta lands resource at full sum.
  - `compute_treats_missing_character_or_skill_as_zero` — no military advisor / no skill resource → contributes `0`; final can equal base.
  - `compute_returns_delta_from_current_value` — nonzero `currentValue` → `target - currentValue`.

- **New `src/Game.Tests/DurabilityCollectorTests.cs`** — same shape for stinginess / economic advisor.

- **Extend `src/Game.Tests/WarsTests.cs`** (and/or new `WartimeResourcesTests.cs` if helpers need isolation):
  - `declare_war_creates_damage_and_durability_resources_and_effects_for_both_countries` — after `DeclareWar`, both countries have `Resource` + Instant + Daily collector effects for both ids (entity counts / collector ids).
  - `stop_war_destroys_damage_and_durability_for_both_former_participants` — after `StopWar`, zero matching resources/effects; peacetime query returns `0` via `ResourceQuery` and no entities.
  - `declare_war_no_op_does_not_duplicate_wartime_resources` — second declare while at war leaves entity counts unchanged.

- **GameLogic integration** (prefer a dedicated richer fixture than bare `WarsTests.BuildLogic` — needs characters with skills + authored bases):
  - Push `DebugDeclareWarCommand`, `Update`, assert both sides' `ResourceQuery.GetValue` for damage/durability match collector formula (settle must run same tick or test issues a second `Update` — prefer asserting same-tick settle from Approach §7).
  - While at war, `DebugCycleCharacterCommand` for `ruler` / `military_advisor` / `economic_advisor`, `Update`, assert affected resource(s) match new characters' skills.
  - While at war, `DebugDropCharacterCommand` for a war-relevant role, `Update`, assert missing skill contributes `0`.
  - Save/load round-trip while at war: values present after `LoadState`; after settle (automatic in `LoadState` before VisualState update) match live formula; Daily effect still present if Instant was already consumed pre-save.

- **Extend `LoaderCountryPreservationTests`** — `baseDamage` / `baseDurability` preserved.

- **Extend `ResourceConfigTests` / targeted-init tests** if `None` seed target needs coverage: `FindResources(None)` returns catalog-only entries; Country seed path still does not create damage/durability at init (`InitSystemTests` or `TargetedResourceInitializationTests` assert peacetime countries lack those resources after first `GameLogic.Update`).

Run via the `dotnet-test` skill against `src/GlobalStrategy.Core.sln`.

## Constitution Check

Checked against `Docs/Constitution.md`.

**No conflicts found — plan aligns with all principles.**

- *ECS for all game logic in `src/`.* New/changed types live in `src/Game.Systems`, `src/Game.Configs`, `src/Game.Configs.Loader`, `src/Game.Main`, `src/Game.Tests` — no MonoBehaviour / Unity simulation state.
- *VContainer sole DI.* No new container registrations; collectors go into the existing directly-constructed `ResourceCollectorRegistry` inside `GameLogic`.
- *UI Toolkit only.* No UI surface; generic `BuildResources` only; War UI OOS.
- *URP only.* No rendering change.
- *Plan before implement / Spec before plan.* Approved spec at `Docs/Specs/26_07_29_16_damage-durability-at-war/spec.md`; this plan is the implement gate.
- *File organisation.* Plan lives beside its spec under `Docs/Specs/26_07_29_16_damage-durability-at-war/`.
- *One `.asmdef` per feature folder.* N/A — `src/` / `.csproj` only; no new `Assets/Scripts/` feature folder.
- *C# style.* Tabs, braces always, `_`-prefixed private members, no redundant access modifiers — matching `Wars.cs`, `OrgScoreCollector.cs`, `InitSystem` org Instant+Daily pattern.

Use the implement skill to start working on the plan or request changes.
