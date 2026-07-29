# Plan: Decrease Enemy Control Card

## Spec

As a player, playing "Decrease Enemy Control" on a selected country weakens whichever rival organization currently holds the most Control there (by 20, clamped at 0) while simultaneously growing the playing org's own Control in that same country (by 10, capped by the shared 100-point country pool). See `spec.md` for full detail.

Key acceptance criteria:
- Eligible to draw/play only while at least one other org holds Control > 0 in the selected country; the deck/draw system and playability checks re-evaluate this the normal way (next slot fill / next UI refresh), never proactively.
- Playing always succeeds, no roll. Exactly one other org is affected per play — the one with the currently-highest Control total in the country, ties broken deterministically. Every other qualifying org is untouched.
- The card never shows a cooldown label (project-wide cooldown mechanic is already fully removed).
- Two Game Log entries appear per play: the targeted org's Control decreasing by 20 (or less if it held less), and the playing org's own Control increasing by 10 (or less if pool-capped).

## Approach

**Most of the shared plumbing this spec's Tech Notes describe as "new groundwork" already exists in the current tree** — this branch is built on top of the already-merged `26_07_24_13_stop-friendship-rivalry-cards` feature, which built exactly the `hasSuitableRelationTarget`/`relationStillExists`-shaped condition wiring this card's `hasEnemyControl` needs to mirror. Verified against actual current source:
- `ExpressionContext` (`src/Game.Configs/ExpressionNode.cs:5-10`) already has `Control`, `Opinion`, `HasSuitableRelationTarget`, `RelationStillExists` — this plan adds a fifth field, `HasEnemyControl`, following the exact same shape.
- `ExpressionNode.Evaluate`'s switch (lines 52-63) already has `"control"`/`"opinion"`/`"hasSuitableRelationTarget"`/`"relationStillExists"` cases — this plan adds one more, `"hasEnemyControl"`.
- `ActionPlayability.Evaluate` already has the exact signature `Evaluate(IReadOnlyWorld world, ActionConfig config, int entity, string actionId, string orgId, string? countryId)` (`src/Game.Systems/ActionPlayability.cs:8`) — no signature change needed, this card's conditions never read anything per-entity, matching the spec's own "Architecture decision" note.
- `CharacterQuery.cs` already exists as a plain non-system helper (`src/Game.Systems/CharacterQuery.cs`) — confirms the "no system-to-system call" pattern this plan's new `ControlQuery.cs` follows.
- `ClearCountryRelationEffectParams`/`SetCountryRelationEffectParams` already exist as siblings in `EffectConfig.cs`'s `ActionEffectDefinitionListConverter` switch (5 existing cases: `DiscoverCountry`, `ControlChange`, `OpinionModifier`, `SetCountryRelation`, `ClearCountryRelation`) — this plan's new `EnemyControlDrain` case is the 6th, following the identical registration pattern.

**All concrete file/line citations in the spec's Tech Notes were individually re-verified against current source and found accurate** (occasionally off by one line, never materially wrong):
- `DrawCardSystem.DrawCountryCards`'s `ctx` construction: exactly line 96.
- `ActionPlayability.Evaluate`'s `ctx` construction block: exactly lines 14-26.
- `VisualStateConverter.BuildEntry`'s `ctx` construction: exactly lines 615-623; its `failedReason` switch is lines 631-636 (spec cited 630-636 — one-line drift, immaterial).
- `CountryActionsView.cs`'s unplayable-reason switch: exactly lines 74-84.
- `CreateActionEffectSystem.Update`'s `ControlChangeEffectParams` branch: exactly lines 42-71; its `GetOrgControlInCountry` helper: exactly lines 128-139; the `SetCountryRelationEffectParams`/`ClearCountryRelationEffectParams` branches sit at exactly lines 103/106.
- `GameLogic.cs`'s `ReduceOrgControlInCountry`: exactly lines 712-740; `GetOtherOrgsControlDescending`: exactly lines 807-825.
- `GameLogEffects.cs`'s `ControlEffectApplied`: struct body at lines 4-9 (spec cited 3-9, off by the namespace-open line — immaterial).
- `GameLogLineFormatter.BuildControlLine`: exactly lines 16-22.
- `VisualStateConverter.UpdateGameLog`: starts at exactly line 798, `ControlEffectApplied` handling at lines 802-804 confirming the cited 798-811 range.

