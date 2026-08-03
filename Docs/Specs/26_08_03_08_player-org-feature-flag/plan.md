# Plan: Player Org Feature Flag (Show Player Org Controls)

## Spec

As a game owner, I want a config-driven feature flag that can hide the player organization's "Characters" and "Actions" buttons, so that I can disable those controls for a build/rollout without a code change.

Acceptance criteria:
- `game_settings.json` `featureFlags` block controls visibility of the player org's toggle buttons:
  - `featureFlags.showPlayerOrgControls` = `false` → whether the player org panel opens or is already open, neither the "Characters" nor the "Actions" button is shown.
  - `featureFlags.showPlayerOrgControls` = `true`, or the `featureFlags` block / `showPlayerOrgControls` key absent entirely → both buttons show exactly as they do today (existing per-content visibility rules still apply).
- Existing per-content visibility rules are preserved regardless of the flag:
  - Flag `true`/absent AND player org has no characters → "Characters" button stays hidden (unchanged existing behavior).
  - Flag `true`/absent AND player org has no actions in hand/deck → "Actions" button stays hidden (unchanged existing behavior).
  - Flag `false` AND player org has characters/actions → both buttons stay hidden anyway — the flag overrides content-based visibility, it does not just relax it.

Resolved ambiguities (owner, issue #119, 2026-08-03):
- Checked-in `Assets/Configs/game_settings.json` value is `false` — buttons hidden immediately for all players.
- An already-open Characters/Actions sub-panel is not force-closed when the flag evaluates to `false` — out of scope, flag is static/build-time config.
- Scope is `OrgInfoDocument`'s two buttons only — no other UI entry points, no discovered/other-org view.

## Goal

Add a `FeatureFlagSettings.ShowPlayerOrgControls` bool (default `true`) to `GameSettings`, wire a `false` value into the checked-in `game_settings.json`, and AND it into `OrgInfoDocument.Refresh()`'s existing `hasChars`/`hasActions` visibility checks.

## Approach

`GameSettings` already flows into `OrgInfoDocument` for free once injected: `GameLifetimeScope` already registers `GameSettings` as a VContainer singleton (`builder.Register(c => c.Resolve<GameLogic>().GameSettings, Lifetime.Singleton)`, `Assets/Scripts/Unity/DI/GameLifetimeScope.cs:82`), and sibling documents (`HUDDocument`, `EndGameWindowDocument`) already resolve it by adding a `GameSettings` parameter to their `[Inject] void Construct(...)` method. No new VContainer registration is needed — `OrgInfoDocument` only needs the same parameter added to its existing `Construct` method and a new `_gameSettings` field.

The new setting follows the sibling-class pattern `GameLogSettings`/`EventNotificationSettings` already use: each lives in its own file (`src/Game.Configs/GameLogSettings.cs`, `src/Game.Configs/EventNotificationSettings.cs`) and is referenced by a property on `GameSettings`, including the paired xUnit round-trip/default tests `GameLogSettingsTests` already establishes for that pattern.

## Section 1 — Agent Steps

- [ ] **Add `FeatureFlagSettings`** — create `src/Game.Configs/FeatureFlagSettings.cs` with a new class `FeatureFlagSettings` containing `public bool ShowPlayerOrgControls { get; set; } = true;`, matching the file-per-class pattern `GameLogSettings.cs`/`EventNotificationSettings.cs` use (not inline in `GameSettings.cs`, which only the older `WarBattleSettings` does). Add a `public FeatureFlagSettings FeatureFlags { get; set; } = new();` property on `GameSettings` in `src/Game.Configs/GameSettings.cs`, placed alongside the other nested settings properties (near `GameLog`/`EventNotifications`).
- [ ] **Add `featureFlags` block to `game_settings.json`** — in `Assets/Configs/game_settings.json`, add `"featureFlags": { "showPlayerOrgControls": false }` (camelCase keys), placed near the other settings blocks (e.g. alongside `gameLog`/`eventNotifications`).
- [ ] **Inject `GameSettings` into `OrgInfoDocument`** — in `Assets/Scripts/Unity/UI/OrgInfoDocument.cs`, add a `GameSettings _gameSettings;` field, add `GameSettings gameSettings` as a parameter to the existing `[Inject] void Construct(...)` method, and assign `_gameSettings = gameSettings;` in the body. No VContainer registration change needed — `GameSettings` is already registered as a singleton in `GameLifetimeScope`.
- [ ] **AND the flag into `Refresh()`'s visibility checks** — in `OrgInfoDocument.Refresh()` (around the existing `hasChars`/`hasActions` block, currently lines ~157-165), compute `bool showControls = _gameSettings?.FeatureFlags?.ShowPlayerOrgControls ?? true;` and AND it into both existing conditions:
  ```csharp
  bool showControls = _gameSettings?.FeatureFlags?.ShowPlayerOrgControls ?? true;
  bool hasChars = showControls && _state.PlayerOrganization.Characters.Slots.Count > 0;
  if (_charsToggleBtn != null) {
      _charsToggleBtn.style.display = hasChars ? DisplayStyle.Flex : DisplayStyle.None;
  }

  bool hasActions = showControls && (_state.PlayerOrganization.Actions.Hand.Count > 0 || _state.PlayerOrganization.Actions.Deck.Count > 0);
  if (_actionsToggleBtn != null) {
      _actionsToggleBtn.style.display = hasActions ? DisplayStyle.Flex : DisplayStyle.None;
  }
  ```
  Keep `style.display = DisplayStyle.None` as the hide mechanism (unchanged) and keep both existing null-guards on the buttons (unchanged). No other line in `Refresh()` changes; no force-close of already-open sub-panels is added (out of scope per resolved ambiguity).
- [ ] **Add `FeatureFlagSettingsTests`** — in `src/Game.Tests/`, add a new `FeatureFlagSettingsTests.cs` mirroring `GameLogSettingsTests.cs`'s three-test shape (see Tests section below) to cover JSON round-trip, default-when-absent, and class-default behavior for `FeatureFlagSettings`.
- [ ] **Run the `Game.Tests` suite** — use the `dotnet-test` skill to confirm the new tests pass and nothing else regressed.

## Section 2 — User Steps

### 1. Visually verify button hiding in the Unity Editor

This automation environment has no Unity Editor access, so the actual UI Toolkit rendering can't be confirmed here. After implementation, open the project in Unity, enter Play mode (or use whatever existing flow opens the player org panel), and confirm:
- With `Assets/Configs/game_settings.json`'s `featureFlags.showPlayerOrgControls` at its checked-in value `false`, neither the "Characters" nor the "Actions" button is visible on the player org panel, even for an org that has characters/actions.
- Temporarily setting the value to `true` (or removing the `featureFlags` block) restores today's behavior: both buttons appear, still subject to the existing per-content rules (a button stays hidden if the org genuinely has no characters, or no actions in hand/deck).
- Revert the temporary test edit back to the checked-in `false` value before committing, since `false` is the owner-approved shipped value.

## Tests

`OrgInfoDocument` is a `MonoBehaviour` + UI Toolkit binding class — this codebase does not unit-test that layer (no existing test project references `Assets/Scripts/Unity/UI/*`; UI documents are presentation/binding glue verified by hand in the Editor, consistent with `.claude/rules/unity/mcp_usage.md`'s "do not self-test in Play mode" guidance directing verification to the human user instead of synthetic input). So the `Refresh()` display-toggling logic itself is not unit-tested; it is covered by the Section 2 manual verification step.

What CAN and should be unit-tested is the config layer, which already has an established pattern in `src/Game.Tests/GameLogSettingsTests.cs` for the sibling `GameLogSettings` nested-class shape. Add `src/Game.Tests/FeatureFlagSettingsTests.cs` with three xUnit facts mirroring that file:

1. `featureFlags_block_round_trips_from_json` — deserialize a JSON string containing `"featureFlags": { "showPlayerOrgControls": false }` via `JsonConvert.DeserializeObject<GameSettings>`, assert `settings.FeatureFlags.ShowPlayerOrgControls` is `false`.
2. `featureFlags_defaults_apply_when_block_absent_from_json` — deserialize a minimal JSON object with no `featureFlags` key, assert `settings.FeatureFlags.ShowPlayerOrgControls` is `true` (the C# default, not JSON presence, guarantees this per the spec's Tech Notes).
3. `featureFlagSettings_class_default_is_true` — `new FeatureFlagSettings()`, assert `ShowPlayerOrgControls` is `true`.

Run via the `dotnet-test` skill against the solution containing `Game.Tests` (same project that hosts `GameLogSettingsTests`).

## Constitution Check

- **Rendering (URP only):** No rendering changes. No conflict.
- **Game Logic (ECS in `src/`, no game state in MonoBehaviours):** No game state is added; `FeatureFlagSettings` is a config value (like other `GameSettings` nested classes), and `OrgInfoDocument` only reads it to toggle presentation (`style.display`), matching its existing use of `_state`/`_resourceConfig` for the same purpose. No conflict.
- **Dependency Injection (VContainer is the sole DI mechanism):** `GameSettings` is already registered as a VContainer singleton in `GameLifetimeScope`; `OrgInfoDocument` receives it via the existing `[Inject] void Construct(...)` method-injection pattern, no `new`, no `FindObjectOfType`, no static mutable singleton. No conflict.
- **UI (UI Toolkit only):** No Canvas/UGUI touched; only C#-driven `style.display` toggling on existing UXML elements, per the spec's Tech Notes. No conflict.
- **Planning Discipline (plan before implement):** This plan itself satisfies the requirement; no code changes have been made yet. No conflict.
- **Specification Discipline (spec before plan for feature work):** `spec.md` exists and is approved with all ambiguities resolved. No conflict.
- **File Organisation (`Docs/Specs/<YY_MM_DD_HH>_<name>/`):** This plan is saved at `Docs/Specs/26_08_03_08_player-org-feature-flag/plan.md`, alongside its `spec.md`. No conflict.
- **Assembly Structure (one `.asmdef` per feature folder):** No new folders or assemblies are introduced; `OrgInfoDocument.cs` stays in its existing `Assets/Scripts/Unity/UI/` assembly, `FeatureFlagSettingsTests.cs` stays in the existing `Game.Tests` project. No conflict.
- **C# Code Style (tabs, `_` prefix, braces always, no redundant access modifiers):** All new/edited code follows the existing file conventions — tabs, `_gameSettings` private field prefix, braces on every `if`, no explicit `private` on private members. No conflict.

No conflicts found — plan aligns with all principles.

Use the implement skill to start working on the plan or request changes.
