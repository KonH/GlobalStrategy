# Plan: Revenge Card

## Spec

As a player, playing the "Revenge" country action card drags the org's home country (HQ) into a war against the selected country, as a direct player-facing alternative to debug-only war declaration.

Key acceptance criteria (see `spec.md` for full detail):
- `revenge`: country-owned card, 50 gold, gated on `control ≥ 20`, `opinion ≥ 25` with the selected country's `military_advisor`, and both the selected country and the org's HQ country being war-free. Not drawable/playable when any condition is false; re-evaluated live (no discard, no push-into-hand).
- Playing always succeeds (no roll): declares a war with the org's HQ as attacker and the selected country as defender, the card leaves hand, a replacement is drawn as usual.
- The new war grants the attacker only a temporary bonus: `damage` +10%, `durability` +5%, applied multiplicatively on top of the live `base + skillA + skillB` collector formula (issue #71). Each month the bonus decays (damage −1pt/month, durability −0.5pt/month, both configurable), floored at 0; decay never ends the war itself.
- A second "Revenge" copy in the same hand is unplayable while a war between HQ and that country is ongoing (same live gate, not a new mechanic).
- Discard-on-win is explicitly deferred (no win-detection mechanic exists yet).

## Goal

Add the `revenge` card end-to-end (config, condition gate, declare-war effect, attacker-only bonus component + decay + collector wiring), generalizing the existing hardcoded-`diplomacy_advisor` Opinion resolution to use `ActionDefinition.TargetRole` so `military_advisor` (and any future role) works without touching other cards' behavior.

## Approach

**Four call sites build `ExpressionContext`, not two.** The spec's Tech Notes name `ActionPlayability.Evaluate` and `DrawCardSystem.DrawCountryCards`. Verified against current tree: `InitSystem.CreateCountryActionEntities` (initial hand fill, `src/Game.Main/InitSystem.cs:700`) and `VisualStateConverter.BuildEntry` (`src/Game.Main/VisualStateConverter.cs:624`) also hardcode `"diplomacy_advisor"` and build a `ctx` — this is the exact same "4th/5th site missed" pattern the `decrease_enemy_control` plan-review caught for `totalCountryControl`. All four must resolve `Opinion` via `TargetRole` and compute `WarFree`, or the initial hand and the Actions-panel display would silently diverge from `ActionPlayability`/`DrawCardSystem`.

**Opinion must move inside the per-action loop, not stay hoisted per-country.** `DrawCountryCards` and `InitSystem`'s hand-fill currently compute `opinion` **once** per country (against `diplomacy_advisor`) before iterating candidate cards. Because different cards can now target different roles (`revenge`→`military_advisor`, `make_friend`→`diplomacy_advisor`) in the *same* draw/init pass, `Opinion` must be recomputed per-candidate keyed by that candidate's own `def.TargetRole`. `ActionPlayability.Evaluate`/`VisualStateConverter.BuildEntry` already operate on one action at a time, so they just swap the hardcoded string for `def.TargetRole` — no loop restructuring needed there.

**HQ-country resolution: use the existing `hqCountryByOrgId` dictionary pattern, not `OrganizationConfig.FindById` at every site.** The spec's Tech Notes cite `OrganizationConfig.FindById(orgId).HqCountryId`. In the current tree this exact lookup is already precomputed once in `GameLogic`'s constructor as `_hqCountryByOrgId` (`Dictionary<string,string>`) and threaded into consumers that need it — `VisualStateConverter` already holds it as a ctor field, `DiscoverCountrySystem.Update` already takes it as a parameter. `ActionPlayability.Evaluate` has ~20 call sites (incl. `BotObservation.Build`, which itself has ~20 more) and `DrawCardSystem.Update`/`CheckActionConditionSystem.Update` have several each; threading a *new required* parameter through all of them is unnecessary churn when the dictionary is already sitting in `GameLogic`. Decision: add `IReadOnlyDictionary<string, string>? hqCountryByOrgId = null` as a **trailing optional parameter** on `ActionPlayability.Evaluate`, `CheckActionConditionSystem.Update`, `DrawCardSystem.Update`, and `CreateActionEffectSystem.Update`; only `GameLogic.cs`'s real call sites pass `_hqCountryByOrgId`. `InitSystem` already has `orgEntry.HqCountryId` directly in scope (no dictionary needed there). `VisualStateConverter.BuildEntry` uses its existing `_hqCountryByOrgId` field. Net effect: zero required-signature breaks for the ~40 existing test/bot call sites; only 4 production call sites in `GameLogic.cs` change.

**Bot path intentionally not wired.** `BotObservation.Build` → `ActionPlayability.Evaluate` will keep resolving `hqCountryByOrgId` as `null` since `Bot.cs`/`BotObservation.Build` have no HQ-lookup plumbed in today. With a `null` dict, `Wars.IsWarFree` falls back to its string overload with an empty `hqCountryId`, so `WarFree` still correctly reflects whether the *target* country is at war — it only fails to account for the org's own HQ being at war (bots can appear eligible to play `revenge` while their HQ is already fighting someone else). This is acceptable per the spec's Out of Scope exclusion of bot/AI strategy, but it is a partial gate, not "never blocking." If a future bot feature needs full parity, thread `hqCountryByOrgId` into `Bot.ExecuteDecisionTick`/`BotObservation.Build` then.

**`Wars.DeclareWar` needs to hand back the generated `WarId`** so the declare-war effect can stamp it onto `RevengeWarBonus`. Add a second overload `DeclareWar(world, attacker, defender, currentTime, out string? warId)`; the existing 4-arg overload delegates to it (`out _`) — zero behavior change for the ~10 existing callers/tests of the 4-arg form.

**Declare-war effect resolves inline in `CreateActionEffectSystem.Update`** (no marker component/new system), mirroring `decrease_enemy_control`'s inline `ControlQuery` calls per the spec.

**Same-tick bonus visibility requires the established `SettleCombatResources()` pattern.** `damage`/`durability` are `Daily`-gated absolute collectors (issue #71); `CreateActionEffectSystem.Update` runs *after* this tick's `ResourceSystem.Update` (line 117), so without a forced settle the new `RevengeWarBonus` wouldn't be reflected until the next in-game day boundary — exactly the staleness problem `GameLogic.SettleCombatResources()` already exists to solve for character-cycle. `CreateActionEffectSystem.Update` returns `bool` (`true` iff a war was declared this call) — a non-breaking change (`void`→`bool`, no caller captures the old return) — so `GameLogic.Update` can call `SettleCombatResources()` conditionally, matching the `IsWarRelevantRole` gating already used for `ApplyDebugCycleCharacter`/`ApplyDebugDropCharacter`.

**Bonus formula reads live via `RevengeWarBonusQuery`, no constructor change to the collectors.** `DamageCollector`/`DurabilityCollector.Compute` already receive `world` — add `* (1 + RevengeWarBonusQuery.GetBonusPercent(world, ownerId, "damage"|"durability") / 100.0)` around the existing `base + skillA + skillB` sum. Non-attacker/peacetime countries have no `RevengeWarBonus` entity, so `GetBonusPercent` returns `0` and the multiplier is `1` — byte-identical to today for every country except an active attacker.

**Monthly decay is its own system, not folded into `WarSystem`.** `RevengeWarBonus` must keep decaying (and the collectors must keep reading it) even if the war ends early via debug stop-war — spec's Out of Scope forbids changing `Wars.StopWar`/`WarSystem` for this feature, so nothing destroys `RevengeWarBonus` on war end; it simply decays to `0/0` over time and then behaves as a no-op forever after, consistent with `WarProgress`'s own floor-clamp-not-destroy precedent. New `RevengeWarBonusDecaySystem.Update`, month-boundary-gated exactly like `WarSystem.Update`, wired as its own call right after `WarSystem.Update` in `GameLogic.Update`. Decay applies *after* this tick's `ResourceSystem.Update` already ran, so the decayed value is visible starting the next day boundary — no forced settle added for decay (mirrors `WarSystem`'s own undisturbed monthly decay, which nothing force-settles either).

**Opinion-gate UI text needs a role-aware generalization too.** `CountryActionsView.cs`'s `"insufficient_opinion"` message is hardcoded to `"...with the diplomacy advisor"` — for `revenge` (military advisor) that text would be wrong once the underlying gate is generalized. Fix: format with `_loc.Get($"character.role.{def.TargetRole}.name")` (key already exists for all 4 advisor roles) as a second placeholder; existing cards keep identical rendered text since they still resolve to "Diplomacy Advisor".

## Agent Steps

- [x] **`ExpressionContext`/`ExpressionNode`: add `WarFree`** — `src/Game.Configs/ExpressionNode.cs`: add `public double WarFree { get; set; }` to `ExpressionContext`; add `case "warFree": return ctx.WarFree;` to `Evaluate`.

- [x] **`Wars.cs`: add `IsWarFree` + out-`warId` `DeclareWar` overload** — `src/Game.Systems/Wars.cs`:
  - `public static bool IsWarFree(IReadOnlyWorld world, string countryId, string hqCountryId)` → `false` if either non-empty id is `IsInWar`; else `true`.
  - `public static bool IsWarFree(IReadOnlyWorld world, string countryId, string orgId, IReadOnlyDictionary<string, string>? hqCountryByOrgId)` → resolves `hqCountryId` from the dict (empty string if missing/null) and delegates to the string overload.
  - `public static bool DeclareWar(World world, string attackerCountryId, string defenderCountryId, DateTime currentTime, out string? warId)` — same guards/body as today, sets `warId` to the generated id on success, `null` on no-op; the existing 4-arg `DeclareWar` becomes a one-line delegate (`return DeclareWar(world, attackerCountryId, defenderCountryId, currentTime, out _);`).

- [x] **Generalize Opinion resolution + add `WarFree` at all 4 context-building sites**:
  - `src/Game.Systems/ActionPlayability.cs`: add trailing `IReadOnlyDictionary<string, string>? hqCountryByOrgId = null` param; replace hardcoded `"diplomacy_advisor"` with `def.TargetRole`; add a hoisted `double warFree = 1.0;` local next to the existing `opinion`/`hasSuitableTarget` locals, set it to `Wars.IsWarFree(world, countryId, orgId, hqCountryByOrgId) ? 1.0 : 0.0` inside the existing `!string.IsNullOrEmpty(countryId)` block (same pattern as `opinion`), then add `WarFree = warFree` to the `ctx` object initializer below (`ctx` isn't constructed until after this block, so it can't be mutated from inside it).
  - `src/Game.Systems/CheckActionConditionSystem.cs`: add trailing optional `hqCountryByOrgId` param, forward to `ActionPlayability.Evaluate`.
  - `src/Game.Systems/DrawCardSystem.cs`: add trailing optional `hqCountryByOrgId` param on `Update`, thread through `DrawCards`/`DrawCountryCards`. In `DrawCountryCards`, stop precomputing `opinion` once per country — move `def.TargetRole`-keyed opinion + `RelationStillExists` resolution inside the per-candidate loop (mirroring the existing `RelationStillExists` per-candidate pattern already there); compute `ctx.WarFree` once per draw call via `Wars.IsWarFree(world, countryId, orgId, hqCountryByOrgId)`.
  - `src/Game.Main/InitSystem.cs` (`CreateCountryActionEntities`): move the per-country hoisted `opinion`/`ctx.Opinion` into the per-action loop (`foreach (var (e, actionId) in createdEntities)`), keyed by that action's `TargetRole` via `actionConfig.Find(actionId)` (already resolved as `d` there); compute `ctx.WarFree` once per country/org via `Wars.IsWarFree(world, entry.CountryId, orgEntry.HqCountryId)` (direct string overload — `orgEntry` already in scope).
  - `src/Game.Main/VisualStateConverter.cs` (`BuildEntry`): replace hardcoded `"diplomacy_advisor"` with `def.TargetRole`; add `ctx.WarFree = Wars.IsWarFree(world, countryId, orgId, _hqCountryByOrgId) ? 1.0 : 0.0;` (existing ctor field); add `"warFree" => "at_war",` to the `failedReason` `fieldType` switch.

- [x] **UI unplayable-reason + role-aware opinion text** — `Assets/Scripts/Unity/UI/CountryActionsView.cs`: add `"at_war" => _loc.Get("action.country.unplayable.at_war"),` case; change the `"insufficient_opinion"` case to `string.Format(_loc.Get("action.country.unplayable.insufficient_opinion"), def != null ? ExtractConditionThreshold(def, "opinion") : 0, def != null ? _loc.Get($"character.role.{def.TargetRole}.name") : "")` — keep the same `def != null` guard the threshold argument already uses, applied to the new role-name argument too.

- [x] **`RevengeWarBonus` component** — new `src/Game.Components/RevengeWarBonus.cs`: `[Savable] public struct RevengeWarBonus { public string WarId; public string CountryId; public double DamageBonusPercent; public double DurabilityBonusPercent; }`.

- [x] **`RevengeWarBonusQuery`** — new `src/Game.Systems/RevengeWarBonusQuery.cs` (plain helper, mirrors `Wars.IsInWar`'s scan shape): `public static double GetBonusPercent(IReadOnlyWorld world, string countryId, string kind)` (`kind` is `"damage"` or `"durability"`) scans `RevengeWarBonus` for a `CountryId` match, returns the matching percent field or `0` if none. Also add `public static void RemoveForCountry(World world, string countryId)` (destroys any existing `RevengeWarBonus` entities for that `countryId`, `Wars.StopWar`-style collect-then-destroy) so `CreateActionEffectSystem` can guarantee at most one `RevengeWarBonus` per country when a new Revenge war is declared for an HQ that still carries a stale, undecayed bonus from an earlier war.

- [x] **`DeclareRevengeWarEffectParams`** — `src/Game.Configs/EffectConfig.cs`: `public class DeclareRevengeWarEffectParams : ActionEffectDefinition { public double DamageBonusPercent { get; set; } public double DurabilityBonusPercent { get; set; } }`; register `case "DeclareRevengeWar": item = obj.ToObject<DeclareRevengeWarEffectParams>(serializer)!; break;` in `ActionEffectDefinitionListConverter`.

- [x] **Dispatch declare-war effect in `CreateActionEffectSystem`** — `src/Game.Systems/CreateActionEffectSystem.cs`: change `Update` to `public static bool Update(World world, ActionConfig actionConfig, EffectConfig effectConfig, DateTime currentTime, IReadOnlyDictionary<string, string>? hqCountryByOrgId = null)`; track `bool anyWarDeclared = false;`; add a branch:
  ```
  else if (effectDef is DeclareRevengeWarEffectParams revengeParams && !string.IsNullOrEmpty(countryId)
      && hqCountryByOrgId != null && hqCountryByOrgId.TryGetValue(orgId, out string? hqCountryId) && !string.IsNullOrEmpty(hqCountryId)) {
      if (Wars.DeclareWar(world, hqCountryId, countryId, currentTime, out string? warId)) {
          anyWarDeclared = true;
          RevengeWarBonusQuery.RemoveForCountry(world, hqCountryId);
          int be = world.Create();
          world.Add(be, new RevengeWarBonus {
              WarId = warId ?? "",
              CountryId = hqCountryId,
              DamageBonusPercent = revengeParams.DamageBonusPercent,
              DurabilityBonusPercent = revengeParams.DurabilityBonusPercent
          });
      }
  }
  ```
  Return `anyWarDeclared` at the end of `Update`.

- [x] **`RevengeWarBonusDecaySystem`** — new `src/Game.Systems/RevengeWarBonusDecaySystem.cs` (mirrors `WarSystem.cs`'s month-boundary shape): `Update(World world, DateTime previousTime, DateTime currentTime, double damageDecayPerMonth, double durabilityDecayPerMonth)` — on month boundary, for every `RevengeWarBonus`, `DamageBonusPercent = Math.Max(0, DamageBonusPercent - damageDecayPerMonth)` and same for `DurabilityBonusPercent`.

- [x] **Apply the bonus in the collectors** — `src/Game.Systems/DamageCollector.cs` / `DurabilityCollector.cs`: multiply the existing `base + skillA + skillB` sum by `(1 + RevengeWarBonusQuery.GetBonusPercent(world, ownerId, "damage"|"durability") / 100.0)` before computing `target - currentValue`.

- [x] **`GameSettings` + config: bonus decay rates** — `src/Game.Configs/GameSettings.cs`: add `public double RevengeDamageBonusDecayPerMonth { get; set; } = 1.0;` and `public double RevengeDurabilityBonusDecayPerMonth { get; set; } = 0.5;`. `Assets/Configs/game_settings.json`: add `"revengeDamageBonusDecayPerMonth": 1.0,` and `"revengeDurabilityBonusDecayPerMonth": 0.5,` siblings to `attackerWarProgressDecayPerMonth`.

- [x] **Wire everything into `GameLogic.cs`**:
  - After the existing `WarSystem.Update(...)` call (~line 119): `RevengeWarBonusDecaySystem.Update(_world, _previousTime, currentTime, GameSettings.RevengeDamageBonusDecayPerMonth, GameSettings.RevengeDurabilityBonusDecayPerMonth);`.
  - `CheckActionConditionSystem.Update(_world, _actionConfig);` → `CheckActionConditionSystem.Update(_world, _actionConfig, _hqCountryByOrgId);`.
  - `CreateActionEffectSystem.Update(_world, _actionConfig, _effectConfig, currentTime);` → `bool revengeWarDeclared = CreateActionEffectSystem.Update(_world, _actionConfig, _effectConfig, currentTime, _hqCountryByOrgId); if (revengeWarDeclared) { SettleCombatResources(); }`.
  - `DrawCardSystem.Update(_world, _actionConfig, _rng);` → `DrawCardSystem.Update(_world, _actionConfig, _rng, _hqCountryByOrgId);`.

- [x] **`Assets/Configs/action_config.json`: new `revenge` action** — append, `ownerType: "country"`, `targetRole: "military_advisor"`, `deckCopies: 3`, `cost: [{ "resourceId": "gold", "amount": 50.0 }]`, `conditions: [gte(control,20), gte(opinion,25), gte(warFree,1)]`, `effectIds: ["revenge_declare_war_effect"]`.

- [x] **`Assets/Configs/effect_config.json`: new `revenge_declare_war_effect`** — `effectType: "DeclareRevengeWar"`, `damageBonusPercent: 10.0`, `durabilityBonusPercent: 5.0`, plus `nameKey`/`descKey`.

- [x] **`Assets/Configs/ActionVisualConfig.asset`: new `revenge` entry** — direct YAML edit, reuse `decrease_enemy_control`'s `frontImage` guid/fileID as placeholder art (same domain, adversarial country card); `backImage: {fileID: 0}`.

- [x] **Localization (en + real ru via the `localization` skill)**:
  - `action.revenge.name` → `"Revenge"`; `action.revenge.desc` → short practical description.
  - `effect.revenge_declare_war.name` / `.desc`.
  - `action.country.unplayable.at_war` → `"Cannot be played while at war"`.
  - Update existing `action.country.unplayable.insufficient_opinion` value to add the role placeholder: `"Requires {0} opinion with the {1}"` (en + ru, both existing key values updated, not new keys).
  - Russian translations for all of the above via the `localization` skill (batch one subagent call).

- [x] **Rebuild Core DLLs** — `dotnet build src/GlobalStrategy.Core.sln -c Release` (per the `dotnet-build` skill) so `Assets/Plugins/Core/*.dll` picks up all `src/` changes.

## User Steps

### 1. Confirm a clean Unity import

After the DLL rebuild, let Unity finish domain reload and check the console for errors — expected: updated `Assets/Plugins/Core/*.dll`, new `action_config`/`effect_config`/`game_settings`/`ActionVisualConfig` entries, new/changed locale entries. No scene or prefab edits in this feature.

### 2. Play-mode smoke test (optional)

Enter Play mode with an org whose HQ and a target country both have Control ≥ 20 and Opinion ≥ 25 at the target's military advisor; confirm "Revenge" is drawn/playable, play it, and verify: a war now exists between HQ and the target (`Wars.IsInWar`/save inspection), the HQ's `damage`/`durability` immediately reflect the +10%/+5% bonus (same tick, no waiting for a day boundary), and a second "Revenge" copy (if drawn) shows unplayable while the war is ongoing. Advance a month and confirm the bonus percentages decay by 1.0/0.5.

## Tests

- **`src/Game.Tests/ExpressionNodeTests.cs`**: `"warFree"` round-trips `ExpressionContext.WarFree`.

- **`src/Game.Tests/WarsTests.cs`**: extend with
  - `declare_war_out_overload_returns_generated_war_id_on_success` / `..._returns_null_warid_on_no_op`.
  - `is_war_free_true_when_neither_side_at_war`, `is_war_free_false_when_country_at_war`, `is_war_free_false_when_hq_country_at_war`, `is_war_free_true_when_hq_country_by_org_id_missing_entry` (dict-based overload).

- **New `src/Game.Tests/RevengeWarBonusQueryTests.cs`**: `GetBonusPercent` returns `0` when no `RevengeWarBonus` entity exists; returns the matching country's `DamageBonusPercent`/`DurabilityBonusPercent`; unaffected by other countries' bonuses.

- **New `src/Game.Tests/RevengeWarBonusDecaySystemTests.cs`** (mirror `WarSystemTests.cs`'s boundary style): month boundary decays both percentages by their configured amounts; floor-clamps at `0` and stays there on a later boundary; no boundary ⇒ no change; two `RevengeWarBonus` entities decay independently.

- **`src/Game.Tests/DamageCollectorTests.cs` / `DurabilityCollectorTests.cs`**: extend with `compute_applies_revenge_bonus_percent_multiplicatively` (seed a `RevengeWarBonus` for the country, assert `target = (base+skills) * (1 + percent/100)`) and `compute_bonus_is_zero_when_no_revenge_bonus_component` (byte-identical to pre-feature behavior).

- **New `src/Game.Tests/RevengeDeclareWarEffectTests.cs`**: `CreateActionEffectSystem.Update` with a `DeclareRevengeWarEffectParams` effect — declares the war (hq=attacker, country=defender), attaches `RevengeWarBonus` with the configured seed percentages, returns `true`; returns `false`/no war/no bonus when `hqCountryByOrgId` is `null` or missing the org; no-ops when `Wars.DeclareWar` itself would no-op (either side already at war); declaring a second Revenge war for an HQ that still has a non-zero leftover `RevengeWarBonus` from a prior, already-ended war replaces it rather than leaving two entities.

- **`src/Game.Tests/ActionPlayabilityTests.cs`**: add a `revenge` fixture (`TargetRole = "military_advisor"`, conditions `gte(control,20)`, `gte(opinion,25)`, `gte(warFree,1)`, cost 50 gold). Cases:
  - unplayable when control `< 20`.
  - unplayable when opinion `< 25` at `military_advisor` even with high `diplomacy_advisor` opinion seeded (proves role generalization, not just role removal).
  - unplayable when the country or the org's HQ (via `hqCountryByOrgId`) is at war.
  - playable when all three hold and affordable.
  - existing `make_friend`/`stop_friendship` (`diplomacy_advisor`) cases still pass unchanged (regression for the generalization).

- **`src/Game.Tests/DrawCardSystemTests.cs`**: `revenge` eligibility mirrors `decrease_enemy_control`'s style (not drawn when any of the 3 conditions fail; drawn when all hold). Add one case seeding both a `make_friend`-shaped and a `revenge`-shaped candidate in the same draw call with different diplomacy/military opinions, asserting each resolves against its own `TargetRole` correctly (regression for the per-candidate opinion restructuring).

- **`src/Game.Tests/InitSystemTests.cs`**: initial hand fill seeds `revenge` correctly using `military_advisor` opinion + `warFree`; a mixed-role regression case (one `diplomacy_advisor` card, one `military_advisor` card) fills correctly in the same run.

- **`src/Game.Tests/VisualStateConverterCountryActionsOpinionGateTests.cs`** (or a small new sibling file): `BuildEntry` yields unplayable reason `"at_war"` when `warFree` fails; confirms condition-array order (control checked before opinion before warFree) matches config order.

- **New `src/Game.Tests/RevengeCardGameLogicTests.cs`** (mirrors `WarsTests.cs`'s `debug_commands_declare_and_stop_war_through_game_logic` / `DamageDurabilityGameLogicTests.cs` fixture style): push a `PlayCardActionCommand` for a hand-seeded `revenge` card through `GameLogic.Update` and assert, same tick: `Wars.IsInWar` true for both HQ and target, `RevengeWarBonus` attached to HQ with seeded percentages, and `ResourceQuery.GetValue` for HQ's `damage`/`durability` already reflects the multiplier (proves the `SettleCombatResources()` same-tick wiring, not just eventual-consistency).

- **Not automatable — `CountryActionsView.cs`'s new `"at_war"` case and role-aware `insufficient_opinion` text**: Unity-side `Assets/Scripts/Unity/UI/` class, no existing test harness reaches it (same pre-existing gap called out in the `decrease_enemy_control` plan for `GameLogLineFormatter`/`CardPlayBarriersHolder`). Verify via the Play-mode smoke test above or `/code-review` inspection of the diff.

- Run via the `dotnet-test` skill against `src/GlobalStrategy.Core.sln`.

## Constitution Check

No conflicts found — plan aligns with all principles:
- **ECS for all game logic** — all new state (`RevengeWarBonus`) and behavior (`RevengeWarBonusQuery`, `RevengeWarBonusDecaySystem`, `Wars.IsWarFree`/`DeclareWar` overload, `CreateActionEffectSystem`/`DamageCollector`/`DurabilityCollector` changes) lives in `src/`; no MonoBehaviour game state.
- **VContainer is the sole DI mechanism** — no new container registrations; collectors keep resolving the bonus live via the existing `world` parameter, same as every other collector.
- **UI Toolkit only** — the only UI change is two `CountryActionsView.cs` text/case additions inside the existing UI Toolkit view; no Canvas/UGUI.
- **URP only** — no rendering change.
- **Plan before implement / Spec before plan** — `spec.md` already exists and is approved for planning; this plan is the implement gate.
- **File organisation** — this plan lives at `Docs/Specs/26_07_29_20_revenge-card/plan.md`, alongside its spec.
- **One asmdef per feature folder** — no new `Assets/Scripts/` feature folder; all new types are `src/` assemblies or a single `Assets/Scripts/Unity/UI/CountryActionsView.cs` edit within its existing assembly.
- **C# code style** — tabs, brace-on-same-line, `_`-prefixed private members, no redundant access modifiers, matching `Wars.cs`/`WarSystem.cs`/`DamageCollector.cs` precedent throughout all new/changed files.

## Out of Scope / Follow-ups

- **Discard-on-win** — deferred per spec; no war-outcome/victory-determination signal exists yet (issues #74, #80, #82 open). Revisit once one lands.
- **Bot/AI strategy for this card** — `BotObservation.Build`'s `hqCountryByOrgId` stays `null` (see Approach); bots see a partial `warFree` gate (target-country war state only, not HQ). Acceptable per spec's Out of Scope; thread `hqCountryByOrgId` into `Bot.cs` in a future bot-feature spec if needed.
- **Target-selection UI** — target is always the currently-selected country, like every other country card.
- **Any roll/chance for this card** — always succeeds once played, per spec.
- **Card artwork** — placeholder art only (reused `decrease_enemy_control` sprite).
- **Any change to `Wars.cs`/`WarSystem.cs` core declare/stop/decay semantics** — only the attacker-only bonus is added; war lifecycle itself is untouched.
- **Rebalancing other existing country cards** as a side effect of the Opinion-role generalization.

Use the implement skill to start working on the plan or request changes.
