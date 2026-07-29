# Plan: Character Card Unlock Hints in Tooltips

## Spec

As a player, hovering a country character card should tell me which cards that character's role unlocks and at which opinion thresholds, so I don't have to play cards blind to discover the gate.

Acceptance criteria (see `spec.md` for the full legend):
- Hovering a character card whose role has one or more opinion-gated cards appends a hint list below the existing role name/description tooltip content: one row per gated card, reading `At opinion <threshold> - <card name>`.
- Rows are ordered by ascending threshold; ties keep stable (config) order and are never merged/deduplicated.
- A row is styled "met" (green) when the character's current opinion >= the row's threshold, "not met" (gray) otherwise.
- A role with no opinion-gated cards shows the existing tooltip content unchanged — no empty heading/list.
- Hint data is fully config-driven: editing `action_config.json` thresholds/gates changes the displayed rows with no tooltip-text changes.
- Hovering a hint row opens a second, nested, non-interactive card preview (art/name/desc/cost) scoped as a child of the character tooltip; moving off the row (not onto the preview) closes only the preview; moving off the character card closes both.
- Out of scope: non-opinion conditions (e.g. `control`), authoring tools, opinion-earning/animation changes, and any card whose only gate is non-opinion.

## Goal

Add a pure, config-driven helper that lists a role's opinion-gated cards, and wire it into `CharactersView`'s existing per-character tooltip so hovering a hint row previews that card, reusing existing tooltip-stacking, card-building, and shared USS infrastructure — no new game logic, no new system.

## Approach

