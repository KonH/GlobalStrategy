# Spec: Secret Advisor Feature Flag

## Feature Intent

As a game owner, I want a config-driven feature flag that disables the country secret advisor and its related action card entirely, so that this content is fully absent from the game for now without deleting its data or code.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

- Checked-in config has the secret advisor flag off (its shipped default)
  - A new game starts => No country has a secret advisor character
  - A new game starts => The action card tied to the secret advisor is never dealt to any country's hand or deck
  - Player opens any country's characters/actions views => No trace of the secret advisor or its card appears anywhere (not shown, not just disabled/greyed out)
- Config has the secret advisor flag turned on
  - A new game starts => Every country's secret advisor character and related action card are created exactly as they are today (unchanged current behavior)
- Regardless of flag value
  - A new game starts => All other character roles and action cards are created exactly as they are today (the flag affects only the secret advisor and its one related card)

## Tech Notes

- New flag on the existing `FeatureFlagSettings` block (`src/Game.Configs/FeatureFlagSettings.cs`), same file/class introduced by the sibling spec `Docs/Specs/26_08_03_08_player-org-feature-flag/spec.md`:
  ```csharp
  public class FeatureFlagSettings {
      public bool ShowPlayerOrgControls { get; set; } = true;
      public bool EnableSecretAdvisor { get; set; } = false;
  }
  ```
  Unlike the sibling flag, this one defaults `false` in C# — per the issue, the feature "is not needed for now" and should be off unless explicitly enabled.
- `Assets/Configs/game_settings.json`: add `"enableSecretAdvisor": false` inside the existing `"featureFlags"` block (camelCase JSON key matching the PascalCase C# property, same convention as `showPlayerOrgControls`). This is a checked-in, build-time value only — no settings-window UI or runtime toggle.
- Single gate point, in `src/Game.Main/InitSystem.cs`:
  - `Run(...)` (currently starting ~line 22) already calls `var settings = context.GameSettings.Load();` (currently ~line 50); read `settings.FeatureFlags.EnableSecretAdvisor` there and thread it as a new `bool` parameter into `CreateCharacterEntities(World world, GameLogicContext context, ResourceConfig resourceConfig, Random rng)` (currently starting ~line 164, called from `Run` ~line 120).
  - Inside `CreateCharacterEntities`'s `foreach (var role in characterConfig.Roles)` loop (currently ~line 178), skip creating the `Character` entity (and its resource entities) when `role.RoleId == "secret_advisor"` and the new flag is off.
  - No other change needed: `CreateCountryActionEntities` (currently starting ~line 620) builds `charsByCountryAndRole` purely by scanning `Character` entities that exist in the `world` (~lines 630-651), then resolves each action definition's `targets` via that dictionary (~lines 672-681); with no `secret_advisor` `Character` entities present, the one action definition in `Assets/Configs/action_config.json` with `"targetRole": "secret_advisor"` (`letter_of_commendation_secret_advisor`) resolves to an empty `targets` list and creates zero card entities for it. `src/Game.Systems/DrawCardSystem.cs`'s `DrawCountryCards` only draws cards that already exist as world entities, so no later redraw can surface it either.
  - `src/Game.Main/VisualStateConverter.cs`'s `s_roleOrder` (line ~25, includes `"secret_advisor"`) and `Assets/Scripts/Unity/UI/CharactersView.cs` (renders `state.Characters` generically) need no change — with no `secret_advisor` `Character` entity ever created, neither surfaces it.
  - Existing `secret_advisor` references that stay as-is and are not touched by this feature: locale keys in `Assets/Localization/en.asset`/`ru.asset` (`character.role.secret_advisor.*`, `action.letter_of_commendation_secret_advisor.*`), the visual entry in `Assets/Configs/ActionVisualConfig.asset`, and `src/Game.Main/CharacterCardHintProjector.cs` (role-agnostic).
- No backward compatibility / migration: `InitSystem.Run` deterministically re-seeds all `Character`/`GameAction` entities from config at world-init time each game start (nothing persists from a prior save format that would need migrating away from), so flipping the flag off never needs to "remove" a previously-saved secret advisor.
- Existing unit tests that reference `secret_advisor` as local test fixture JSON (`src/Game.Tests/CharacterInitTests.cs`, `CharacterVisualStateTests.cs`, `CharacterCardHintProjectorTests.cs`) build their own in-memory config/settings rather than reading `Assets/Configs/game_settings.json`, so they are unaffected by the checked-in JSON default — but the plan step must check each one explicitly for whether it needs an updated `GameSettings`/flag value (or an explicit `true` override) to keep asserting the secret-advisor behavior it exercises.
- Follow existing JSON round-trip test pattern (`src/Game.Tests/FeatureFlagSettingsTests.cs`) for the new flag: round-trips `true`/`false` from JSON, and defaults to `false` when the `featureFlags` block or the `enableSecretAdvisor` key is absent from JSON entirely.

## Out of Scope

- Any other character role or action card besides `secret_advisor` / `letter_of_commendation_secret_advisor`.
- Any settings-window UI or runtime control to toggle the flag — checked-in JSON value only.
- Removing or renaming any existing `secret_advisor` config data, locale keys, or visual config entries — they remain in the repo, simply unused while the flag is off.
- Migration/fallback handling for any previously-saved game state that might reference a secret advisor character or card (no backward compatibility required, per the issue).

## Ambiguities

None — all three prior clarification questions were confirmed by the owner in the issue #121 thread:
- Flag name `EnableSecretAdvisor` (C#) / `enableSecretAdvisor` (JSON) confirmed.
- Checked-in `Assets/Configs/game_settings.json` value confirmed as `false`.
- Scope confirmed as exactly the `secret_advisor` role and its single related action card (`letter_of_commendation_secret_advisor`).
