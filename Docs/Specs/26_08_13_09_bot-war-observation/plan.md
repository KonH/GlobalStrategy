# Plan: Bot War Observation Extensions (Child A)

## Spec summary

Source: `Docs/Specs/26_08_13_09_bot-war-observation/spec.md` (Child A of umbrella `Docs/Specs/26_08_01_09_bot-war-features/`, issue #83).

Extend the bot observation facade so seat-visible war / relation / progress / occupation / score / destroy / combat-input signals are available on `IBotObservation` and country views. Downstream war features (B–F) must be able to estimate Δorg_score (peace control shifts, occupation/province-transfer EV, destroy EV, gold) and call `WarWinChanceEstimator.EstimateAttackerWinPercent` without touching `World` or privileged foresight.

**Already shipped — do not redesign:** `BotCardView.TargetCountryId` / `BotCardDrawChoiceView.TargetCountryId` for relation and revenge cards.

**Out of this plan:** children B–F (interval, arbitration, discard, draw scorer, `warUnlock` / `warDeclare` / `warProsecute`, eval packages). They **depend on** these fields; do not implement them here.

## Goal

Add immutable observation fields (and one-pass `BotObservation.Build` population) for per-country war participation + opponent, own-side war progress, rival ids, owned + foreign-occupied province counts, `IsDestroyed`, `CountryScore`, combat resources used by `WarWinChanceEstimator`, plus observing-org `OrgScore` and public per-org score shares — with tests proving correctness, deterministic ordering, and no private-state leaks — so later war features can score plays from observation alone.

## Approach

### 1. Field design (locked semantics)

Extend DTOs in `src/Game.Bots/BotViews.cs` and surface what `IBotObservation` needs. Tabs, braces always, `_` private prefix on new impl members.

**`IBotObservation` / `BotObservation` additions:**

| Member | Type | Meaning |
|--------|------|---------|
| `OrgScore` | `double` | Observing org’s live `ResourceDefinitions.OrgScore` via `ResourceQuery` (same resource `OrgScoreCollector` writes). |
| `OrgScores` | `IReadOnlyList<BotOrgScoreView>` | **Public** leaderboard-style shares: every org id present as an org owner of `org_score`, ordinal by `OrgId`. Includes self. Seat-visible (matches VisualState org score projection). |

```csharp
public sealed class BotOrgScoreView {
	public string OrgId = "";
	public double OrgScore;
}
```

**`BotCountryView` additions** (every country already listed in `Countries` — all world countries today):

| Member | Type | Meaning |
|--------|------|---------|
| `IsDestroyed` | `bool` | `CountryDestroySystem.IsCountryDestroyed` / `Has<IsDestroyed>` on the country entity. |
| `IsAtWar` | `bool` | Country has a `WarParticipant` row (`Wars.IsInWar` / snapshot). |
| `WarOpponentCountryId` | `string` | Other participant’s country id when at war; `""` when not. Game is two-sided; if somehow missing opponent, leave `""` and still set `IsAtWar` only when a participant row exists. |
| `OwnWarProgress` | `double` | `Wars.GetOwnWarProgress(world, resources, countryId)` (attacker-signed; 0 when not at war). |
| `RivalCountryIds` | `IReadOnlyList<string>` | From `CountryRelations.GetRelationsByCountryId(...).Rivals`, sorted `StringComparer.Ordinal`. Empty when destroyed / none. |
| `CountryScore` | `double` | `resources.GetValue(world, countryId, ResourceDefinitions.CountryScore)`. |
| `OwnedProvinceCount` | `int` | Provinces with `ProvinceOwnership.OwnerId == countryId`. |
| `OccupiedOwnedProvinceCount` | `int` | Among owned provinces: `OccupierId` non-empty **and** `OccupierId != countryId`. Matches peace-transfer eligibility on the **loser** side in `Wars.TransferOccupiedProvinces` (eligible = loser-owned + foreign occupier). Expose for **every** country so both war sides are covered without a separate war-only path. |
| `Recruits` | `double` | `ResourceDefinitions.Recruits` |
| `Damage` | `double` | `ResourceDefinitions.Damage` (live value; sell_arms / revenge bonuses already folded into live combat resources when active). |
| `Durability` | `double` | `ResourceDefinitions.Durability` |
| `TroopsDamageBonusPercent` | `double` | Live `troops_damage_bonus_percent` on the country (seat-visible). Required so Child E can stack another `sell_arms` without reading `World` (additive stack uses current bonus, not revenge-replace pending). |

**Not observation fields (by design):**

- Pending hypothetical bonuses for cards not yet played (`pendingAttackerDamageBonusPercent` / durability for revenge, sell_arms preview). Features pass those as `WarWinChanceEstimator` args from `EffectConfig` when scoring a candidate play — same pattern as `VisualStateConverter` for declare/revenge win %. Live `TroopsDamageBonusPercent` is exposed so prosecute can compute the next additive stack from observation alone.
- War-card kind flags on `BotCardView` — `ActionId` is already sufficient for B–E matching; do not add redundant bools.
- Friend lists, multi-war / ally graphs (game remains two participants).
- Recomputing `OrgScore` via a bot-local copy of `OrgScoreCollector` — read the live resource.

**Score contribution:** features compute `(MyControl / 100.0) * CountryScore` from existing `MyControl` + new `CountryScore`. No dedicated contribution field required.

### 2. Build path (`BotObservation.Build`)

Keep the existing `ControlWarSnapshot.Build` for playability. Add **one additional aggregate pass** (private helpers on `BotObservation` or a small sealed helper in `Game.Bots`, e.g. `BotWorldWarScoreSnapshot`) built once per `Build`:

1. **War pairs** — single scan of `WarParticipant` archetypes → `countryId → (warId, kind)` then resolve opponent per war id (two participants). Do not call `Wars.GetOpponentCountryIds` per country (N rescans).
2. **Destroyed** — while scanning `Country` entities (already done for `countryIds`), record `Has<IsDestroyed>` into a `HashSet` / dictionary.
3. **Occupation / ownership** — one pass over `ProvinceOwnership` plus `ProvinceOccupationSystem.GetOccupierByProvinceId(world)` (or a single paired scan of both archetype columns) → per-country `OwnedProvinceCount` and `OccupiedOwnedProvinceCount`.
4. **Rivals** — for each `countryId`, `relations.GetRelationsByCountryId(world, countryId).Rivals`, copy + ordinal sort.
5. **Scores / combat** — `resources.GetValue` for country_score / recruits / damage / durability / **troops_damage_bonus_percent**; org_score for observing org; collect all org_score owners into `OrgScores` (scan org-seeded resource entities or known participating orgs — prefer a single resource scan filtered by `ResourceId == org_score`, ordinal by owner id). Prefer whatever pattern `VisualStateConverter.UpdateCountryScore` / org-score projection already uses if a shared query helper exists; otherwise local scan is fine.
6. **Progress** — `Wars.GetOwnWarProgress` per at-war country only (or read `war_progress` once per war id and apply attacker/defender sign from the pair map to avoid duplicate war-id lookups).

Wire new fields when constructing each `BotCountryView` and the observation ctor. Extend the private ctor + property list on `BotObservation` / `IBotObservation` accordingly.

Update `src/Game.Tests/BotObservationTests.cs` equality helpers (`AssertCountryEqual`, `AssertObservationsEqual`) to include the new members so determinism tests keep working.

### 3. Estimator contract for downstream (document only)

Children D/E will call:

```csharp
WarWinChanceEstimator.EstimateAttackerWinPercent(
	world, resources, attackerCountryId, defenderCountryId,
	pendingAttackerDamageBonusPercent, pendingAttackerDurabilityBonusPercent);
```

Observation does **not** wrap the estimator (features still need `IReadOnlyWorld` only inside host code — **features must not take World**). So either:

- **Preferred for later children:** add a thin `Game.Systems` overload or bot-facing pure helper that takes the numeric inputs (`recruits`, `damage`, `durability` for both sides + pending bonuses) — **only if** D/E `/plan` needs it; **out of Child A** unless implementers find `EstimateAttackerWinPercent` unusable without World. Child A ships the raw inputs on `BotCountryView` so a pure helper can be added later without another observation change.

- Child A acceptance is satisfied by exposing the inputs; do not move estimator into `Game.Bots` here.

### 4. Information hiding & ordering

- No other org hands/gold/slots.
- `OrgScore` property = observing org only; `OrgScores` list is the public leaderboard (all orgs’ scores), consistent with player-visible score UI.
- Undiscovered-country rules no longer apply (discovery removed); continue exposing all `Country` entities, including destroyed ones (flagged), matching map/destroy UX.
- Deterministic sorts: countries / org scores / rivals / control shares remain ordinal; hands by `SlotIndex`.

### 5. Explicit non-goals

- No `IBotFeature`, registry, eval config, `game_settings.json` cadence, or Assets changes.
- No Unity / VisualState / UI Toolkit work.
- Do not expose `ControlWarSnapshot` on `IBotObservation` (internal playability cache stays internal).

## Agent Steps

- [ ] **Extend view DTOs** — `BotOrgScoreView`; add Child A fields to `BotCountryView` in `src/Game.Bots/BotViews.cs` per Approach §1.
- [ ] **Extend `IBotObservation`** — add `OrgScore` and `OrgScores` in `src/Game.Bots/IBotObservation.cs`.
- [ ] **Implement Build aggregates** — update `src/Game.Bots/BotObservation.cs`: one-pass war-pair / destroy / ownership+occupation / rivals / score+combat population; extend ctor/properties; keep existing playability/`ControlWarSnapshot` path intact.
- [ ] **Update equality helpers** — `src/Game.Tests/BotObservationTests.cs` `AssertCountryEqual` / `AssertObservationsEqual` (and any other bot tests that construct/`Assert` country views) for the new fields.
- [ ] **Add observation coverage** — new facts in `BotObservationTests` (or a focused sibling file) per Tests below.
- [ ] **Run tests + Release build** — `dotnet test` on `src/GlobalStrategy.Core.sln`, then `/dotnet-build Release` (project workflow after any `src/` change).

## User Steps

### 1. None

None — `src/`-only observation/API/test work; no Unity Editor scene, prefab, or visual inspection steps.

## Tests

Extend `src/Game.Tests/BotObservationTests.cs` (or add `BotObservationWarScoreTests.cs` if the file grows too large). Use existing `GameLogic` / multi-org harness style.

- **`observation_exposes_war_opponent_and_own_progress`** — declare a war between two countries via `Wars.DeclareWar`; set war progress resource; observing org’s views show `IsAtWar`, matching `WarOpponentCountryId` both ways, and `OwnWarProgress` equal to `Wars.GetOwnWarProgress` (attacker +p, defender −p).
- **`observation_lists_rivals_ordinally`** — set rival relations; `RivalCountryIds` matches `CountryRelations` and is ordinal; non-rivals absent.
- **`observation_occupied_owned_province_count_matches_occupation_state`** — own N provinces, foreign-occupy K of them (`ProvinceOccupationSystem.SetOccupier`); assert `OwnedProvinceCount` / `OccupiedOwnedProvinceCount`. Clear occupier → count drops. Occupier == owner must **not** count as foreign occupied.
- **`observation_marks_destroyed_countries`** — country with `IsDestroyed` → `IsDestroyed == true`; living country false.
- **`observation_exposes_country_score_and_combat_inputs`** — set `country_score` / recruits / damage / durability; view fields match `ResourceQuery`.
- **`observation_org_score_and_public_org_scores`** — observing `OrgScore` matches own resource; `OrgScores` contains all orgs’ scores, ordinal by `OrgId`, values match resources (leaderboard-shaped; no other private fields).
- **`observation_war_fields_deterministic_across_rebuilds`** — build twice; war/occupation/score fields element-wise equal (extend existing determinism coverage).
- **Regression:** existing information-hiding / playability / ordering facts stay green; equality helpers include new fields so silent drops fail loudly.

Run: `dotnet test src/GlobalStrategy.Core.sln`, then Release build per workflow.

## Constitution Check

Checked against `Docs/Constitution.md`.

- *Unity 6 + URP only.* No rendering / Unity asset changes.
- *ECS for all game logic in `src/`.* Observation remains a read-only facade over `IReadOnlyWorld` + existing systems (`Wars`, `ProvinceOccupationSystem`, `CountryRelations`, `CountryDestroySystem`, `ResourceQuery`); no MonoBehaviour game logic.
- *VContainer sole DI.* No new Unity registrations; no static mutable singletons — snapshot helpers are pure/static or instance-local to `Build`.
- *UI Toolkit only.* No UI changes.
- *Plan before implement / Spec before plan.* Child A is explicitly outside the bot-feature carve-out; this paired `spec.md` + `plan.md` under `Docs/Specs/26_08_13_09_bot-war-observation/` is the planning artifact. No code precedes approval/implementation of this plan.
- *File organisation.* Spec+plan live in the dated Specs subdirectory (not legacy `Docs/Plans/`).
- *One `.asmdef` per `Assets/Scripts/` feature folder.* No `Assets/Scripts` changes.
- *C# code style.* Tabs, braces always, `_` private prefix, no redundant access modifiers — match surrounding `Game.Bots` files.

No conflicts found — plan aligns with all principles.

Use the implement skill to start working on the plan or request changes.
