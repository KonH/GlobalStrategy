# Plan: Declare War Card

## Spec

As a player, I want to play a "Declare War" card naming one of the selected country's current rivals, so a rivalry can be turned into an actual war without going through the debug terminal.

Condensed acceptance criteria:
- One `declare_war` card instance exists per current `Rival` relation the selected country holds (mirroring `stop_rivalry`'s one-instance-per-rival pattern), independent of any other instance.
- An instance is only drawable/playable when: the named rivalry still exists, the war-declaring country's ruler OR military advisor holds 50+ opinion of the player's org, neither side is already at war, and the 100-gold cost is affordable.
- Each unmet gate maps to its own distinct unplayable reason (`insufficient_target_opinion`, `already_at_war`, `relation_no_longer_exists`, ordinary insufficient-funds) — never a silent no-op and never a retarget.
- Playing a valid instance always succeeds (no roll), consumes the card, draws a replacement, and declares war between the selected country and the named rival via the existing `Wars.DeclareWar`.
- A successful declaration produces a Game Log entry naming the attacker and defender.
- No cooldown of any kind ever applies to this card.

## Goal

Add a player-facing "Declare War" country card that reuses the existing `stop_rivalry` per-instance-targeting plumbing, gates play on a new two-role opinion check and a new not-already-at-war check, and resolves by calling the already-merged `Wars.DeclareWar` helper, emitting a new `GameLogEntryKind.War` log line on success.

## Approach

Per the spec's Tech Notes, this is a pure config + `src/` ECS + UI-Toolkit-adjacent-C# change, no new components beyond one small event struct, no new systems:

- **Targeting:** reuse `RelationCardTarget`/`RelationCardSyncSystem` unchanged — add a second `EnsureCardInstance(..., RelationKind.Rival, "declare_war")` call inside the existing `foreach (string otherCountryId in rivals)` loop.
- **New gates on `ExpressionContext`:** `TargetRulerOrMilitaryOpinion` (max of the war-declaring country's ruler/military_advisor opinion of the player's org) and `NeitherSideAtWar` (1.0 unless either side is already in a war), each computed per-entity at the three existing `RelationStillExists` call sites (`DrawCardSystem.DrawCountryCards`, `ActionPlayability.Evaluate`, `VisualStateConverter.BuildEntry`).
- **New effect:** `DeclareWarEffectParams` (no fields), resolved inline in `CreateActionEffectSystem.Update` by reading the entity's own `RelationCardTarget` and calling `Wars.DeclareWar(world, countryId, targetCountryId, currentTime)` directly (a plain static helper call, not a system-to-system call).
- **New log type:** `GameLogEntryKind.War`, following `.claude/commands/propose-log-type.md` — a new one-shot `WarDeclaredApplied` event component created only when `Wars.DeclareWar` returns `true`, cleaned up next tick, read by `VisualStateConverter.UpdateGameLog`, rendered by a new `GameLogLineFormatter.BuildWarLine`.
- **Config:** one new `action_config.json` row (`declare_war`, `deckCopies: 0`, three `conditions`) and one new `effect_config.json` row (`declare_war_effect`, `effectType: "DeclareWar"`).
- **Locale:** new English + real-Russian-translated keys via the `localization` skill for the card name/desc, effect name/desc, two new unplayable reasons, and the log line format.

No Unity Editor / scene / prefab / UXML work is required — see Section 2 below.

## Section 1 — Agent Steps

- [ ] **Add `TargetRulerOrMilitaryOpinion` and `NeitherSideAtWar` to `ExpressionContext`/`ExpressionNode`** — `src/Game.Configs/ExpressionNode.cs`: add both `double` properties to `ExpressionContext`, and `case "targetRulerOrMilitaryOpinion": return ctx.TargetRulerOrMilitaryOpinion;` / `case "neitherSideAtWar": return ctx.NeitherSideAtWar;` to `ExpressionNode.Evaluate`.

- [ ] **Add `DeclareWarEffectParams` and register it** — `src/Game.Configs/EffectConfig.cs`: add `public class DeclareWarEffectParams : ActionEffectDefinition { }` alongside the existing effect param classes, and `case "DeclareWar": item = obj.ToObject<DeclareWarEffectParams>(serializer)!; break;` in `ActionEffectDefinitionListConverter`'s switch.

- [ ] **Add `WarDeclaredApplied` event component** — `src/Game.Components/GameLogEffects.cs`: add `public struct WarDeclaredApplied { public string OrgId; public string CountryId; public string DefenderCountryId; }` alongside `RelationSetApplied` (no `[Savable]`, matching the other event structs in this file).

- [ ] **Compute the two new gates in `DrawCardSystem.DrawCountryCards`** — `src/Game.Systems/DrawCardSystem.cs`, inside the per-entity loop (alongside the existing `ctx.RelationStillExists = ...` assignment): when `world.Has<RelationCardTarget>(candidateEntity)`, resolve `countryId`'s `ruler`/`military_advisor` via `CharacterQuery.GetTargetCharacterByCountryAndRole` + `ResourceQuery.GetValue(..., $"opinion_{orgId}")`, take the max for `ctx.TargetRulerOrMilitaryOpinion` (default `0.0` when absent), and set `ctx.NeitherSideAtWar` via `Wars.IsInWar(world, countryId)` / `Wars.IsInWar(world, target.TargetCountryId)` (default `1.0` when absent). Rival-country character opinion is ignored.

- [ ] **Compute the two new gates in `ActionPlayability.Evaluate`** — `src/Game.Systems/ActionPlayability.cs`: same computation as above, added alongside the existing `relationStillExists` block, included in the `ExpressionContext` passed to `ExpressionNode.Evaluate`.

- [ ] **Compute the two new gates in `VisualStateConverter.BuildEntry`, and map their failure to new unplayable reasons** — `src/Game.Main/VisualStateConverter.cs`: same computation alongside the existing `relationStillExists` block (~line 619-623); extend the `fieldType switch` (~line 640-645) with `"targetRulerOrMilitaryOpinion" => "insufficient_target_opinion"` and `"neitherSideAtWar" => "already_at_war"`.

- [ ] **Render the two new unplayable reasons in `CountryActionsView.cs`** — `Assets/Scripts/Unity/UI/CountryActionsView.cs`'s `BuildHandCard` reason-text `switch` (~lines 74-85) is the only place `ActionCardEntry.UnplayableReason` becomes player-facing text; unmapped reasons silently fall into the `_ =>` "Requires {0} control" branch. Add `"insufficient_target_opinion" => string.Format(_loc.Get("action.country.unplayable.insufficient_target_opinion"), def != null ? ExtractConditionThreshold(def, "targetRulerOrMilitaryOpinion") : 0),` and `"already_at_war" => _loc.Get("action.country.unplayable.already_at_war"),` as new cases, following the existing `insufficient_opinion`/`no_suitable_target` patterns respectively (the former needs the `{0}` threshold placeholder like `insufficient_opinion`; the latter is a plain message like `relation_no_longer_exists`).

- [ ] **Resolve `DeclareWarEffectParams` in `CreateActionEffectSystem.Update`** — `src/Game.Systems/CreateActionEffectSystem.cs`: new `else if` branch alongside the `ClearCountryRelationEffectParams` branch, guarded by `effectDef is DeclareWarEffectParams && !string.IsNullOrEmpty(countryId) && world.Has<RelationCardTarget>(entity)`; read `targetCountryId` from `RelationCardTarget`, call `bool declared = Wars.DeclareWar(world, countryId, targetCountryId, currentTime);`, and only when `declared` is `true`, create a `WarDeclaredApplied { OrgId = orgId, CountryId = countryId, DefenderCountryId = targetCountryId }` entity.

- [ ] **Clean up `WarDeclaredApplied` next tick** — `src/Game.Systems/CleanupEffectNotificationsSystem.cs`: add `RemoveComponent<WarDeclaredApplied>(world);` to `UpdateActionEffects`, alongside the existing `RelationSetApplied`/`RelationClearedApplied` sweeps.

- [ ] **Add `GameLogEntryKind.War` and scan `WarDeclaredApplied` into `GameLogEntry`** — `src/Game.Main/VisualState.cs`: add `War` to the `GameLogEntryKind` enum (no new `GameLogEntry` field — reuse `TargetCountryId`). `src/Game.Main/VisualStateConverter.cs`'s `UpdateGameLog`: add a scan block for `WarDeclaredApplied` mirroring the `RelationSetApplied` block, respecting `_gameLogIncludePlayerActions`, emitting `new GameLogEntry(0, GameLogEntryKind.War, applied[i].OrgId, applied[i].CountryId, "", "", Array.Empty<string>(), 0, 0, false, applied[i].DefenderCountryId)`.

- [ ] **Add `EnsureCardInstance` call for `declare_war` in `RelationCardSyncSystem`** — `src/Game.Systems/RelationCardSyncSystem.cs`: inside `foreach (string otherCountryId in rivals)`, add `EnsureCardInstance(world, orgId, countryId, otherCountryId, RelationKind.Rival, "declare_war");` alongside the existing `stop_rivalry` call.

- [ ] **Add `GameLogLineFormatter.BuildWarLine` and wire it into `ActionLogView`** — `Assets/Scripts/Unity/UI/GameLogLineFormatter.cs`: new method alongside `BuildRelationLine`, coloring org/attacker/defender names via `WrapColored`/`countryVisualConfig`/`orgVisualConfig`, then `string.Format(loc.Get("game_log.war_declared_format"), orgName, countryName, defenderName)`. `Assets/Scripts/Unity/UI/ActionLogView.cs`: add `GameLogEntryKind.War => GameLogLineFormatter.BuildWarLine(entry, _loc, _countryVisualConfig, _orgVisualConfig),` to the `entry.Kind switch` in `BuildLabel`.

- [ ] **Add the new locale keys (English + real Russian translation)** — use the `localization` skill to add to `Assets/Localization/en.asset`/`ru.asset`: `game_log.war_declared_format` (e.g. `"{0}: {1} declares war on {2}!"`), `action.declare_war.name` (e.g. `"Declare war on {0}!"`, templated like `stop_rivalry`), `action.declare_war.desc` (short practical form, e.g. `"Declare war on this rival."`), `effect.declare_war_effect.name`/`.desc`, `action.country.unplayable.insufficient_target_opinion` (with a `{0}` threshold placeholder, mirroring `insufficient_opinion`'s `Value: Requires {0} opinion with the diplomacy advisor`, e.g. `"Requires {0} opinion from the country's ruler or military advisor"`), `action.country.unplayable.already_at_war` (plain message, no placeholder, mirroring `relation_no_longer_exists`).

- [ ] **Add `declare_war` to `Assets/Configs/action_config.json` and `declare_war_effect` to `Assets/Configs/effect_config.json`** — action row: `actionId: "declare_war"`, `ownerType: "country"`, `nameKey: "action.declare_war.name"`, `descKey: "action.declare_war.desc"`, `targetRole: ""`, `deckCopies: 0`, `cost: [{ "resourceId": "gold", "amount": 100.0 }]`, `effectIds: ["declare_war_effect"]`, `conditions`: `targetRulerOrMilitaryOpinion >= 50`, `relationStillExists >= 1`, `neitherSideAtWar >= 1` (three `gte` nodes). Effect row: `effectId: "declare_war_effect"`, `effectType: "DeclareWar"`, `nameKey: "effect.declare_war_effect.name"`, `descKey: "effect.declare_war_effect.desc"`.

- [ ] **Add/update tests** — see Tests section below for the concrete list; touches `src/Game.Tests/ExpressionNodeTests.cs`, `ActionPlayabilityTests.cs`, `DrawCardSystemTests.cs`, a new `VisualStateConverterCountryActionsWarGateTests.cs` (or extend the existing opinion-gate test file), `RelationCardSyncSystemTests.cs`, and a new `CreateActionEffectSystemDeclareWarTests.cs` (or extend an existing `CreateActionEffectSystem`-adjacent test file if the search below finds one already covering `ClearCountryRelationEffectParams`).

## Section 2 — User Steps

None. This feature has no Unity Editor, scene, prefab, or UXML surface — the card is drawn/rendered entirely through the existing generic country-card hand/deck UI (already data-driven off `ActionCardEntry`/`CountryActionsState`) and the existing `ActionLogView` UXML, neither of which needs new elements: the new log kind reuses the same `Label`-per-entry rendering as every other `GameLogEntryKind`. All work is config + `src/` C# + `Assets/Scripts/Unity/UI/*.cs` + locale assets, all of which Claude can write directly.

## Tests

- **`ExpressionNodeTests.cs`** — add cases for `"targetRulerOrMilitaryOpinion"` and `"neitherSideAtWar"` evaluating from `ExpressionContext`, mirroring the existing `"relationStillExists"`/`"opinion"` cases.
- **`ActionPlayabilityTests.cs`** — add a `declare_war`-shaped `ActionDefinition` to `BuildActionConfig` (three conditions: `targetRulerOrMilitaryOpinion >= 50`, `relationStillExists >= 1`, `neitherSideAtWar >= 1`) and tests mirroring the existing `stop_friendship_*` cases:
  - unplayable when the rival relation no longer holds (reuse `relationStillExists` pattern).
  - unplayable when neither the war-declaring country's ruler nor military advisor meets 50 opinion.
  - playable when either the ruler OR the military advisor alone meets 50 opinion (two separate cases, proving the "OR" — not just "ruler only").
  - unplayable when either side is already at war (two cases: attacker at war with a third country; defender at war with a third country), using `Wars.DeclareWar` to seed the war state.
  - playable when all three gates are met and cost is affordable; unplayable when unaffordable despite gates met.
- **`DrawCardSystemTests.cs`** — mirror the `stop_friendship`/`relationStillExists` eligibility tests: a `declare_war` instance is not eligible for a free-slot draw when opinion is below threshold, when neither side gate fails (already at war), or when the relation no longer exists; it is eligible when all three gates pass.
- **`VisualStateConverterCountryActionsOpinionGateTests.cs`** (or a new sibling file) — assert `BuildEntry` maps a failed `targetRulerOrMilitaryOpinion` condition to `unplayableReason == "insufficient_target_opinion"` and a failed `neitherSideAtWar` condition to `"already_at_war"`, distinct from `"insufficient_opinion"`/`"relation_no_longer_exists"`.
- **`RelationCardSyncSystemTests.cs`** — add a test that a `Rival` relation produces both a `stop_rivalry` instance and a `declare_war` instance (two independent entities, same `TargetCountryId`/`Kind`), mirroring `simultaneous_friend_and_rival_relations_produce_two_independent_entities`; and that a second, simultaneous rival (e.g. Spain + Portugal) produces two independent `declare_war` instances.
- **New `CreateActionEffectSystem` test (extend the file covering `ClearCountryRelationEffectParams`, or add `CreateActionEffectSystemDeclareWarTests.cs` if none exists)**:
  - `DeclareWarEffectParams` resolution calls `Wars.DeclareWar` with the entity's `CountryContext.CountryId` as attacker and `RelationCardTarget.TargetCountryId` as defender, and creates a `WarDeclaredApplied` entity only when it returns `true`.
  - When `Wars.DeclareWar` returns `false` (e.g. one side already at war, reachable only via the same in-flight-tick race the `neitherSideAtWar` gate defends against), no `WarDeclaredApplied` entity is created.
- **`CleanupEffectNotificationsSystem`** — extend whatever existing test covers `UpdateActionEffects`' sweep (or add one) to assert `WarDeclaredApplied` is removed the tick after `UpdateActionEffects` runs, mirroring the `RelationSetApplied`/`RelationClearedApplied` coverage.
- **`VisualStateConverter` game-log test** (`src/Game.Tests/GameLogStateTests.cs`, which already covers `RelationSetApplied` -> `GameLogEntryKind.Relation` via its `BuildLogic()`/`Entries(logic)` harness — not `UnifiedPipelineTests.cs`, which covers the unrelated `RelationClearedApplied` event) — a `WarDeclaredApplied` entity produces exactly one `GameLogEntryKind.War` entry with `CountryId` = attacker, `TargetCountryId` = defender, respecting `_gameLogIncludePlayerActions` the same way the `RelationSetApplied` block already is tested.
- **`GameLogLineFormatter`** (wherever `BuildRelationLine` is tested, if a dedicated formatter test file exists under `src/Game.Tests` or an editor test folder) — `BuildWarLine` produces the expected formatted string with both country names and the org name colored.
- **End-to-end** — extend `WarsTests.cs`-style coverage (or `UnifiedPipelineTests.cs`) with a full-pipeline test: seed a `Rival` relation, run `RelationCardSyncSystem` + `DrawCardSystem` to get a `declare_war` instance into hand, satisfy the opinion gate, play it via the standard `CheckActionConditionSystem` → `DeductActionCostSystem` → `ActionSucceededSystem` → `CreateActionEffectSystem` → `RemoveCardFromHandSystem` pipeline, and assert `Wars.IsInWar` becomes true for both sides and a replacement card is drawn into the vacated slot.

## Constitution Check

No conflicts found — plan aligns with all principles:
- **ECS for all game logic:** all new gates/effects/log wiring live in `src/Game.Systems`/`src/Game.Configs`/`src/Game.Components`; the only `Assets/Scripts` changes are the existing pure-C# `GameLogLineFormatter`/`ActionLogView` presentation layer, unchanged in kind from the existing `Relation`/`Opinion`/etc. line types.
- **VContainer-only DI:** no new services, no `new` singletons, no `FindObjectOfType` introduced.
- **UI Toolkit only:** no Canvas/UGUI; no new UXML/scene/prefab surface at all (Section 2 is empty).
- **C# style:** new structs/classes follow tabs, `_`-prefixed-private, always-braces, no-redundant-modifiers conventions already used throughout the files being edited.
- **No system-to-system calls:** `Wars.DeclareWar` is a plain static helper (not a system entry point invoked from the top-level loop), called from inside `CreateActionEffectSystem`'s per-entity loop exactly as `EnemyControlDrainEffectParams`'s branch already calls `ControlQuery` helpers inline.
- **Spec before plan:** `Docs/Specs/26_07_29_21_declare-war-card/spec.md` already exists and is fully resolved.
- **File organisation:** this plan is written to `Docs/Specs/26_07_29_21_declare-war-card/plan.md`, the same dated folder as the spec.

Use the implement skill to start working on the plan or request changes.
