# Plan: Secret Advisor Feature Flag

## Spec

As a game owner, I want a config-driven feature flag that disables the country secret advisor and its related action card entirely, so that this content is fully absent from the game for now without deleting its data or code.

Acceptance criteria:
- Checked-in config has the secret advisor flag off (its shipped default):
  - A new game starts => No country has a secret advisor character.
  - A new game starts => The action card tied to the secret advisor is never dealt to any country's hand or deck.
  - Player opens any country's characters/actions views => No trace of the secret advisor or its card appears anywhere (not shown, not just disabled/greyed out).
- Config has the secret advisor flag turned on:
  - A new game starts => Every country's secret advisor character and related action card are created exactly as they are today (unchanged current behavior).
- Regardless of flag value:
  - A new game starts => All other character roles and action cards are created exactly as they are today (the flag affects only the secret advisor and its one related card).

Resolved ambiguities (owner, issue #121): flag name `EnableSecretAdvisor`/`enableSecretAdvisor` confirmed; checked-in JSON value confirmed `false`; scope confirmed as exactly `secret_advisor` role + `letter_of_commendation_secret_advisor` card.

## Goal

Add `FeatureFlagSettings.EnableSecretAdvisor` (C# bool, default `false`) alongside the existing `ShowPlayerOrgControls` flag, wire `"enableSecretAdvisor": false` into the checked-in `game_settings.json`, and gate `InitSystem.CreateCharacterEntities`'s per-role loop so no `secret_advisor` `Character` entity (or its resource entities) is ever created when the flag is off — which transitively starves `CreateCountryActionEntities`'s target resolution for `letter_of_commendation_secret_advisor`, so zero action-card entities for it are created either.

## Approach

This follows the exact sibling pattern from `Docs/Specs/26_08_03_08_player-org-feature-flag/`, which added `ShowPlayerOrgControls` to the same `FeatureFlagSettings` class. `src/Game.Configs/FeatureFlagSettings.cs` currently has one bool property; add a second one, defaulting `false` (unlike the sibling, which defaults `true` — this flag ships off per the issue). `Assets/Configs/game_settings.json`'s existing `featureFlags` block gets one more camelCase key.

The gate point is a single `if` inside `InitSystem.CreateCharacterEntities`'s existing `foreach (var role in characterConfig.Roles)` loop (`src/Game.Main/InitSystem.cs:178`): when `role.RoleId == "secret_advisor"` and the flag is off, `continue` before creating the `Character` entity. The flag itself is read once in `Run` (`src/Game.Main/InitSystem.cs:50`, right after `context.GameSettings.Load()`) and threaded through as a new `bool enableSecretAdvisor` parameter on `CreateCharacterEntities`, matching how `resourceConfig`/`rng` are already threaded as plain parameters rather than re-reading settings inside the callee.

No change is needed in `CreateCountryActionEntities`: it resolves each action's `targets` purely by scanning `Character` entities already present in the `world` (`InitSystem.cs:630-651`, `:672-681`). With zero `secret_advisor` `Character` entities created, `letter_of_commendation_secret_advisor`'s `targets` list stays empty and its per-copy entity-creation loop (`:683-684`) simply never runs for it — no new conditional required there. Likewise `VisualStateConverter.s_roleOrder` and `CharactersView` need no change: they only ever surface roles that have a backing `Character` entity, so an absent entity is invisible for free.

Two existing test fixtures build their own in-memory `GameSettings` without setting `FeatureFlags`, so they'll pick up the new `false` C# default and their `secret_advisor`-presence assertions will start failing — `CharacterInitTests.cs` and `CharacterVisualStateTests.cs` both need `FeatureFlags = new FeatureFlagSettings { EnableSecretAdvisor = true }` added to their `BuildLogic` helpers' `gameSettings` object to keep exercising the behavior they assert. `CharacterCardHintProjectorTests.cs` calls `CharacterCardHintProjector.Build` directly with a hand-built `ActionConfig` and a `"secret_advisor"` role-id string — it never touches `InitSystem`, `GameSettings`, or `Character` entities, so it is unaffected and needs no change.

## Section 1 — Agent Steps

- [ ] **Add `EnableSecretAdvisor` to `FeatureFlagSettings`** — in `src/Game.Configs/FeatureFlagSettings.cs`, add `public bool EnableSecretAdvisor { get; set; } = false;` below the existing `ShowPlayerOrgControls` property.
- [ ] **Add `enableSecretAdvisor` to `game_settings.json`** — in `Assets/Configs/game_settings.json`'s existing `"featureFlags"` block (currently lines 66-68, containing `"showPlayerOrgControls": false`), add `"enableSecretAdvisor": false` as a sibling key.
- [ ] **Thread the flag into `CreateCharacterEntities` and gate the role loop** — in `src/Game.Main/InitSystem.cs`: in `Run` (line 22), after `var settings = context.GameSettings.Load();` (line 50), read `var enableSecretAdvisor = settings.FeatureFlags.EnableSecretAdvisor;`; change the `CreateCharacterEntities(world, context, resourceConfig, rng)` call (line 120) to pass `enableSecretAdvisor` as a new final argument; update the method signature at line 164 to `static void CreateCharacterEntities(World world, GameLogicContext context, ResourceConfig resourceConfig, Random rng, bool enableSecretAdvisor)`; inside the `foreach (var role in characterConfig.Roles)` loop (line 178), immediately after the existing `if (!pool.Slots.TryGetValue(...)) { continue; }` check, add:
  ```csharp
  if (role.RoleId == "secret_advisor" && !enableSecretAdvisor) {
      continue;
  }
  ```
  No change to `CreateCountryActionEntities` — it already resolves targets purely from `Character` entities present in the world, so an absent `secret_advisor` entity naturally yields zero targets and zero card entities for `letter_of_commendation_secret_advisor`.
- [ ] **Update `CharacterInitTests.BuildLogic`** — in `src/Game.Tests/CharacterInitTests.cs`, in the `gameSettings` object built inside `BuildLogic` (currently lines 98-103), add `FeatureFlags = new FeatureFlagSettings { EnableSecretAdvisor = true }` so the existing `Assert.Contains("secret_advisor", roles)` (line 172) keeps passing under the new `false` default.
- [ ] **Update `CharacterVisualStateTests.BuildLogic`** — in `src/Game.Tests/CharacterVisualStateTests.cs`, in the `gameSettings` object built inside `BuildLogic` (currently lines 102-107), add `FeatureFlags = new FeatureFlagSettings { EnableSecretAdvisor = true }` so the existing `Assert.Contains("secret_advisor", roleIds)` (line 173) keeps passing under the new `false` default.
- [ ] **Confirm `CharacterCardHintProjectorTests.cs` needs no change** — verify (already checked while planning) that it calls `CharacterCardHintProjector.Build` directly with its own `ActionConfig` fixture and never constructs `GameSettings`/`InitSystem`/`Character` entities, so its `secret_advisor`-role-id assertions are unaffected by the new flag's default. No edit expected; leave as-is.
- [ ] **Add `FeatureFlagSettingsTests` cases for `EnableSecretAdvisor`** — in `src/Game.Tests/FeatureFlagSettingsTests.cs`, add facts mirroring the existing `ShowPlayerOrgControls` coverage: (1) a JSON round-trip fact deserializing a `featureFlags` block containing `"enableSecretAdvisor": true` and asserting `settings.FeatureFlags.EnableSecretAdvisor` is `true`; (2) a defaults-when-absent fact deserializing JSON with no `featureFlags` block (or the block present but missing the `enableSecretAdvisor` key) and asserting `settings.FeatureFlags.EnableSecretAdvisor` is `false`; (3) extend (or add to) the class-default fact so `new FeatureFlagSettings().EnableSecretAdvisor` is asserted `false`.
- [ ] **Run the `Game.Tests` suite** — use the `dotnet-test` skill to confirm the updated/new tests pass and nothing else regressed.

## Section 2 — User Steps

### 1. Optional manual playtest with the flag toggled on

This feature touches no scenes, prefabs, or UI assets — only config data and a C#-side entity-creation gate — so no Unity Editor verification is strictly required. If desired, temporarily set `Assets/Configs/game_settings.json`'s `featureFlags.enableSecretAdvisor` to `true`, start a new game in the Editor, and confirm every country's secret advisor character and its action card appear exactly as they did before this change, then revert the value back to the checked-in `false` before committing.

## Tests

Extend the config-layer unit coverage; no new integration/system test is needed since `CreateCountryActionEntities` requires no code change (its existing behavior already produces the right outcome once the `Character` entity is absent).

1. `src/Game.Tests/FeatureFlagSettingsTests.cs` — add, following the existing `ShowPlayerOrgControls` three-fact shape:
   - `enableSecretAdvisor_round_trips_from_json` — deserialize a JSON string with `"featureFlags": { "enableSecretAdvisor": true }`, assert `settings.FeatureFlags.EnableSecretAdvisor` is `true`.
   - `enableSecretAdvisor_defaults_to_false_when_absent_from_json` — deserialize JSON with no `featureFlags` block, assert `settings.FeatureFlags.EnableSecretAdvisor` is `false`.
   - `featureFlagSettings_class_default_enableSecretAdvisor_is_false` — `new FeatureFlagSettings()`, assert `EnableSecretAdvisor` is `false` (either as a new fact or an added assertion in the existing `featureFlagSettings_class_default_is_true` fact — keep whichever reads more naturally alongside the existing `ShowPlayerOrgControls` assertion).
2. `src/Game.Tests/CharacterInitTests.cs` — update `BuildLogic`'s `gameSettings` to set `FeatureFlags = new FeatureFlagSettings { EnableSecretAdvisor = true }`, keeping the existing `Assert.Contains("secret_advisor", roles)` assertion meaningful and passing. No new fact needed — the flag-off path (secret advisor absent) is out of scope for this file's existing coverage focus; if the implementer wants explicit off-path coverage, an additional fact building `GameLogic` with `EnableSecretAdvisor = false` (or omitted, since that's now the default) and asserting `secret_advisor` is absent from produced roles would directly exercise the acceptance criteria — recommended as a follow-up fact in this same file since `BuildLogic` already supports parameterizing without disruption.
3. `src/Game.Tests/CharacterVisualStateTests.cs` — same update to `BuildLogic`'s `gameSettings`, keeping the existing `Assert.Contains("secret_advisor", roleIds)` assertion passing.
4. `src/Game.Tests/CharacterCardHintProjectorTests.cs` — no change; confirmed unaffected (operates on `CharacterCardHintProjector.Build` directly, no `GameSettings`/`InitSystem` involvement).

Run via the `dotnet-test` skill against the solution containing `Game.Tests` (same project hosting `FeatureFlagSettingsTests`/`CharacterInitTests`).

## Constitution Check

- **Rendering (URP only):** No rendering changes. No conflict.
- **Game Logic (ECS in `src/`, no game state in MonoBehaviours):** The gate lives entirely in `InitSystem` (a `src/Game.Main` system), reading a config value and conditionally skipping entity creation — no MonoBehaviour touches game state. No conflict.
- **Dependency Injection (VContainer is the sole DI mechanism):** No new dependency wiring — `GameSettings` already flows into `InitSystem.Run` via the existing `GameLogicContext.GameSettings.Load()` call; the flag is read from that already-resolved object. No conflict.
- **UI (UI Toolkit only):** No UI assets or Canvas/UGUI touched; `CharactersView`/`VisualStateConverter` need no change since they only ever render `Character` entities that exist. No conflict.
- **Planning Discipline (plan before implement):** This plan itself satisfies the requirement; no code changes have been made yet. No conflict.
- **Specification Discipline (spec before plan for feature work):** `spec.md` exists and is approved with all ambiguities resolved (issue #121). No conflict.
- **File Organisation (`Docs/Specs/<YY_MM_DD_HH>_<name>/`):** This plan is saved at `Docs/Specs/26_08_03_13_secret-advisor-flag/plan.md`, alongside its `spec.md`. No conflict.
- **Assembly Structure (one `.asmdef` per feature folder):** No new folders or assemblies introduced; all edits stay within existing `src/Game.Configs`, `src/Game.Main`, and `src/Game.Tests` projects. No conflict.
- **C# Code Style (tabs, `_` prefix, braces always, no redundant access modifiers):** All new/edited code follows existing file conventions — tabs, braces on the new `if`, no explicit access modifiers beyond what the sibling `ShowPlayerOrgControls` property already uses. No conflict.

No conflicts found — plan aligns with all principles.

Use the implement skill to start working on the plan or request changes.