1. **Shared condition-threshold helper (dedupe, not reimplement).** `CountryActionsView.ExtractConditionThreshold(ActionDefinition def, string fieldType)` (`Assets/Scripts/Unity/UI/CountryActionsView.cs:152`) already walks `ActionDefinition.Conditions` (`List<ExpressionNode>`) for a `{"type":"gte","members":[{"type":fieldType},{"type":"value"}]}` node, but returns `0` as a "not found" sentinel — ambiguous with a genuine `gte 0` threshold, and it's `private` inside a Unity-only view class the new pure `src/Game.Main` projector can't call anyway (wrong layer/direction). Move the walk into a new public static helper in `src/Game.Configs` (the layer that owns both `ActionDefinition` and `ExpressionNode`), shaped as a `TryExtract` so "no such condition" is unambiguous:
   ```csharp
   // src/Game.Configs/ActionConditionHelper.cs
   public static class ActionConditionHelper {
       public static bool TryExtractConditionThreshold(ActionDefinition def, string fieldType, out int threshold) { ... }
   }
   ```
   `CountryActionsView` calls this instead of its private copy (discarding the bool where it's only used after `UnplayableReason` already told it the condition exists); `CharacterCardHintProjector` calls it to both filter (exclude actions with no opinion condition) and extract the threshold in one pass.

2. **New pure projector, `src/Game.Main/CharacterCardHintProjector.cs`, mirroring `WinConditionHintProjector`.** Signature: `Build(ActionConfig actionConfig, string roleId) -> List<CharacterCardHintRowState>`. (The spec's Tech Notes sketch a `CharacterConfig characterConfig` parameter too, but nothing in the filter/threshold logic needs it — `roleId` alone drives the `ActionConfig.Actions` filter — so it's dropped to avoid an unused parameter.) Logic: iterate `actionConfig.Actions`, keep `ActionDefinition.TargetRole == roleId`, call `ActionConditionHelper.TryExtractConditionThreshold(def, "opinion", out threshold)`, skip if `false`, otherwise add a row. Sort ascending by threshold using a **stable** sort (`List<T>.OrderBy` via LINQ, not `List<T>.Sort`) so same-threshold rows keep config order per the acceptance criteria.

3. **Row state shape, `CharacterCardHintRowState`, added to `src/Game.Main/VisualState.cs`** next to `WinConditionHintRowState` (same file already hosts sibling hint-row shapes):
   ```csharp
   public class CharacterCardHintRowState {
       public string ActionId { get; }
       public int Threshold { get; }
       public bool IsMet { get; set; }
       public CharacterCardHintRowState(string actionId, int threshold) {
           ActionId = actionId;
           Threshold = threshold;
       }
   }
   ```
   `IsMet` is mutable and left `false` by the pure projector; `CharactersView` sets it per-row after `Build` returns by comparing against `entry.Opinion.Display` (see step 5) — matching the spec's "projector stays config-only, caller computes IsMet" split without needing the projector to see live resource state.

4. **Rebuild the DLL after adding `src/Game.Main`/`src/Game.Configs` code.** Per `.claude/rules/unity/plugins.md`, `dotnet build src/GlobalStrategy.Core.sln -c Release` regenerates `Assets/Plugins/Core/Game.Main.dll` and `Game.Configs.dll` — required before Unity-side code compiles against the new types.

5. **Wire into `CharactersView` — data already available, no new DI.** `CountryInfoView` (`Assets/Scripts/Unity/UI/CountryInfoView.cs:46`) already receives `ActionConfig actionConfig` and `ActionVisualConfig actionVisualConfig` as constructor parameters and already constructs a sibling view (`CountryActionsView`, line 79) with them — so `CharactersView`'s constructor gains the same two parameters, and the call site at `CountryInfoView.cs:67` passes them through. No VContainer/asmdef changes needed; `GS.Unity.UI.asmdef` already references the config/common assemblies (confirmed by existing `using GS.Game.Configs;` / `using GS.Unity.Common;` in `CharactersView.cs`).

   In `BuildCharacterCard` (`CharactersView.cs:40`), the existing role tooltip trigger (line 120: `_tooltip.RegisterTrigger(card, $"role-{entry.RoleId}-{entry.CharacterId}", _ => BuildSimpleTooltip(roleName, capturedDesc), ...)`) is replaced by a trigger whose `buildContentFunc` receives the `TooltipContext` (needed for nested triggers — the outer `RegisterTrigger` call already gets a `TooltipContext` param, currently discarded as `_`) and:

   Note: today this trigger is registered only inside `if (!string.IsNullOrEmpty(roleDesc))` (`CharactersView.cs:118`). All current roles have non-empty descriptions, so this is not an active bug, but hint-row presence is now driven by config independent of `roleDesc` — widen the guard to `if (!string.IsNullOrEmpty(roleDesc) || hintRows.Count > 0)` (computing `hintRows` via `CharacterCardHintProjector.Build` before the guard) so a future description-less role still shows its hint tooltip.
   - Calls `CharacterCardHintProjector.Build(_actionConfig, entry.RoleId)`.
   - Sets `row.IsMet = entry.Opinion.Display >= row.Threshold` for each row (character's live opinion is already flowing into `entry.Opinion.Display` — the same value the opinion badge on the card already shows, per `CharactersView.cs:74-79` — so no separate `Resource{opinion_<orgId>}` lookup is needed at the UI layer).
   - Builds `BuildSimpleTooltip(roleName, capturedDesc)` as today, and — only if the row list is non-empty — appends a rows container below it. Each row is a `Label` (`AddToClassList("tooltip-effect-name")` for typography, plus `AddToClassList("tooltip-inner-trigger")`, plus `"gs-color-positive"` when met / `"gs-color-hint"` when not met via `EnableInClassList` — matching the `plusRow`/`minusRow` pattern in `ResourcesView.cs:124-126`, which combines a typography class with `tooltip-inner-trigger` and a colour class, and matching the already-defined green/gray palette entries in `SharedStyles.uss` rather than inventing new one-off colour classes) reading the localized `hud.character.card_hint` format string.
   - Registers each row via `context.RegisterInnerTrigger(rowLabel, $"card-hint-{entry.CharacterId}-{row.ActionId}", innerCtx => BuildCardPreview(row.ActionId))`, exactly the pattern already used by `ResourcesView.cs` (`ctx.RegisterInnerTrigger(plusRow, ...)`, lines ~131-184) and `CountryInfoView.cs:251` — this is an established, working pattern in this codebase, so no extra `PickingMode` handling is needed: `TooltipSystem.OpenTooltip` already calls `SetPickingIgnoreRecursive` on the **outer** panel only after `buildContent` (and therefore the inner `RegisterTrigger` calls) have already run, and the existing nested triggers in `ResourcesView`/`CountryInfoView` prove pointer enter/leave still reaches inner-trigger elements under that call order. This resolves the spec's open question in Tech Notes ("confirm whether... already applies this") — it does; follow the existing call shape exactly, do not add a second picking-mode pass.
   - `BuildCardPreview(actionId)` resolves `_actionConfig.Find(actionId)` for `NameKey`/`DescKey` via `_loc.Get(...)`, `_actionVisualConfig?.FindFront(actionId)` for the sprite, and a gold-cost string via the same shape as `CountryActionsView.GetGoldCostText`/`GetGoldCost` (`CountryActionsView.cs:163-175`) — since that pair is `private static` and there's no existing shared version, duplicate the same two small static methods into `CharactersView` (they're pure formatting over `ActionDefinition.Cost`, not worth a cross-file promotion for one extra call site). Calls `ActionCardBuilder.Build(name, desc, goldCostText, sprite)` and adds `"action-card--available"` (matching the preview-only usage already established in `CardTransitionView.cs`, which builds a card purely for animation/preview with no click handler wired) to the returned `Card`, then returns it as the nested tooltip content — no `PointerUpEvent`/click wiring.

6. **USS.** `hud-root` (owned by `HUD.uxml`) is where the tooltip panel actually lives (per the USS-scope rule in `.claude/rules/unity/uitoolkit.md`), and `HUD.uxml` already imports `SharedStyles.uss`, `HUD.uss`, **and** `Assets/UI/Overlay/OrgInfo/OrgActions.uss` (confirmed at `HUD.uxml:9-11`) — so `.action-card`/`.action-card-header`/etc. (used by `ActionCardBuilder`, defined in `OrgActions.uss`) and `.tooltip-header`/`.tooltip-effect-name`/`.tooltip-inner-trigger`/`.gs-color-positive`/`.gs-color-hint` (all in `SharedStyles.uss`) already resolve with zero new imports; the spec's Tech Notes assumption that these live in `HUD.uss` is slightly off, but the net effect (no new import needed) is the same. Add exactly one new layout-only class to `Assets/UI/HUD/HUD.uss` (per the "new dynamically created elements' container classes go in the document that owns hud-root" rule): `.character-hint-rows` for the rows container (column flex, small top margin to separate from the header/body labels). Do not add new colour/typography classes — reuse `.gs-color-positive`/`.gs-color-hint`/`.tooltip-inner-trigger`/`.tooltip-effect-name` per the "shared classes for all visual styling" usage rule.

7. **Locale key.** Add `hud.character.card_hint` = `At opinion {0} - {1}` to `Assets/Localization/en.asset`, with a real Russian translation added to `ru.asset` via the `localization` skill (not a placeholder copy). Namespaced under `hud.*` per `.claude/rules/unity/localization.md`; no threshold/name values baked into the English text.

## Steps

### Agent Steps

- [ ] **Add `ActionConditionHelper` to `src/Game.Configs`** — new file `src/Game.Configs/ActionConditionHelper.cs` with `public static bool TryExtractConditionThreshold(ActionDefinition def, string fieldType, out int threshold)`, moving the `gte`/`opinion`/`value` tree-walk logic currently in `CountryActionsView.ExtractConditionThreshold` (`Assets/Scripts/Unity/UI/CountryActionsView.cs:152-161`).
- [ ] **Update `CountryActionsView` to call the shared helper** — replace the private `ExtractConditionThreshold` method and its two call sites (`~78`, `~83`) with calls to `ActionConditionHelper.TryExtractConditionThreshold(def, "opinion"/"control", out threshold)`, discarding the bool (both call sites only run when `UnplayableReason` already implies the condition exists) and delete the now-dead private method.
- [ ] **Add `CharacterCardHintRowState` to `src/Game.Main/VisualState.cs`** — plain state class (`ActionId`, `Threshold`, mutable `IsMet`) placed near `WinConditionHintRowState`.
- [ ] **Add `src/Game.Main/CharacterCardHintProjector.cs`** — `public static class CharacterCardHintProjector { public static List<CharacterCardHintRowState> Build(ActionConfig actionConfig, string roleId) { ... } }`, filtering `actionConfig.Actions` by `TargetRole == roleId`, using `ActionConditionHelper.TryExtractConditionThreshold(def, "opinion", out threshold)` to filter+extract, sorted ascending by threshold via a stable LINQ `OrderBy`.
- [ ] **Add `src/Game.Tests/ActionConditionHelperTests.cs`** — unit tests for the promoted helper (see Tests section).
- [ ] **Add `src/Game.Tests/CharacterCardHintProjectorTests.cs`** — unit tests for the new projector, mirroring `WinConditionHintProjectorTests.cs` shape (see Tests section).
- [ ] **Run `dotnet-test` skill** against `src/GlobalStrategy.Core.sln` to confirm the two new test files and the updated `CountryActionsView`-adjacent logic compile and pass before touching Unity code.
- [ ] **Rebuild the Core DLLs** — `dotnet build src/GlobalStrategy.Core.sln -c Release` so `Assets/Plugins/Core/Game.Main.dll` and `Game.Configs.dll` pick up the new/changed types.
- [ ] **Extend `CharactersView` constructor and call site** — add `ActionConfig actionConfig, ActionVisualConfig actionVisualConfig` parameters to `CharactersView`'s constructor (`Assets/Scripts/Unity/UI/CharactersView.cs:17`), store them as `_actionConfig`/`_actionVisualConfig` fields, and update the construction call in `Assets/Scripts/Unity/UI/CountryInfoView.cs:67` to pass `actionConfig, actionVisualConfig` (both already in `CountryInfoView`'s own constructor parameters).
- [ ] **Build the hint rows and nested preview in `CharactersView.BuildCharacterCard`** — widen the tooltip's registration guard from `if (!string.IsNullOrEmpty(roleDesc))` to `if (!string.IsNullOrEmpty(roleDesc) || hintRows.Count > 0)` (computing `hintRows` via `CharacterCardHintProjector.Build` before the guard), then replace the role tooltip's `_ => BuildSimpleTooltip(...)` lambda with a `context =>` lambda that: calls `CharacterCardHintProjector.Build`, sets `IsMet` per row from `entry.Opinion.Display`, builds the existing header/body content, and — only when rows are non-empty — appends a `.character-hint-rows` container of localized, met/unmet-styled (`tooltip-effect-name` + `gs-color-positive`/`gs-color-hint`), nested-trigger-registered row `Label`s. Add the small `BuildCardPreview(string actionId)` helper (plus duplicated `GetGoldCost`/`GetGoldCostText` static helpers, matching `CountryActionsView.cs:163-175`) that returns an `ActionCardBuilder.Build(...)`-built preview card with `"action-card--available"` added and no click handler.
- [ ] **Add `.character-hint-rows` to `Assets/UI/HUD/HUD.uss`** — column flex, small top margin, no colour/font rules (those come from reused shared classes).
- [ ] **Add locale key via the `localization` skill** — `hud.character.card_hint` = `At opinion {0} - {1}` in `Assets/Localization/en.asset`, with a real Russian translation generated into `ru.asset` by the skill (not an English placeholder).
- [ ] **`refresh_unity` and `read_console(types=["error"])`** — confirm the Unity-side compile is clean after all script/USS/locale changes.