**One gap the spec's Tech Notes correctly flagged and this plan must not skip:** `GameLogLineFormatter.cs` lives under `Assets/Scripts/Unity/UI/` (Unity-side, not `src/`) and has **no existing xUnit test coverage anywhere in the repo** (confirmed — no `GameLogLineFormatterTests.cs` exists, and no other Unity-UI-script test file exists as a precedent either). The `dotnet test` suite cannot exercise it. This plan implements the `Delta >= 0` branch fix as specified but calls out in Tests below that it is verified by manual/code-review inspection only, consistent with the rest of this file's untested status — not a gap introduced by this feature.

**Two gaps found during `/plan-review` that the spec's Tech Notes did not call out, both verified against real source, both required — not optional polish:**

1. **The drain half must also emit a `ResourceChange`, not just a `ControlEffectApplied`.** `CreateActionEffectSystem`'s existing `ControlChangeEffectParams` branch (`src/Game.Systems/CreateActionEffectSystem.cs:53-59`) pairs every Control mutation with a `ResourceChange { ResourceId = "control_{countryId}", ... }` — this is what `CardPlayAnimator.HandleLastFrameEffectsChanged` (`Assets/Scripts/Unity/UI/CardPlayAnimator.cs:95-99`) reads to hold an animation barrier on `SelectedCountry.Control.UsedControl` (the country's shared-pool gauge). The spec's Tech Notes only described the `ControlEffectApplied` (Game Log) side of the drain and omitted this. Without it, the gauge only ever sees the +10 gain and never the -20 drain, permanently misrepresenting the true net pool change for the duration of the animation. The drain branch below (Step 6) now creates both a `ControlEffectApplied` and a `ResourceChange` with a negative `Amount`, mirroring the gain branch's shape exactly.
2. **`CardPlayBarriersHolder` must be extended to hold multiple simultaneous barriers under one key before this fix is safe to ship.** Verified: `AnimatableInt`/`AnimationBarrierInt` (`src/Game.Main/AnimatableInt.cs`, `AnimationBarrierInt.cs`) already support multiple concurrent barriers correctly — `AnimatableInt` keeps a `List<AnimationBarrierInt>` and sums every barrier's `Offset` into `Display`. The bug is entirely in `CardPlayBarriersHolder` (`Assets/Scripts/Unity/UI/CardPlayBarriersHolder.cs:17-27`): `_ints`/`_doubles` are `Dictionary<string, EntryInt/EntryDouble>`, one entry per key, and `AddInt`/`AddDouble` **overwrite** the existing entry rather than appending. Once this card exists, `HandleLastFrameEffectsChanged`'s per-effect loop calls `_barrierHolder.AddInt("control", ...)` twice in the same play (once for the gain's `ResourceChange`, once for the new drain's) — the second call silently discards the reference to the first barrier, which is never `Release()`d or `Cancel()`ed and stays held on `SelectedCountry.Control.UsedControl` forever, permanently offsetting the gauge's `Display` value for the rest of the play session. This is the first card ever to produce two `control_`-prefixed `ResourceChange` events in one play, so the bug has no prior trigger. Step 7 below fixes `CardPlayBarriersHolder` to store a list per key (append, not overwrite) and release/cancel every entry in that list together.

