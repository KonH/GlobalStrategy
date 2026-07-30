# Spec: Damage and Durability at War

## Feature Intent

As a game designer, I want every available country to expose two country-scoped combat resources — `damage` and `durability` — derived from a per-country config baseline (1–100, reflecting 1880-era war technology) plus the relevant ruler and advisor character skills, and kept correct whenever those characters or skills change, so that a later battle/combat slice (and potential country view) can read stable offensive and defensive war strength without re-deriving skill math itself.

Depends on: `Docs/Specs/26_07_25_06_war-mechanics-core` (issue #69) and the resource-collector pipeline (`Docs/Specs/26_07_18_17_resource-collector-pipeline`).

## Owner amendments (2026-07-30)

- Resources are **always present** for available countries (InitSystem Country seed), not wartime-only — may be shown in country view later.
- `ResourceSeedTarget.None` is **not** used: it existed only to keep InitSystem from seeding wartime-only ids; with always-on lifecycle, `seedTarget: "Country"` is correct.
- Config bases are cached as a single `CountryCombatBases` entry keyed by country id (not two parallel dictionaries).
- Bundle version increments stay on the current major milestone (`1.99` → `1.100`), not a major bump.

## Acceptance Criteria

- **Given** an available country after init **When** that country's resources are queried **Then** it has live country `Resource` entities with `ResourceId` `damage` and `durability`, plus Instant + Daily collector effects that set each resource to `base + skillA + skillB` (see Tech Notes).
- **Given** a country (peacetime or at war) **When** its `damage` is computed **Then** `target = baseDamage + rulerPower + militaryAdvisorPower`, where `baseDamage` is that country's authored config integer in `[1, 100]`, `rulerPower` is the `power` skill `Resource` on the country's `ruler` character (or `0` if missing), and `militaryAdvisorPower` is the `power` skill on the `military_advisor` character (or `0` if missing). Theoretical max is `300` when base and both skills are `100`.
- **Given** a country **When** its `durability` is computed **Then** `target = baseDurability + rulerStinginess + economicAdvisorStinginess`, with the same additive shape, using config `baseDurability` in `[1, 100]` and the `stinginess` skill on roles `ruler` and `economic_advisor` (missing → `0`).
- **Given** per-country base damage and base durability **When** config is authored **Then** they live as new fields on `CountryEntry` in `Assets/Configs/country_config.json` (integers in `[1, 100]`), with the proposed values in Tech Notes applied for every `isAvailable: true` country and the documented default applied for all other countries.
- **Given** a country's ruler, military advisor, and/or economic advisor is replaced (e.g. `DebugCycleCharacterCommand`) **When** the character change is applied **Then** collectors reading live skill resources re-derive the affected resource(s) so `damage` / `durability` match the new characters' current skills (not deferred to a day/month boundary).
- **Given** a contributing skill `Resource.Value` mutates in place on the assigned ruler or relevant advisor **When** that mutation is applied **Then** the same collectors re-derive the affected resource(s) from the live skill values.
- **Given** only damage's or only durability's contributing roles/skills changed **When** recalculation runs **Then** the resource whose inputs changed ends correct; the other may be left unchanged if its inputs were untouched, as long as both remain correct relative to current config + skills.
- **Given** a country stops being at war (e.g. `DebugStopWarCommand` / `Wars.StopWar`) **When** its `WarParticipant` is removed **Then** that country's `damage` and `durability` resources and collectors remain present and correct (war lifecycle does not create/destroy them).
- **Given** attacker A and defender B are both in the same war **When** each side's damage/durability are read **Then** each country has its own independently computed pair from *that* country's config bases and *that* country's characters.
- **Given** damage and durability exist for a country **When** any other system or a future battle slice needs them **Then** they are queryable as ordinary country `Resource` values (same style as gold / recruits / `country_score`) without the consumer re-deriving skill math. No battle consumer is implemented in this feature.
- **Given** a save is loaded **When** init/load completes **Then** each available country's `[Savable]` `damage` / `durability` resources are present (persisted like gold) and collectors re-sync absolute values from current config bases + current character skills so values are not stale relative to skills.
- **Given** a country is selected **When** `VisualState.SelectedCountry.Resources` is built **Then** `damage` and `durability` appear automatically via existing `BuildResources` (no special display whitelist work in this feature; dedicated War / country-view UI is out of scope).

## Tech Notes

- **Dependency:** Read war participation only (`WarParticipant` / `Wars.IsInWar`). Do not change the war model from `Docs/Specs/26_07_25_06_war-mechanics-core`.
- **Formula (locked):** `final = base + skillA + skillB`. Base ∈ `[1, 100]`; each skill ∈ `[1, 100]` when present. Theoretical max `300`. Missing character or missing skill resource contributes `0` (final can equal base, or sit between base and 300). No extra clamp beyond that arithmetic. Integer/double storage follows existing country `Resource.Value` conventions.
- **Skill mapping (locked):** "military skill" → character skill resource id `power`. "stinginess" → `stinginess`. Roles: damage uses `ruler` + `military_advisor`; durability uses `ruler` + `economic_advisor`.
- **Config home (locked):** New fields on `CountryEntry` / `Assets/Configs/country_config.json` (e.g. `baseDamage`, `baseDurability`), preserved across GeoJSON regen the same way `historicalFriends` / `historicalRivals` are.
- **Unavailable-country rule (locked):** Only `isAvailable: true` countries get authored historical bases (table below). Every other country in `country_config.json` uses default **baseDamage = 40**, **baseDurability = 40**. (Mexico and other prompt-named ids that are not available in config are covered by this default; playable ids must match config, e.g. `Germany` not `German_Empire`, `Manchu_Empire` not `China`, `Kingdom_of_Brazil` not `Brazil`, `Russian_Empire` not `Russia`, `SwedenNorway` not `Sweden`, `United_Kingdom_of_Great_Britain_and_Ireland` not `Great_Britain`, `United_States_of_America` not `USA`, `Imperial_Japan` not `Japan`.)
- **ECS shape (locked):** Ordinary country `Resource` entities with ids `damage` and `durability` (`ResourceOwner(countryId, OwnerType.Country)`), `[Savable]` like gold.
- **Lifecycle (locked, amended 2026-07-30):** Always present for available countries via `ResourceSeedTarget.Country` + InitSystem Instant+Daily collectors (same dual-effect shape as org_score). War declare/stop does not create or destroy them. On save/load: resources persist; collectors re-sync from live skills.
- **Why not `ResourceSeedTarget.None`:** `None` was only a wartime-only escape hatch so InitSystem would not Country-seed these ids. Always-on lifecycle uses `Country` seed and InitSystem attachment instead; `None` is removed.
- **Collectors (locked):** Register Instant absolute collectors (same delta contract as `country_score` / `CountryPopulationCollector` / `OrgScoreCollector`: `return target - currentValue`). Collectors read live skill `Resource` values on the current characters for that country, so character cycle and in-place skill mutation are covered whenever the collector runs. Pair with Instant seed + Daily recurring effects so skill changes stay correct after the Instant effect self-destructs — without inventing a second pipeline. Config bases are supplied via a single `IReadOnlyDictionary<string, CountryCombatBases>` shared by both collectors.
- **VisualState (locked):** No special display whitelist. They surface like any other selected-country resource through `BuildResources`. Dedicated War UI later is out of scope.
- **Proposed base damage / base durability** (1880 war-tech / sustainment judgment; owner may amend):

| CountryId | Base Damage | Base Durability | Notes (1880) |
| --- | ---: | ---: | --- |
| `United_Kingdom_of_Great_Britain_and_Ireland` | 95 | 92 | Leading industrial and naval power; professional forces |
| `Germany` | 93 | 88 | Post-unification Prussian staff system; advanced arms industry |
| `France` | 85 | 82 | Major continental army and industry; recovering from 1870–71 |
| `United_States_of_America` | 78 | 90 | Vast industrial depth; small peacetime army but strong sustainment |
| `Austria_Hungary` | 72 | 70 | Large multi-ethnic army; competent but strained dual monarchy |
| `Russian_Empire` | 68 | 86 | Huge manpower and depth; modernization still uneven |
| `Italy` | 58 | 55 | Young kingdom; uneven training and industry |
| `Imperial_Japan` | 52 | 58 | Meiji military reforms underway; still catching great powers |
| `Netherlands` | 50 | 55 | Small professional force; colonial experience |
| `Belgium` | 48 | 58 | Industrialized; strong fortification tradition relative to size |
| `Ottoman_Empire` | 48 | 52 | Reforming but weakened relative to European peers |
| `SwedenNorway` | 46 | 52 | Small, decent-quality force; limited great-power projection |
| `Spain` | 45 | 48 | Declined relative to mid-century; colonial overstretch |
| `Argentina` | 44 | 42 | Modernizing regional army; thin industrial depth |
| `Kingdom_of_Brazil` | 42 | 48 | Large regional state; limited industrial war sustainment |
| `Egypt` | 40 | 38 | Mixed modern/traditional forces; increasing external dependence |
| `Portugal` | 38 | 42 | Small colonial military; limited European weight |
| `Ethiopia` | 36 | 55 | Traditional arms vs peers; notable defensive resilience |
| `Manchu_Empire` | 32 | 58 | Severe tech lag after mid-century wars; territorial depth |
| `Persia` | 28 | 35 | Weak Qajar military vs contemporary peers |
| *(all `isAvailable: false`)* | 40 | 40 | Documented default; no per-country historical authoring required |

## Out of Scope

- Any usage, consumption, spending, or modification of `damage` / `durability` by battles, combat resolution, war progress, or any other gameplay consumer — separate future battle spec.
- Any change to war declaration, war ending, war progress, or the `War` / `WarParticipant` / `WarProgress` model beyond reading participation to know who is at war.
- Any consumption or interaction with the existing `recruits` resource.
- Dedicated War window / HUD / tooltip UI for damage or durability (generic `BuildResources` appearance is enough).
- Natural war declaration, peace resolution, allies / multi-country wars, or any battle simulation.
- New character roles, new skill ids, or changes to how character skills are seeded/cycled — this feature only reads existing `power` / `stinginess` on existing `ruler` / `military_advisor` / `economic_advisor` roles.
- Bot/AI strategy that chooses wars or battles based on damage/durability.