### User Steps

### 1. Visual verification in Play Mode

Enter Play Mode, open a country's Characters panel, and hover a character card whose role has at least one opinion-gated action in `Assets/Configs/action_config.json`. Confirm: the hint rows appear below the existing role description, ordered ascending by threshold; a row whose threshold is at/below the character's current opinion renders in the green (`gs-color-positive`) style and one above renders gray (`gs-color-hint`); hovering a row opens the nested non-playable card preview with correct art/name/desc/cost; moving off the row (not onto the preview) closes only the preview; moving off the character card closes both. Also hover a character whose role has zero opinion-gated cards and confirm no empty heading/list appears.

### 2. Confirm layout/spacing reads correctly

Since `.character-hint-rows` is a new dynamically-laid-out container inside the existing tooltip panel, visually confirm in the Editor that row spacing, the panel's auto-resize/reposition behavior (via `TooltipSystem.PositionNear`/`AdjustPosition`), and the tooltip's width don't clip the longest expected card name/threshold combination at typical resolutions.

## Tests

- **`src/Game.Tests/ActionConditionHelperTests.cs`** (new) — unit tests for `ActionConditionHelper.TryExtractConditionThreshold`:
  - a `gte` condition on the requested `fieldType` against a `value` node returns `true` and the correct integer threshold
  - a `Conditions` list with only a different field type (e.g. `control` when asking for `opinion`) returns `false`
  - a `Conditions` list with only non-`gte` condition types (e.g. an `eq`/`lte` node) returns `false`
  - an empty `Conditions` list returns `false`
  - a genuine `gte opinion 0` condition returns `true` with `threshold == 0` (the case the old sentinel-`0`-return couldn't distinguish from "not found")
- **`src/Game.Tests/CharacterCardHintProjectorTests.cs`** (new, mirrors `WinConditionHintProjectorTests.cs`) — unit tests for `CharacterCardHintProjector.Build`:
  - a role with one matching action carrying an opinion `gte` condition yields a single row with the right `ActionId`/`Threshold`
  - a role with no actions at all yields an empty list
  - a role whose only action has a `control`-only condition (no opinion `gte`) yields an empty list
  - actions for a different `TargetRole` are excluded
  - multiple gated actions are returned ordered ascending by threshold
  - two actions sharing the same threshold both appear, in the same order they appear in `ActionConfig.Actions` (stable sort — no merge/dedup)
- No existing test file exercises `CountryActionsView.ExtractConditionThreshold` today (it's `private` inside a Unity-assembly view class with no `Game.Tests` coverage) — the coverage moves onto the new `ActionConditionHelperTests.cs`, which is a net increase in tested surface for that logic. No existing test needs updating beyond this move.

## Constitution Check

- **ECS for all game logic (`src/`).** `CharacterCardHintProjector` and `ActionConditionHelper` are pure, stateless, config-only helpers — no `World`, no components, no simulation state, no system entry point call from another system. They are read-only projections over already-loaded config data, the same shape as `WinConditionHintProjector`. `CharactersView` (a plain C# view, not a MonoBehaviour) calls them directly, matching how `CountryActionsView` already calls plain static helpers. No conflict.
- **VContainer sole DI.** No new container registrations are introduced — `ActionConfig`/`ActionVisualConfig` are threaded through existing constructor parameters that `CountryInfoView` already receives via its own VContainer-resolved construction path. No `new` of a singleton service, no `FindObjectOfType`. No conflict.
- **UI Toolkit only.** All new elements (`Label`s, a rows container `VisualElement`) are UI Toolkit `VisualElement`s built in C#, added to an existing UI Toolkit tooltip panel. No Canvas/UGUI. No conflict.
- **One asmdef per feature folder.** No new folders under `Assets/Scripts/` are introduced; all Unity-side changes stay inside the existing `Assets/Scripts/Unity/UI` (`GS.Unity.UI.asmdef`) and `Assets/Scripts/Unity/Common` folders, which already have their asmdefs and already reference the assemblies this plan needs (`GS.Game.Configs`, `GS.Main` via `Plugins/Core`, `GS.Unity.Common`). No conflict.
- **C# code style.** Plan follows tabs, `_`-prefixed private fields, always-braced control flow, no redundant access modifiers, matching every existing file read during planning (`CharactersView.cs`, `CountryActionsView.cs`, `WinConditionHintProjector.cs`). No conflict.
- **Planning discipline / spec-before-plan.** This plan follows an approved `spec.md` in the same `Docs/Specs/26_07_29_00_character-hints/` folder, per the File Organisation and Specification Discipline principles. No conflict.

No conflicts found — plan aligns with all principles.

Use the implement skill to start working on the plan or request changes.
