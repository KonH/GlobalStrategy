# Plan: Chance instead of deckCount per cards

## Spec

Source: `Docs/Specs/26_09_01_17_card-deck-chance-display/spec.md` (issue #173).

`ActionDefinition.DeckCopies` (`int`, JSON `deckCopies`, default `3`) is renamed and retyped to `ActionDefinition.Chance` (`double`, JSON `chance`, default `3.0`) — not merely computed for display, but as the actual config field in `Assets/Configs/action_config.json` and its mirror `src/Game.WebClient/wwwroot/configs/action_config.json`. Every production consumer that reads `DeckCopies` as a draw weight or an enable/disable gate (`DrawCardSystem`, `CountryCardDrawQuery`, `RelationCardSyncSystem`, `RevengeCardSyncSystem`, `InitSystem`, `DebugCardAvailabilityView`) moves to `Chance`, preserving the "`<= 0` disables the card" semantics. `DrawCardSystem.DrawOrgCards`'s currently `int`-only weighted-pick candidate/roll widens to `double`, matching the country-card path's existing `double`-weighted `CountryCardDrawCandidate`. The `stop_rivalry` (×0.5) and `declare_war` (×1.7) multipliers hardcoded in `DrawCardSystem.AdjustWeight` move into `action_config.json` as a new optional `ActionDefinition.DrawWeightMultiplier` (JSON `drawWeightMultiplier`, nullable, defaults to `1.0`), applied generically on top of `Chance`. `DebugCardAvailabilityView` drops its `x{count}` row segment in favor of a chance percentage (2-decimal, matching existing `CalculateChancePercent`/`FormatNumber` behavior) for both org decks and country/relation-targeted decks, computed from the same `Chance × DrawWeightMultiplier` value the real draw system uses. This is debug-only/internal — no player-facing UI, no localization changes, no renormalization of the weight semantics (`Chance` stays a relative draw weight, not a 0–1 or 0–100 probability), no change to the weighted-pick selection algorithm itself.

**Acceptance criteria** (full text in spec.md): both `action_config.json` copies rename `deckCopies` → `chance` with unchanged numeric values and stay identical; `ActionDefinition.Chance` (`double`, default `3.0`) replaces `DeckCopies`; every listed production consumer and every `Game.Tests` call site moves to `Chance`; `DrawOrgCards`'s weighted pick widens to `double`; a new optional `DrawWeightMultiplier` field reproduces today's `stop_rivalry`/`declare_war` hardcoded multipliers via `ActionConfig.Find`, with the hardcoded action-id branches removed or reduced to only what config cannot express; `DebugCardAvailabilityView.GetDrawWeight` multiplies `Chance` by the configured multiplier; deck rows drop `x{count}` for a `({percent}%)` suffix, 2-decimal precision, `0%` for zero-weight groups, percentages internally consistent and summing to ~100% across nonzero-weight groups, deterministic across repeated views; no localization key added.

## Goal

Rename the config-level draw-weight field from an integer copy count to a `double` chance, migrate every production and test call site, move the two hardcoded action-id draw-weight multipliers into config, and make the debug card-availability view display a real chance percentage computed from the same weight the live draw system uses — with zero behavioral change to actual game weighting (existing whole-number values carry over unchanged; the roulette-selection algorithm is untouched).

## Approach

### Exhaustive call-site inventory (verified via `grep -rn "DeckCopies"` / `"deckCopies"` across `src/` and `Assets/`)

**Config type:**
- `src/Game.Configs/ActionConfig.cs` — `ActionDefinition.DeckCopies` (`int`, default `3`)

**Config data (must stay identical):**
- `Assets/Configs/action_config.json` — 16 `deckCopies` occurrences (confirmed via `grep -c`)
- `src/Game.WebClient/wwwroot/configs/action_config.json` — 16 occurrences, byte-identical to the above (`diff` confirms; both are 725 lines)

**Production consumers:**
- `src/Game.Systems/DrawCardSystem.cs` — `DrawOrgCards` weighted pick (`definition.DeckCopies`, `int`-typed candidate tuple), `AdjustWeight` (hardcoded `stop_rivalry`/`declare_war` branches)
- `src/Game.Systems/CountryCardDrawQuery.cs` — `GetDrawableCards` (`definition.DeckCopies <= 0` gate, `CountryCardDrawCandidate` weight source)
- `src/Game.Systems/RelationCardSyncSystem.cs` — `EnsureCardInstance` (`def.DeckCopies <= 0` gate, with an explanatory comment to update)
- `src/Game.Systems/RevengeCardSyncSystem.cs` — top-of-`Update` gate (`definition.DeckCopies <= 0`)
- `src/Game.Main/InitSystem.cs` — `CreateCountryActionEntities` (`def.DeckCopies <= 0` skip)
- `Assets/Scripts/Unity/UI/DebugCardAvailabilityView.cs` — `GetDrawWeight` (currently `int`-returning), `RefreshDeck`'s `totalDrawWeight` accumulator, `DeckGroup.DrawWeight`, `DeckActionGroup.TargetDrawWeight`, `CalculateChancePercent`, and the `BuildDeckCard`/`BuildTargetedDeckGroup` title strings that currently embed `x{count}`

**Config-parity test:**
- `src/Game.Tests/StringConfigParityTests.cs` — `sell_arms_action_deserializes_with_required_conditions_and_effects` asserts `action.DeckCopies == 9`; this is the parity test that keeps `FileConfig<ActionConfig>` and `StringConfig<ActionConfig>` loading in sync with the JSON shape — it does not separately enforce Assets/WebClient identity (that's just the fact both paths load the same file content), so no additional identity-check test exists to update beyond this one field-name/value assertion.

**Game.Tests call sites constructing `ActionDefinition { DeckCopies = N }` or asserting `.DeckCopies`** — confirmed via per-file grep, all under `src/Game.Tests/`:
`BotCardAcquisitionTests.cs`, `VisualStateConverterCardDrawTests.cs`, `UnifiedPipelineTests.cs`, `VisualStateConverterCountryActionsVisibilityTests.cs`, `StringConfigParityTests.cs`, `DeclareWarCardTests.cs`, `VisualStateConverterDebugOrgCardAvailabilityTests.cs`, `GameLogStateTests.cs`, `DrawCardSystemTests.cs`, `DiscardCardSystemTests.cs`, `ControlFeatureTests.cs`, `RevengeCardGameLogicTests.cs`, `InitSystemTests.cs`, `CardDrawOfferTests.cs`, `MultiOrgTestSupport.cs` — 79 individual `DeckCopies = N` / `.DeckCopies` occurrences across these 14 files (re-verified via per-file `grep -c`). `GameLogStateTests.cs`'s `ControlActionConfig(int deckCopies)` helper reuses its `deckCopies` int parameter for both `HandSize` (stays `int`) and `ActionDefinition.DeckCopies` (becomes `Chance`, assigned via an implicit `int`→`double` conversion — no cast needed, per the Agent Steps wording below) — the parameter itself does not need renaming for this plan to succeed, just the assignment expression.

**Stale `DeckCopies` mentions in test comments (not just code)** — beyond the field renames above, live prose comments referencing `DeckCopies` also need updating so they don't read as stale after the rename: `CardDrawOfferTests.cs` lines 447, 487 (`// "stop_rivalry" and "make_rival" both have DeckCopies = 1.` / `// "declare_war" and "a" both have DeckCopies = 1.`), `GameLogStateTests.cs` lines 99, 246 (`// DeckCopies = 0 so InitSystem creates...` / `// ...see RelationActionConfig's DeckCopies = 0.`), and `MultiOrgTestSupport.cs` line 129 (`// Relation-synced cards: DeckCopies is draw weight...`). These are exactly what the plan's own final grep-verification step (below) would otherwise flag with no prior instruction on how to resolve them — update each comment's `DeckCopies` reference to `Chance` as part of the same edit that updates the surrounding code.

**Behavioral test dependency (not a pure rename) found in `CardDrawOfferTests.cs`:**
- `stop_rivalry_draws_at_half_weight` and `declare_war_gains_weight_only_for_the_player_org_when_controlling_a_rival_of_the_target` rely on `BuildConfig()`'s `stop_rivalry`/`declare_war` entries getting their ×0.5/×1.7 boost from `DrawCardSystem.AdjustWeight`'s *hardcoded* branches. Once those branches read from config instead, `BuildConfig()` must set `DrawWeightMultiplier = 0.5` on `stop_rivalry` and `DrawWeightMultiplier = 1.7` on `declare_war`, or both tests silently start asserting on a no-op (multiplier defaults to `1.0`) and either false-pass by luck or start failing under the trial-based statistical assertions they use.

### Judgment call: `declare_war` conditional gating (spec Resolved Decision 3)

`AdjustWeight` today has two branches:
```csharp
if (actionId == "stop_rivalry") {
    return candidate.Weight * 0.5;
}
if (actionId == "declare_war"
    && orgId == playerOrgId && !string.IsNullOrEmpty(playerOrgId)
    && world.Has<RelationCardTarget>(candidate.Entity)) {
    string targetCountryId = world.Get<RelationCardTarget>(candidate.Entity).TargetCountryId;
    if (HasControlInRivalOf(world, relations, orgId, targetCountryId)) {
        return candidate.Weight * 1.7;
    }
}
```
`stop_rivalry`'s multiplier is unconditional — it collapses entirely into a generic config lookup, no residual branch needed. `declare_war`'s multiplier is conditional on **runtime world state** (is this the player org, does it control a rival of the target) that no static per-action config value can express. Resolution: keep a **narrower** `actionId == "declare_war"` branch whose only job is the world-state gate (player-org check + `HasControlInRivalOf`) — it no longer hardcodes the `1.7` magnitude itself, that comes from `config.Find(actionId)?.DrawWeightMultiplier ?? 1.0`. Concretely:

```csharp
static double AdjustWeight(
    IReadOnlyWorld world, ActionConfig config, CountryRelations relations,
    string orgId, string playerOrgId, CountryCardDrawCandidate candidate) {
    if (!world.Has<GameAction>(candidate.Entity)) {
        return candidate.Weight;
    }
    string actionId = world.Get<GameAction>(candidate.Entity).ActionId;
    double multiplier = config.Find(actionId)?.DrawWeightMultiplier ?? 1.0;
    if (multiplier == 1.0) {
        return candidate.Weight;
    }
    if (actionId == "declare_war") {
        bool gated = orgId != playerOrgId
            || string.IsNullOrEmpty(playerOrgId)
            || !world.Has<RelationCardTarget>(candidate.Entity)
            || !HasControlInRivalOf(world, relations, orgId, world.Get<RelationCardTarget>(candidate.Entity).TargetCountryId);
        if (gated) {
            return candidate.Weight;
        }
    }
    return candidate.Weight * multiplier;
}
```
This satisfies the acceptance criterion literally: the `actionId == "stop_rivalry"` branch and the `1.7`-hardcoding are both removed; only the world-state gate that config cannot express survives, narrowed to a boolean condition rather than the multiplier value. `HasControlInRivalOf` is untouched. `AdjustWeight` gains an `ActionConfig config` parameter, threaded from `TryCreateOffer` (which already receives `config`).

`DebugCardAvailabilityView.GetDrawWeight` does **not** replicate this gating — per spec Acceptance Criterion 19 it unconditionally multiplies `Chance × (DrawWeightMultiplier ?? 1.0)`, same as today's `GetDrawWeight` unconditionally ignores the multiplier entirely. This is consistent with today's behavior (the debug view has never modeled the gating) and keeps the debug view simple; it is explicitly what the acceptance criteria ask for, not a gap.

### `DrawOrgCards` `int` → `double` widen

Current shape: `List<(int Entity, int Weight)> candidates`, `int totalWeight`, `rng.Next(totalWeight)`. Widen in place to `List<(int Entity, double Weight)>`, `double totalWeight`, and replace `rng.Next(totalWeight)` with `rng.NextDouble() * totalWeight` (mirroring `PickWeightedIndex`'s existing roll pattern for the country-card path) so both paths use the same `double`-uniform roulette shape. Keep `DrawOrgCards`'s own removal-as-you-draw loop structure (it draws multiple cards per call, unlike `PickWeightedIndex`'s single-index helper called repeatedly by `TryCreateOffer`) — do not force it into `PickWeightedIndex`'s exact signature, since the two candidate shapes (`(Entity, Weight)` tuple vs. `CountryCardDrawCandidate` struct) differ and forcing a shared generic helper here is not requested by the spec and risks over-abstracting two call sites with different surrounding loop shapes for no behavior change.

### Debug view weight/percent widen

`GetDrawWeight` returns `double`. `RefreshDeck`'s `totalDrawWeight` accumulator becomes `double`. `DeckGroup.DrawWeight` and `DeckActionGroup.TargetDrawWeight` become `double` (their `Add` methods take `double drawWeight`). `CalculateChancePercent(double drawWeight, double totalDrawWeight)` widens its parameters (return type is already `double`). `BuildDeckCard`/`BuildTargetedDeckGroup` title strings drop the `x{count}` segment:
- `BuildDeckCard`: `$"{ResolveCardName(group.Representative)} x{group.TotalCount} (...)"` → `$"{ResolveCardName(group.Representative)} ({FormatNumber(chancePercent)}%)"`
- `BuildTargetedDeckGroup`: `$"{FormatActionId(actionGroup.ActionId)} x{actionGroup.TargetCount} (...)"` → `$"{FormatActionId(actionGroup.ActionId)} ({FormatNumber(chancePercent)}%)"`

`TotalCount`/`TargetCount` fields stay on `DeckGroup`/`DeckActionGroup` (still used internally to compute `EligibleCount`/`TargetEligibleCount`), just no longer interpolated into the title.

### Build-stays-green ordering

The rename touches a shared config type consumed by many production files and ~14 test files simultaneously — there is no way to keep every intermediate commit compiling while the field is renamed (C# has no "both names valid" transition for a required-shape POCO without adding a throwaway duplicate field, which is not worth the churn for an internal config rename). The plan therefore does the rename as one atomic pass across `ActionConfig.cs` + all production consumers + both JSON files + all test call sites, then verifies with a single full build + test run at the end, rather than claiming false incremental greenness. `/dotnet-build Release` runs once, after all `src/` edits are complete.

## Agent Steps

- [x] **Rename the config field** — `src/Game.Configs/ActionConfig.cs`: `ActionDefinition.DeckCopies` (`int`, default `3`) → `Chance` (`double`, default `3.0`); add new `DrawWeightMultiplier` (`double?`, default `null`, JSON `drawWeightMultiplier`) to the same class.
- [x] **Update both `action_config.json` copies** — `Assets/Configs/action_config.json` and `src/Game.WebClient/wwwroot/configs/action_config.json`: rename every `"deckCopies"` key to `"chance"` keeping the same numeric value (verify existing whole numbers deserialize fine as `double`, e.g. `9` stays `9`); add `"drawWeightMultiplier": 0.5` to the `stop_rivalry` entry and `"drawWeightMultiplier": 1.7` to the `declare_war` entry; re-run the `diff` check after editing to confirm the two files are still byte-identical.
- [x] **`DrawCardSystem.cs`** — widen `DrawOrgCards`'s candidate tuple/roll to `double` per Approach; rewrite `AdjustWeight` to take `ActionConfig config`, do a generic `config.Find(actionId)?.DrawWeightMultiplier ?? 1.0` lookup, drop the `stop_rivalry` branch entirely, narrow the `declare_war` branch to only the world-state gate (see Approach's judgment-call code sketch); update the `TryCreateOffer` call site to pass `config` into `AdjustWeight`; update `DrawOrgCards`'s `definition.DeckCopies` reads to `definition.Chance`; update `AdjustWeight`'s XML doc comment (currently hardcodes "half its normal weight" / "+70% weight" — line ~245-246), rewording it to describe the multiplier as config-driven rather than citing fixed magnitudes that could drift from `action_config.json`.
- [x] **`CountryCardDrawQuery.cs`** — `GetDrawableCards`: `definition.DeckCopies <= 0` → `definition.Chance <= 0`; `new CountryCardDrawCandidate(entity, definition.DeckCopies)` → `definition.Chance`.
- [x] **`RelationCardSyncSystem.cs`** — `EnsureCardInstance`'s `def.DeckCopies <= 0` gate → `def.Chance <= 0`; update the adjacent `// DeckCopies == 0 means...` comment to reference `Chance`.
- [x] **`RevengeCardSyncSystem.cs`** — `definition.DeckCopies <= 0` → `definition.Chance <= 0`.
- [x] **`InitSystem.cs`** — `CreateCountryActionEntities`'s `def.DeckCopies <= 0` → `def.Chance <= 0`.
- [x] **`DebugCardAvailabilityView.cs`** — per Approach's "Debug view weight/percent widen" and "judgment call" sections: `GetDrawWeight` returns `double`, reads `definition.Chance` and multiplies by `definition.DrawWeightMultiplier ?? 1.0` (skip the multiplier lookup — just use `1.0` — when `definition` is null, matching the existing null-safety shape); widen `totalDrawWeight`, `DeckGroup.DrawWeight`, `DeckActionGroup.TargetDrawWeight`, `Add(...)` parameters, and `CalculateChancePercent`'s parameters to `double`; drop `x{count}` from both `BuildDeckCard` and `BuildTargetedDeckGroup` title strings, replacing with `({FormatNumber(chancePercent)}%)`.
- [x] **Update `Game.Tests` call sites** — across `BotCardAcquisitionTests.cs`, `VisualStateConverterCardDrawTests.cs`, `UnifiedPipelineTests.cs`, `VisualStateConverterCountryActionsVisibilityTests.cs`, `StringConfigParityTests.cs`, `DeclareWarCardTests.cs`, `VisualStateConverterDebugOrgCardAvailabilityTests.cs`, `GameLogStateTests.cs`, `DrawCardSystemTests.cs`, `DiscardCardSystemTests.cs`, `ControlFeatureTests.cs`, `RevengeCardGameLogicTests.cs`, `InitSystemTests.cs`, `CardDrawOfferTests.cs`, `MultiOrgTestSupport.cs`: rename every `DeckCopies = N` to `Chance = N` (as `double`, e.g. `Chance = 1` is a valid `int`→`double` implicit literal, no cast needed for integer literals) and every `.DeckCopies` read/assert to `.Chance`; `GameLogStateTests.cs`'s `ControlActionConfig(int deckCopies)` keeps its `int` parameter for `HandSize` and passes it to `Chance` (implicit `int`→`double` conversion, no cast needed). Also update the live prose comments still referencing `DeckCopies` in `CardDrawOfferTests.cs` (lines 447, 487), `GameLogStateTests.cs` (lines 99, 246), and `MultiOrgTestSupport.cs` (line 129) to say `Chance` instead.
- [x] **Fix the `stop_rivalry`/`declare_war` weight tests** — in `CardDrawOfferTests.cs`, add `DrawWeightMultiplier = 0.5` to the `stop_rivalry` entry and `DrawWeightMultiplier = 1.7` to the `declare_war` entry inside `BuildConfig()`, so `stop_rivalry_draws_at_half_weight` and `declare_war_gains_weight_only_for_the_player_org_when_controlling_a_rival_of_the_target` keep exercising real ×0.5/×1.7 behavior now that `AdjustWeight` reads it from config instead of hardcoding it.
- [x] **Search for any remaining reference** — re-run `grep -rn "DeckCopies\|deckCopies"` across `src/` and `Assets/` and confirm zero hits outside historical comments that were already updated in the steps above.
- [x] **Run the full test suite** — `dotnet test` on `src/GlobalStrategy.Core.sln` (via the `dotnet-test` skill), confirm all tests pass, including the new/adjusted `CardDrawOfferTests` and `StringConfigParityTests` assertions.
- [x] **`/dotnet-build Release`** — mandatory after any `src/` change per `.claude/rules/workflow.md`; confirms `Assets/Plugins/Core/` DLLs rebuild clean. Stop and report if it fails.

## User Steps

None. This is a pure C#/JSON change: a config field rename, a small system/query update, and a debug-only Unity UI Toolkit view's string formatting. There is no scene, prefab, or other Unity-Editor-authored asset to touch, and `DebugCardAvailabilityView` has no existing automated test harness (it's plain C# under `Assets/Scripts/Unity/UI`, not `Game.Tests`), so the only way to see its new percentage rendering is manual visual inspection in the Editor's dev-menu debug panel — which per `.claude/rules/unity/mcp_usage.md` ("Do not self-test in Play mode") is left to the user rather than driven by Claude entering Play mode. After implementation, ask the user to open the debug card-availability panel in Play mode and confirm rows show `(N.NN%)` instead of `x{count}`, and that a destroyed-country-targeted card shows `(0%)`.

## Tests

All new/updated coverage lives in `src/Game.Tests` (xunit, run via `dotnet test`/the `dotnet-test` skill):

- **Rename-only regressions** — every existing test enumerated in Agent Steps continues to assert the same numeric intent, just via `Chance` instead of `DeckCopies` (e.g. `StringConfigParityTests.sell_arms_action_deserializes_with_required_conditions_and_effects` now asserts `Assert.Equal(9.0, action.Chance)`). These are pre-existing tests, not new ones, but must all stay green — this is the primary regression net for the rename.
- **`stop_rivalry_draws_at_half_weight` / `declare_war_gains_weight_only_for_the_player_org_when_controlling_a_rival_of_the_target`** (`CardDrawOfferTests.cs`) — updated per Agent Steps to set `DrawWeightMultiplier` explicitly in `BuildConfig()`; these remain the behavioral proof that the config-driven multiplier reproduces today's ×0.5/×1.7 outcomes, including that `declare_war`'s boost still only applies for the player org controlling a rival of the target (the retained world-state gate).
- **New: `AdjustWeight` generic-multiplier coverage** — add a focused test (in `DrawCardSystemTests.cs` or `CardDrawOfferTests.cs`) that a **third**, previously-unmodeled action id with a configured `DrawWeightMultiplier` (e.g. `2.0`) gets that multiplier applied with no special-cased branch needed — proving the lookup is generic, not just working for the two grandfathered ids by coincidence.
- **New: `DrawOrgCards` fractional-weight coverage** — add a test giving two org-deck actions fractional `Chance` values (e.g. `1.5` and `4.5`) and asserting the weighted-pick distribution across many seeded trials favors the higher-weight card, proving the `int` → `double` widen didn't silently truncate weights (a pre-widen bug would round `1.5`/`4.5` down to `1`/`4` via `int` field access, which would previously not even compile once the field is `double`, but a truncation into the roll math would still be a latent risk worth a regression test).
- **New/updated: `DebugCardAvailabilityView` weight computation** — since this class has no existing `Game.Tests` coverage (verified: zero references outside `Assets/Scripts/Unity/UI`), this plan does not add C# unit tests for it (it's a Unity-assembly class without a `Game.Tests`-visible seam for `GetDrawWeight`/`CalculateChancePercent`/title-building without a broader test-access change out of scope for this plan). Coverage here is the manual User Step above. If a future plan wants automated coverage, `CalculateChancePercent`/`FormatNumber`/`GetDrawWeight` would need to move to `public` (per `.claude/rules/csharp/code_style.md`'s "prefer public over InternalsVisibleTo" guidance) — not done here since it's outside this issue's requested scope.
- Full suite (`dotnet test` on `src/GlobalStrategy.Core.sln`) must pass before `/dotnet-build Release`.

## Constitution Check

Checked against `Docs/Constitution.md`.

- *Unity 6 + URP only.* No rendering/material/shader changes.
- *ECS for all game logic in `src/`.* `DrawCardSystem`/`CountryCardDrawQuery`/`RelationCardSyncSystem`/`RevengeCardSyncSystem`/`InitSystem` are existing ECS systems in `src/`; this plan edits their query/weight logic in place, no new MonoBehaviour game-state.
- *VContainer sole DI.* No new container registrations; `DebugCardAvailabilityView` keeps its existing constructor-injected `ActionConfig`.
- *UI Toolkit only.* `DebugCardAvailabilityView` is existing UI Toolkit C# (`VisualElement`/`Button`); only string-building logic changes, no Canvas/UGUI.
- *Plan before implement.* This plan file is written and pending approval before any code/asset change, per this repo's workflow.
- *Spec before plan for feature work.* `spec.md` already exists and is finalized with owner-resolved decisions; this is a technical migration off an approved spec, consistent with either path.
- *File organisation.* This plan lives at `Docs/Specs/26_09_01_17_card-deck-chance-display/plan.md`, matching the spec's existing folder — not the legacy `Docs/Plans/` location.
- *One `.asmdef` per Assets feature folder.* No new folders/assemblies; `DebugCardAvailabilityView.cs` stays in its existing `Assets/Scripts/Unity/UI/GS.Unity.UI.asmdef`.
- *C# code style.* Tabs, `_`-prefixed private members, braces always, no redundant access modifiers — all edits follow the existing style of the files they touch (verified against `ActionConfig.cs`, `DrawCardSystem.cs`, `DebugCardAvailabilityView.cs` as-read).

No conflicts found — plan aligns with all principles.

Use the implement skill to start working on the plan or request changes.
