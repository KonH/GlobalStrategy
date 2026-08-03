# Spec: Player Org Feature Flag (Show Player Org Controls)

## Feature Intent

As a game owner, I want a config-driven feature flag that can hide the player organization's "Characters" and "Actions" buttons, so that I can disable those controls for a build/rollout without a code change.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

- `game_settings.json` featureFlags block controls visibility of the player org's toggle buttons
  - Config has `featureFlags.showPlayerOrgControls` set to `false` => Player org panel opens (or is already open) => The "Characters" button is not shown
  - Config has `featureFlags.showPlayerOrgControls` set to `false` => Player org panel opens (or is already open) => The "Actions" button is not shown
  - Config has `featureFlags.showPlayerOrgControls` set to `true`, or the `featureFlags` block / `showPlayerOrgControls` key is absent entirely => Player org panel opens => Both buttons show exactly as they do today (existing per-content visibility rules, see below, still apply)
- Existing per-content visibility rules are preserved regardless of the flag
  - Flag is `true` (or absent) AND the player org has no characters => Player org panel opens => "Characters" button stays hidden (unchanged existing behavior)
  - Flag is `true` (or absent) AND the player org has no actions in hand/deck => Player org panel opens => "Actions" button stays hidden (unchanged existing behavior)
  - Flag is `false` AND the player org has characters/actions => Player org panel opens => Both buttons stay hidden anyway — the flag overrides content-based visibility, it does not just relax it

## Tech Notes

- New nested settings class in `src/Game.Configs/GameSettings.cs`, following the existing pattern of `WarBattleSettings`/`GameLogSettings`/`EventNotificationSettings`:
  ```csharp
  public class FeatureFlagSettings {
      public bool ShowPlayerOrgControls { get; set; } = true;
  }
  ```
  exposed as a new property on `GameSettings`:
  ```csharp
  public FeatureFlagSettings FeatureFlags { get; set; } = new();
  ```
  Default `true` so an absent `featureFlags` block or an absent `showPlayerOrgControls` key preserves current behavior (buttons visible, subject to existing content rules) — the C# default, not JSON presence, is what guarantees this.
- `Assets/Configs/game_settings.json`: add a `"featureFlags": { "showPlayerOrgControls": false }` block (camelCase JSON keys matching the PascalCase C# properties, per existing convention, e.g. `"startYear"` <-> `StartYear`). The owner's request sets the flag to `false` in this rollout; whether the checked-in default value should be `true` or `false` is covered under Ambiguities.
- `Assets/Scripts/Unity/UI/OrgInfoDocument.cs` is the only consumer (it exclusively renders `_state.PlayerOrganization`; there is a separate view for discovered/other orgs, out of scope here):
  - Inject `GameSettings` via the existing `[Inject] void Construct(...)` method (VContainer method-injection pattern per `.claude/rules/unity/vcontainer.md`; `GameSettings` is already registered as a config object resolved off `GameLogic`, following `builder.Register(c => c.Resolve<GameLogic>().GameSettings, Lifetime.Singleton)` — mirror however the sibling configs `ResourceConfig`/`CharacterConfig`/etc. are currently registered).
  - In `Refresh()` (around the existing `hasChars`/`hasActions` block at lines ~157-165), AND the flag into the existing per-content checks rather than replacing them:
    ```csharp
    bool showControls = _gameSettings?.FeatureFlags?.ShowPlayerOrgControls ?? true;
    bool hasChars = showControls && _state.PlayerOrganization.Characters.Slots.Count > 0;
    _charsToggleBtn.style.display = hasChars ? DisplayStyle.Flex : DisplayStyle.None;
    bool hasActions = showControls && (_state.PlayerOrganization.Actions.Hand.Count > 0 || _state.PlayerOrganization.Actions.Deck.Count > 0);
    _actionsToggleBtn.style.display = hasActions ? DisplayStyle.Flex : DisplayStyle.None;
    ```
    This keeps `style.display = DisplayStyle.None` as the hide mechanism (consistent with existing code at those lines) and preserves the "no content => hidden" behavior when the flag is on.
  - No UXML changes needed — `OrgInfo.uxml`'s `org-toggle-block`/`chars-toggle-btn`/`actions-toggle-btn` structure is unchanged; only C#-driven `style.display` toggling changes.

## Out of Scope

- Any UI entry point for the player org other than the two named buttons in `OrgInfoDocument`/`OrgInfo.uxml` (see Ambiguities for whether this should be revisited).
- The discovered/other-org view (a separate file from `OrgInfoDocument.cs`) — not mentioned in the request and not touched.
- Any settings-window UI to toggle the flag at runtime — this is a build-time config value only, per the literal request.
- Force-closing an already-open Characters/Actions sub-panel when the flag is false (see Ambiguities).

## Ambiguities

- [NEEDS CLARIFICATION: Should the checked-in `Assets/Configs/game_settings.json` default `showPlayerOrgControls` to `true` (preserve current shipped behavior, flag exists but inactive) or `false` (immediately hide the buttons for all players)? The issue text implies the owner wants it off now ("if disabled player org don't show..."), but the acceptance criteria above assume `true` is the safe C#-level default for any *unset* key — this question is specifically about what value ships in the actual JSON file today.]
- [NEEDS CLARIFICATION: If the Characters or Actions sub-panel is already open (slid out) at the moment the flag evaluates to false — e.g. the flag were ever changed at runtime, or on the frame `Refresh()` first runs after the panel was opened by some other trigger — should the open sub-panel be force-closed (`SetCharsOpen(false)`/`SetActionsOpen(false)`), or is hiding just the toggle button sufficient per the literal request, leaving an already-open panel visible with no way to close it via the button? Given the flag is described as build-time/static config rather than a live toggle, this may not be reachable in practice — confirm whether it's worth guarding anyway.]
- [NEEDS CLARIFICATION: Does "player org" here mean only the buttons on `OrgInfoDocument` (the panel exclusively bound to `_state.PlayerOrganization`), or should the flag also affect any other player-org-specific UI entry points elsewhere in the HUD (e.g. a keyboard shortcut, a different panel, or a notification/tutorial prompt that references Characters/Actions) that aren't visible from the two files inspected for this spec?]