Everything else in the spec's Tech Notes — `ControlQuery.cs` (new file, doesn't exist yet), `EnemyControlDrainEffectParams` (new type, doesn't exist yet), the `decrease_enemy_control` action/effect config rows, and the Game Log negative-`Delta` line — is genuinely new work with no existing implementation, and is implemented as described in `spec.md`'s Tech Notes below.

## Steps

### Agent Steps

- [ ] **Add `HasEnemyControl` to `ExpressionContext` + DSL node** — In `src/Game.Configs/ExpressionNode.cs`: add `public double HasEnemyControl { get; set; }` to `ExpressionContext` (alongside `Control`/`Opinion`/`HasSuitableRelationTarget`/`RelationStillExists`, line 9), and add `case "hasEnemyControl": { return ctx.HasEnemyControl; }` to `ExpressionNode.Evaluate`'s switch (alongside the existing `"relationStillExists"` case, after line 63).

- [ ] **New `src/Game.Systems/ControlQuery.cs`** — Plain non-system helper, same file-per-type pattern as `CharacterQuery.cs`/`ResourceQuery.cs`:
  - `public static bool HasOtherOrgControl(IReadOnlyWorld world, string orgId, string countryId)` — scans `ControlEffect` entities (`TypeId<ControlEffect>.Value`), sums `Value` grouped by `OrgId` for every entity where `CountryId == countryId && OrgId != orgId`, returns `true` if any other org's total is `> 0`.
  - `public static string? GetHighestControlOtherOrg(IReadOnlyWorld world, string orgId, string countryId)` — same grouping (`CountryId == countryId && OrgId != orgId`), returns the `OrgId` with the highest positive total, ties broken by `string.CompareOrdinal(OrgId)` ascending (mirrors `GameLogic.cs`'s private `GetOtherOrgsControlDescending`, lines 807-825), or `null` if no other org holds any Control.
  - `public static void ReduceOrgControlInCountry(World world, string orgId, string countryId, int amount)` — collects every `ControlEffect` entity for `(orgId, countryId)` into a list first (avoid mutating while `GetMatchingArchetypes` enumerates, same reasoning as `GameLogic.cs`'s private version), sorts by `EffectId` via `string.CompareOrdinal` for determinism, then reduces/destroys entries front-to-back until `amount` is consumed — this is a near-identical reimplementation of `GameLogic.cs`'s private `ReduceOrgControlInCountry` (lines 712-740); `GameLogic`'s version is private and lives in `Game.Main`, unreachable from `Game.Systems`, so this feature adds its own copy (consolidating the two is out of scope, per spec).

- [ ] **Wire `HasEnemyControl` into the three condition evaluators** — unconditionally, once per country-card evaluation, same style as the existing `Control` field (not per-entity like `RelationStillExists`):
  - `DrawCardSystem.DrawCountryCards` (`src/Game.Systems/DrawCardSystem.cs`, in the `ctx` construction at line 96): add `HasEnemyControl = ControlQuery.HasOtherOrgControl(world, orgId, countryId) ? 1.0 : 0.0` to the `ExpressionContext` initializer.
  - `ActionPlayability.Evaluate` (`src/Game.Systems/ActionPlayability.cs`, inside the existing `if (!string.IsNullOrEmpty(countryId))` block, lines 16-20): compute `hasEnemyControl` the same way, add it to the `ctx` initializer at line 26.
  - `VisualStateConverter.BuildEntry` (`src/Game.Main/VisualStateConverter.cs`, alongside the existing `hasSuitableTarget`/`relationStillExists` locals, lines 615-623): compute and add to the `ctx` initializer at line 623.

- [ ] **Unplayable-reason plumbing** — following the existing per-condition-field switch exactly:
  - `VisualStateConverter.BuildEntry`'s `failedReason` switch (`src/Game.Main/VisualStateConverter.cs:631-636`): add `"hasEnemyControl" => "no_enemy_control",` as a new case (alongside `"opinion"`/`"hasSuitableRelationTarget"`/`"relationStillExists"`).
  - `CountryActionsView.cs`'s reason-text switch (`Assets/Scripts/Unity/UI/CountryActionsView.cs:74-84`): add `"no_enemy_control" => _loc.Get("action.country.unplayable.no_enemy_control"),` as a new case (alongside `"no_suitable_target"`/`"relation_no_longer_exists"`).

- [ ] **New effect type: `EnemyControlDrainEffectParams`** — In `src/Game.Configs/EffectConfig.cs`: add `public class EnemyControlDrainEffectParams : ActionEffectDefinition { public int Amount { get; set; } }` (alongside `ClearCountryRelationEffectParams`), and register `case "EnemyControlDrain": item = obj.ToObject<EnemyControlDrainEffectParams>(serializer)!; break;` in `ActionEffectDefinitionListConverter`'s switch (alongside the existing five cases, after the `"ClearCountryRelation"` case).

- [ ] **Dispatch the drain effect in `CreateActionEffectSystem`** — In `src/Game.Systems/CreateActionEffectSystem.cs`'s `foreach (var effectId in def.EffectIds)` loop (after the `ClearCountryRelationEffectParams` branch at line 106): add
  ```
  else if (effectDef is EnemyControlDrainEffectParams drainParams && drainParams.Amount > 0 && !string.IsNullOrEmpty(countryId)) {
      string? targetOrgId = ControlQuery.GetHighestControlOtherOrg(world, orgId, countryId);
      if (targetOrgId != null) {
          int targetControlBefore = GetOrgControlInCountry(world, targetOrgId, countryId);
          int actualDrain = Math.Min(drainParams.Amount, targetControlBefore);
          ControlQuery.ReduceOrgControlInCountry(world, targetOrgId, countryId, actualDrain);
          if (actualDrain > 0) {
              int rc = world.Create();
              world.Add(rc, new ResourceChange {
                  EffectId = $"control_{targetOrgId}_{countryId}_{currentTime.Ticks}",
                  ResourceId = $"control_{countryId}",
                  OwnerId = targetOrgId,
                  Amount = -actualDrain
              });
              int ge = world.Create();
              world.Add(ge, new ControlEffectApplied {
                  OrgId = targetOrgId,
                  CountryId = countryId,
                  Delta = -actualDrain,
                  Total = targetControlBefore - actualDrain
              });
          }
      }
  }
  ```
  Reuses the file's own existing private `GetOrgControlInCountry(world, orgId, countryId)` helper (lines 128-139) as-is — no signature change needed. No new marker component, no new system, no `GameLogic.cs` wiring: this resolves synchronously inline, exactly like the existing `ControlChangeEffectParams`/`OpinionModifierEffectParams` branches, because (unlike `SetCountryRelationEffectParams`/`ClearCountryRelationEffectParams`) it needs no `Random`/`ProximityMapData` unavailable inside this system.
  The new `ResourceChange` mirrors the existing gain branch's shape exactly (`ControlChangeEffectParams`'s own `ResourceChange` at lines 53-59), except `Amount` is negative and `OwnerId` is the *target* org, not the playing org — this is what feeds the country's `UsedControl` pool-gauge animation barrier on the drain side (see Approach and Step 7 below; without both this and Step 7, the gauge would silently misrepresent the drain).

- [ ] **Fix `CardPlayBarriersHolder` to support multiple simultaneous barriers per key** — In `Assets/Scripts/Unity/UI/CardPlayBarriersHolder.cs`: change `_doubles`/`_ints` from `Dictionary<string, EntryDouble/EntryInt>` to `Dictionary<string, List<EntryDouble>>`/`Dictionary<string, List<EntryInt>>`. `AddDouble`/`AddInt` append a new entry to the key's list (creating the list if absent) instead of overwriting. `Has` checks for a non-empty list. `Animate` releases every entry in the key's list and awaits all of them (`UniTask.WhenAll`) instead of just one. `CancelAll` iterates every entry in every list. This is required because this feature is the first to ever call `AddInt("control", ...)` twice in one play (once for the gain's `ResourceChange`, once for the drain's, added in the previous step) — without this fix the second `AddInt` call silently discards the first barrier, which is then never released, permanently offsetting `SelectedCountry.Control.UsedControl`'s `Display` value. `AnimatableInt`/`AnimationBarrierInt` (`src/Game.Main/`) already support multiple concurrent barriers correctly (summed into `Display`) — no change needed there, this fix is entirely local to `CardPlayBarriersHolder`.

- [ ] **Fix `GameLogLineFormatter.BuildControlLine` for negative `Delta`** — In `Assets/Scripts/Unity/UI/GameLogLineFormatter.cs` (lines 16-22): replace the hardcoded `string deltaText = "+" + FormatNumber(entry.Delta);` / fixed `game_log.control_increased_format` lookup with a branch on `entry.Delta >= 0`:
  ```csharp
  public static string BuildControlLine(GameLogEntry entry, ILocalization loc, CountryVisualConfig countryVisualConfig, OrgVisualConfig orgVisualConfig) {
      string orgName = WrapColored(loc.Get($"organization_name.{entry.OrgId}"), orgVisualConfig.Find(entry.OrgId)?.color);
      string countryName = WrapColored(loc.Get($"country_name.{entry.CountryId}"), countryVisualConfig.Find(entry.CountryId)?.color);
      string totalText = FormatNumber(entry.Total);
      if (entry.Delta >= 0) {
          string deltaText = "+" + FormatNumber(entry.Delta);
          return string.Format(loc.Get("game_log.control_increased_format"), orgName, countryName, deltaText, totalText);
      }
      string decreaseText = "-" + FormatNumber(System.Math.Abs(entry.Delta));
      return string.Format(loc.Get("game_log.control_decreased_format"), orgName, countryName, decreaseText, totalText);
  }
  ```
  The increase branch keeps byte-identical wording/format-key to today. `VisualStateConverter.UpdateGameLog` (`src/Game.Main/VisualStateConverter.cs:798`) and `CleanupEffectNotificationsSystem` need no change — both already forward/sweep `ControlEffectApplied` regardless of sign (confirmed: `CleanupEffectNotificationsSystem` already sweeps `ControlEffectApplied` generically).

- [ ] **`Assets/Configs/action_config.json`: new action row** — append, following the existing `sphere_of_pressure`-shape entries:
  ```json
  {
    "actionId": "decrease_enemy_control",
    "ownerType": "country",
    "rarity": "Standard",
    "nameKey": "action.decrease_enemy_control.name",
    "descKey": "action.decrease_enemy_control.desc",
    "targetRole": "",
    "deckCopies": 3,
    "conditions": [
      {
        "type": "gte",
        "members": [
          { "type": "hasEnemyControl" },
          { "type": "value", "value": 1 }
        ]
      }
    ],
    "cost": [{ "resourceId": "gold", "amount": 250.0 }],
    "effectIds": ["decrease_enemy_control_drain_effect", "decrease_enemy_control_gain_effect"]
  }
  ```
  `effectIds` order is load-bearing — the drain must run first so the reused `ControlChangeEffectParams` branch's `usedTotal < 100` pool-cap check sees the post-drain total.

- [ ] **`Assets/Configs/effect_config.json`: two new effect rows** — append, following the existing `sphere_of_pressure_control`/`clear_friendship_effect`-shape entries:
  ```json
  {
    "effectId": "decrease_enemy_control_drain_effect",
    "effectType": "EnemyControlDrain",
    "nameKey": "effect.decrease_enemy_control_drain.name",
    "descKey": "effect.decrease_enemy_control_drain.desc",
    "amount": 20
  },
  {
    "effectId": "decrease_enemy_control_gain_effect",
    "effectType": "ControlChange",
    "nameKey": "effect.decrease_enemy_control_gain.name",
    "descKey": "effect.decrease_enemy_control_gain.desc",
    "amount": 10
  }
  ```
  The gain effect needs no new C# — reuses the existing, unmodified `ControlChangeEffectParams`/`"ControlChange"` branch.

- [ ] **New `ActionVisualConfig` entry** — `Assets/Configs/ActionVisualConfig.asset` is a plain YAML `ScriptableObject` asset, directly editable (confirmed: `stop_friendship`/`stop_rivalry` already have hand-added entries in this exact file reusing another action's `guid`+`fileID` sprite reference as placeholder art — e.g. `stop_friendship` reuses `letter_of_commendation_diplomacy_advisor`'s `frontImage: {fileID: -6234567890123456789, guid: b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7, type: 3}`). Append a new entry following the same list-item format:
  ```yaml
  - actionId: decrease_enemy_control
    frontImage: {fileID: -7234567890123456789, guid: a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6, type: 3}
    backImage: {fileID: 0}
  ```
  (reusing `sphere_of_pressure`'s `frontImage` guid/fileID as placeholder — same domain, a Control-granting country card — real card art generation is out of scope per spec). This is a direct file edit, not a Unity Inspector step — see User Steps below.

- [ ] **Locale keys** — Add to both `Assets/Localization/en.asset` and `Assets/Localization/ru.asset`, matching the existing `- Key: ... / Value: ...` format (use the `localization` skill for the Russian translations, per `.claude/rules/unity/localization.md`):
  - `action.decrease_enemy_control.name` → `"Undermine Influence"` (placeholder, non-binding per spec).
  - `action.decrease_enemy_control.desc` → `"Weaken a rival's control and strengthen your own."` (short practical form per the Card/Action Description Text rule).
  - `effect.decrease_enemy_control_drain.name` / `.desc` — short name/desc for the drain half.
  - `effect.decrease_enemy_control_gain.name` / `.desc` — short name/desc for the gain half.
  - `action.country.unplayable.no_enemy_control` → `"No organization with control available to target"`.
  - `game_log.control_decreased_format` → `"{0} decreased control in {1} by {2} ({3})"`.
  - Russian equivalents (real translations, not English placeholders) for all six.

### User Steps

### 1. None

None — every change in this plan is a code, config, or asset-YAML file edit (including the `ActionVisualConfig.asset` placeholder-art entry, which is a plain YAML edit, not an Inspector operation). No Unity Editor scene/asset work, visual inspection, or other hands-on Unity step is required. Optional, non-blocking: confirm in the Editor that the reused `sphere_of_pressure` placeholder art renders correctly on the new card once implemented.

## Tests

- **`src/Game.Tests/ExpressionNodeTests.cs`**: extend the `Ctx(...)` helper with a `hasEnemyControl` parameter (defaulting to `0`), and add a `has_enemy_control_node_returns_context_value` case mirroring `has_suitable_relation_target_node_returns_context_value` (asserts `1.0`/`0.0` round-trip through `ExpressionNode.Evaluate`).

- **`src/Game.Tests/ActionPlayabilityTests.cs`**: add a `"decrease_enemy_control"` entry to `BuildActionConfig()` (condition: `gte(hasEnemyControl, 1)`, cost 250 gold), following the existing `country_card`/`make_friend` entries' shape. Add cases:
  - `decrease_enemy_control_unplayable_when_no_other_org_holds_control` — no `ControlEffect` for any other org in the country → `Evaluate` returns `false`.
  - `decrease_enemy_control_playable_when_another_org_holds_control_and_affordable` — seed `AddControl(world, "OrgB", "Prussia", 10)` plus gold for `OrgA` → `Evaluate` returns `true`.
  - `decrease_enemy_control_unplayable_when_unaffordable_even_with_enemy_control_present` — same `ControlEffect` seed, insufficient gold → `false`.
  - Since this card carries no per-entity data, no `-1`-entity regression case is needed beyond the existing `entity_negative_one_with_relation_agnostic_conditions_does_not_throw` coverage (already exercises the `entity == -1` path generically).

- **`src/Game.Tests/DrawCardSystemTests.cs`**: add cases mirroring its existing `stop_friendship` deck-eligibility tests:
  - a candidate `decrease_enemy_control` deck entity is **not** drawn into hand when no other org holds Control in the country.
  - it **is** drawn once another org's `ControlEffect` in that country is `> 0`.

- **New `src/Game.Tests/ControlQueryTests.cs`** (no existing precedent — new query type, follow `CharacterQuery`'s absence of a dedicated test file as informing that this can be a small, focused new file):
  - `HasOtherOrgControl` returns `false` with zero other-org `ControlEffect` entities in the country, `true` once one exists with `Value > 0`, and ignores the querying org's own `ControlEffect` entities.
  - `GetHighestControlOtherOrg` returns `null` when no other org holds Control; returns the single other org when exactly one qualifies; returns the org with the strictly higher total when two qualify; returns the ordinally-lower `OrgId` when two other orgs are tied (mirroring `GameLogic.cs`'s existing `GetOtherOrgsControlDescending` tie-break convention — no direct test of that private method exists today, so this is the first explicit tie-break assertion for this domain).
  - `ReduceOrgControlInCountry` reduces a single `ControlEffect` entity's `Value` without destroying it when `amount < Value`; destroys it and consumes only what's needed when `amount >= Value` and a second entity exists to continue draining; clamps at zero total drain when `amount` exceeds the org's total Control in the country (no negative `Value`, no over-drain into other countries/orgs).

- **`src/Game.Tests/GameLogStateTests.cs`** (extend, following the existing `raise_control`-style `ControlActionConfig`/`ControlEffectConfig` fixture pattern at lines 175-208): add a `decrease_enemy_control`-shaped `ActionConfig`/`EffectConfig` pair (two effect ids: `EnemyControlDrainEffectParams { Amount = 20 }` then `ControlChangeEffectParams { Amount = 10 }`) and assert, after playing it against a country where a second org holds Control:
  - Two `GameLogEntryKind.Control` entries appear for the single play: one with negative `Delta` (the drain, `-20` or less if the target held less) and one with positive `Delta` (the gain, `+10` or less if pool-capped), both carrying the post-mutation `Total` for their respective org.
  - Clamp behavior: when the target org holds less than 20 Control, the drain `Delta` equals `-<their total>`, and their resulting Control is exactly `0`, never negative.
  - Tie-break/single-target behavior: when two other orgs each hold Control > 0, only the higher-Control (or ordinally-lower-`OrgId` on a tie) org's Control changes; the other org's Control and Game Log entries are untouched by the play.
  - Pool-cap interaction: when the country's total Control is already near the 100-point cap, the gain half is capped by `100 - postDrainTotal`, confirming the drain-then-gain `effectIds` ordering is load-bearing (a test that swaps the config's `effectIds` order and asserts the gain is short-changed would demonstrate this, if useful as a regression guard).

- **Not automatable — `GameLogLineFormatter.BuildControlLine`'s negative-`Delta` branch**: this file has no existing xUnit coverage anywhere in the repo (Unity-side `Assets/Scripts/Unity/UI/` class, no test harness reaches it). Verify manually in the Editor (play the card, check the Action Log line reads `"... decreased control in ... by 20 (...)"` with no literal `"+-20"`) or via `/code-review` inspection of the diff — do not attempt to add a `Game.Tests` entry for it, consistent with this file's pre-existing untested status.

- **Not automatable — `CardPlayBarriersHolder`'s multi-entry-per-key fix**: also a Unity-side `Assets/Scripts/Unity/UI/` class with no existing test coverage. Verify manually in the Editor: play the card against a country where drain and gain are both non-zero and confirm the `UsedControl` gauge animates to the correct net total (not stuck offset) and that a second card play afterward still animates correctly (confirms no orphaned barrier survived from the first play). `src/Game.Main/AnimatableInt.cs`/`AnimationBarrierInt.cs` (the classes the fix relies on already supporting multiple barriers) *are* reachable from `src/Game.Tests` if a regression test is wanted — e.g. a new case asserting `AnimatableInt.Hold` called twice produces a `Display` that sums both offsets — but `CardPlayBarriersHolder` itself stays Editor-only-verified, consistent with `GameLogLineFormatter.cs`'s status above.

## Constitution Check

No conflicts found — plan aligns with all principles: game logic (`ControlQuery`, the new effect type, dispatch) lives entirely in `src/` as ECS-pattern static helpers/systems with no MonoBehaviour game-state; no system-to-system calls (`ControlQuery.cs` is a plain non-system helper, same shape as `CharacterQuery.cs`); no VContainer or UI Toolkit changes needed beyond the existing `CountryActionsView`/`ActionVisualConfig` wiring pattern already established by prior country cards; plan is stored under `Docs/Specs/26_07_29_00_decrease-enemy-control-card/` alongside its spec; C# style (tabs, `_`-prefixed private members, always-braces) is followed throughout the new code shown above.

Use the implement skill to start working on the plan or request changes.
