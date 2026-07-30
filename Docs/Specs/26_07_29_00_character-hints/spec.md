# Spec: Character Card Unlock Hints in Tooltips

## Feature Intent

As a player, I want the country character tooltip to tell me which cards a character unlocks at which opinion levels, so that I understand what raising a character's opinion actually gets me without having to play cards blind to discover it.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

- The player hovers a character card in the country Characters panel, and that character's role has one or more cards gated by an opinion threshold
  - Hover the character card => the existing tooltip (role name + role description) appears, followed by a list of hint rows, one per opinion-gated card, each row reading "At opinion `<threshold>` - `<card name>`"
  - The tooltip has more than one hint row => rows are ordered by ascending opinion threshold
  - Two cards share the same opinion threshold => both rows are shown, in a stable order (not merged or deduplicated)
  - The character's current opinion (`Resource{opinion_<orgId>}`) is greater than or equal to a row's threshold => that row is styled in the "met" (green) label style
  - The character's current opinion is below a row's threshold => that row is styled in the "not met" (gray) label style
- The player hovers a character card whose role has no opinion-gated cards at all (either the role unlocks no cards, or its cards have no opinion condition)
  - Hover the character card => the tooltip shows only the existing role name/description content; no empty "cards" heading or list appears
- The card unlock data changes in config (a designer edits the opinion threshold on an existing card's condition, or adds/removes an opinion-gated card for a role) without any tooltip text being touched
  - Hover the character card afterward => the displayed threshold(s) and card list reflect the new config values automatically
- The player is already hovering the character tooltip with its hint list visible
  - Hover (move the pointer onto) one of the hint rows => a second, nested preview opens showing that card's non-playable view (art, name, description, cost) — the same visual card used elsewhere to preview a card without making it playable
  - Move the pointer off the hint row (and not onto the nested preview) => the nested preview closes; the underlying character tooltip remains visible
  - Move the pointer off the character card entirely => both the nested preview (if open) and the character tooltip close

## Tech Notes

Maps each product-facing behaviour above to its concrete implementation — specific files, classes, methods, commands, state paths.

- Hint rows appended to the existing whole-card tooltip, hidden when empty:
  - `Assets/Scripts/Unity/UI/CharactersView.cs`, inside `BuildCharacterCard(CharacterStateEntry entry)` (~line 40), the existing whole-card `_tooltip.RegisterTrigger(card, $"role-{entry.RoleId}-{entry.CharacterId}", _ => BuildSimpleTooltip(roleName, capturedDesc), ...)` call (~line 120) is extended: the tooltip content builder computes the hint rows for `entry` and, if the list is non-empty, appends a rows container below the existing header/body `Label`s built by `BuildSimpleTooltip` (~line 136); if the list is empty, only the current header/body content is returned, unchanged from today.
  - Row list is recomputed on every tooltip build (tooltip content is built lazily by the trigger's `buildContentFunc`, not cached), consistent with the "rebuild small lists on every `Refresh()`" convention noted in `Docs/Specs/26_07_24_10_friends-rivals-panel/spec.md`.
- Threshold values and eligible cards are derived from config, not authored text:
  - New pure static helper, e.g. `CharacterCardHintProjector.Build(CharacterConfig characterConfig, ActionConfig actionConfig, string roleId) -> List<CharacterCardHintRowState>` under `src/Game.Main/` (plain C#, no Unity/ECS dependency), mirroring the existing template `src/Game.Main/WinConditionHintProjector.cs` (`Build` fans out to a private `Flatten`/filter step, returns a small row-state list the UI layer only formats).
  - Filtering: iterate `ActionConfig.Actions` (`src/Game.Configs/ActionConfig.cs`), keep entries where `ActionDefinition.TargetRole == roleId`; `roleId` comes directly from `CharacterStateEntry.RoleId` (already the role id string that `ActionDefinition.TargetRole` is compared against elsewhere, e.g. `CountryActionsView`).
  - Threshold extraction: reuse the existing `gte` + `opinion` condition-tree walk in `CountryActionsView.ExtractConditionThreshold(ActionDefinition def, string fieldType)` (`Assets/Scripts/Unity/UI/CountryActionsView.cs` ~line 152) against `ExpressionNode` trees (`src/Game.Configs/ExpressionNode.cs`) — promote this method to a shared public helper (e.g. move/duplicate into the new `src/Game.Main` projector, or expose a public static utility both call) rather than re-implementing the same tree walk a second time; call out in the PR that `CountryActionsView`'s private copy should be replaced with a call to the shared version.
  - An action with `Conditions` containing no `{"type":"gte","members":[{"type":"opinion"},{"type":"value",...}]}` node is excluded from the hint list entirely (this is what "no opinion-gated cards" means in the Acceptance Criteria above) — actions with other condition types only (e.g. `control`) are not shown as opinion hints.
  - Card display name: `ActionConfig.Find(actionId).NameKey` resolved via `ILocalization.Get(...)` at the Unity/view layer (the pure projector returns the `actionId`/threshold pair; localization lookup happens in `CharactersView`, matching where `ILocalization` is already injected for role/skill names).
- Nested hover preview reuses the existing tooltip stacking + non-playable card view:
  - Each hint row becomes its own trigger element; register it via `TooltipContext.RegisterTrigger` (the nested `TooltipSystem` handed to the parent tooltip's `buildContentFunc`, per `Assets/Scripts/Unity/UI/TooltipController.cs`) rather than the outer `_tooltip`, so the preview is scoped as a child of the character tooltip and dismisses correctly when the parent closes.
  - Preview content: resolve `ActionConfig.Find(actionId)` for `NameKey`/`DescKey` (via `ILocalization.Get`), `ActionVisualConfig.FindFront(actionId)` (`Assets/Scripts/Unity/Common/ActionVisualConfig.cs`) for the art `Sprite`, cost text from the same `GetGoldCost`-style lookup already used in `CountryActionsView` (~line 163), then call `ActionCardBuilder.Build(name, desc, goldCostText, sprite)` (`Assets/Scripts/Unity/UI/ActionCardBuilder.cs`) and place the returned `CardResult.Card` into the nested tooltip's content — no play button/click handler is wired, matching how `CardTransitionView.cs` already reuses `ActionCardBuilder.Build` purely as a preview.
  - After building the nested tooltip content, call `SetPickingIgnoreRecursive`-equivalent handling if/as required by `TooltipController.cs`'s existing pattern (per `.claude/rules/unity/uitoolkit.md`, "PickingMode.Ignore is not recursive") — confirm whether `TooltipContext`/`TooltipSystem` already applies this for nested panels before adding a second pass.
- New locale keys for the hint row template text ("At opinion {0} - {1}") and any list heading go through the `localization` skill (`Assets/Localization/en.asset` + `ru.asset` with a real Russian translation), namespaced under `hud.*` per `.claude/rules/unity/localization.md` (e.g. `hud.character.card_hint`); no threshold/card-name values are hardcoded into the key's English text — those are interpolated at format time.
- Row container and row styling: new dynamically created elements get USS classes added in `Assets/UI/HUD/HUD.uss` (tooltip content is parented under `hud-root`, not the CountryInfo template — see the USS-scope rule in `.claude/rules/unity/uitoolkit.md`), reusing `tooltip-header`/`tooltip-effect-name` where applicable and shared typography classes (`.gs-content`/`.gs-hint`) from `SharedStyles.uss` rather than introducing new one-off font/colour rules.
- Met/not-met styling: `CharacterCardHintRowState` (the projector's row struct) carries the threshold, actionId, and a `bool IsMet` computed by the projector caller (`CharactersView`) by comparing the character's current `Resource{opinion_<orgId>}` value against the row's threshold — the projector itself stays pure/config-only and does not read live resource state. `CharactersView` applies one of two new USS classes to each row label (e.g. `character-hint-met` = green, `character-hint-unmet` = gray) added to `HUD.uss`, toggled via `EnableInClassList`, consistent with how other met/unmet or state-driven styling toggles classes elsewhere in the HUD views rather than setting inline colours.
- No system-to-system call is introduced: `CharacterCardHintProjector` is a plain helper class called from UI code (`CharactersView`), the same shape as `CharacterQuery`/`WinConditionHintProjector`, per `.claude/rules/unity/ecs_patterns.md`.

## Out of Scope

- Full action playability (cost affordability, non-opinion conditions such as `control`) is not reflected in the hint list — only the opinion-threshold condition is surfaced, matching the issue's mockup.
- No new UI surface outside the existing character tooltip (e.g. no dedicated "character detail" screen or panel).
- No change to how opinion itself is earned, displayed as `+N`/`-N` on the character card, or animated (`AnimatableInt Opinion` in `VisualState.cs`) — this feature only adds read-only hint text derived from existing data.
- No authoring/editing tool for opinion thresholds — designers continue to edit `Assets/Configs/action_config.json` directly; the hint list is a pure read/display of that data.
- Cards whose only unlock gate is something other than opinion (e.g. `control`-only conditions) are not shown in this hint list at all, not even without a threshold number.

## Resolved Clarifications

- Hint rows visually distinguish met vs. not-met thresholds: green label style when the character's current opinion is >= the row's threshold, gray label style otherwise. Owner confirmed 2026-07-29 (issue #77 comment).
- Locale string format confirmed literal: `At opinion {0} - {1}` (space-hyphen-space), per the issue mockup. Owner confirmed 2026-07-29 (issue #77 comment).
