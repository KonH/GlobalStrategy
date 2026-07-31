# Spec: Debug Card Availability

## Feature Intent

As a developer debugging card gates, I want the Selected country and My organization debug sections to show deck/hand card availability with per-condition pass/fail coloring in a two-column layout, so that I can see at a glance why a card cannot be drawn or played without digging through simulation state.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

- The debug panel is open.
  - Selected country and My organization sit side-by-side as two columns (not stacked one above the other).
  - Selected province remains a full-width section below those two columns, unchanged in content and behavior.
- The developer expands Selected country.
  - Character-related controls (Next/Drop per country role, Improve Opinion) live inside an inner expandable "Characters" block and are not loose at the top of the section.
  - Relation debug controls (country dropdown, Set friend / Set rival / Clear relation) remain visible in the Selected country section but outside the Characters foldout, unchanged in behavior.
  - Two additional inner expandable blocks appear: "Deck" and "Hand".
- The developer expands Selected country's Deck block while a country is selected and that country has cards still in the deck (not in hand).
  - Each distinct deck card appears once as an expandable row labeled like `CardName xN (M%)`, where N is the number of copies of that card still in the deck and M is the chance to draw that card on the next draw among currently drawable deck entities.
  - The card-name portion of the row is green when at least one of those deck copies is eligible to be drawn, red when none are.
  - Expanding a deck card row lists each draw-gate condition as its own green (passing) or red (failing) line; if the card has costs, cost affordability appears as an additional condition-like green/red line.
- The developer expands Selected country's Hand block while that country has cards in hand.
  - Every card currently in hand appears as its own expandable row (no collapsing of hand copies).
  - The card name is green when the card is playable, red when it is not.
  - Expanding a hand card row lists each play-gate condition as its own green/red line; if the card has costs, cost affordability appears as an additional condition-like green/red line.
- No country is selected, or the selected country has an empty deck / empty hand.
  - Expanding Deck or Hand shows an empty list (no crash, no leftover rows from a previous selection).
- The developer expands My organization.
  - Character Next/Drop controls (master and agents) live inside an inner expandable "Characters" block.
  - Discover All Countries and the Win/Lose force buttons remain in the My organization section but outside the Characters foldout, unchanged in behavior.
  - Inner expandable "Deck" and "Hand" blocks appear and behave the same way as the country blocks, sourced from the player's organization deck and hand.
- The developer toggles any of the new inner foldouts (Characters, Deck, Hand) or the outer Selected country / My organization menus.
  - Each uses the same ▶ closed / ▼ open label prefix pattern already used by other debug menu toggles.
- Labels and condition text in these debug blocks.
  - All text is debug-only English (or raw debug identifiers); no localization keys are required.

## Tech Notes

- **Two-column Selected country / My organization layout:**
  - Restructure `Assets/UI/HUD/HUD.uxml` so `btn-selected-country-debug-menu` + `selected-country-debug-menu` and `btn-my-organization-debug-menu` + `my-organization-debug-menu` sit in a shared horizontal (`flex-direction: row`) container under `debug-panel`, each column taking roughly half width.
  - Keep `btn-selected-province-debug-menu` / `selected-province-debug-menu` full-width below that row.
  - Add any needed layout rules in `Assets/UI/HUD/HUD.uss` (reuse `.debug-panel`, `.debug-panel-inner`, `.debug-panel-menu-toggle`, `.debug-panel-button` patterns).
- **Characters foldouts (Selected country):**
  - Introduce an expandable Characters block inside `selected-country-debug-menu` (new toggle button + `debug-panel-inner` container), wired via the existing `RegisterDebugMenuToggle` pattern in `Assets/Scripts/Unity/UI/HUDDocument.cs`.
  - Move the buttons currently parented under `character-debug-container` (Next/Drop per country-pool role + Improve Opinion, built around lines ~187–220 of `HUDDocument.cs`) into that Characters inner container.
  - Leave `relation-debug-container` (built by `BuildRelationDebugUi`) as a sibling outside Characters — relation dropdown / Set friend / Set rival / Clear relation stay where they are functionally.
- **Characters foldouts (My organization):**
  - Introduce the same Characters expandable pattern inside `my-organization-debug-menu`.
  - In `RebuildOrgCharDebugButtons`, only Next/Drop for `master` and agent slots go into the Characters container; Discover All Countries and Win/Lose (`PushForceCompletionCondition`) buttons stay outside Characters in the org section root (today they all share `org-char-debug-container`).
- **Deck / Hand foldouts — data sources:**
  - Country: `VisualState.SelectedCountry.CountryActions` (`CountryActionsState` Hand + Deck of `ActionCardEntry`) populated by `VisualStateConverter.UpdateCountryActions` / `BuildEntry` (`src/Game.Main/VisualStateConverter.cs`).
  - Org: `VisualState.PlayerOrganization.Actions` (`OrgActionsState` Hand + Deck) populated by `VisualStateConverter.UpdateOrgActions`.
  - Refresh the Deck/Hand debug lists whenever those visual-state collections change (same PropertyChanged-driven refresh path other HUD debug widgets already use in `HUDDocument.cs`).
- **Per-card expandable UI:**
  - Inside each Deck/Hand foldout, create one expandable row per displayed card using `RegisterDebugMenuToggle` (or the same ▶/▼ show/hide convention on a `debug-panel-inner`).
  - Card title color: green when available (drawable in Deck / playable in Hand), red otherwise — USS classes or inline tint on the toggle label; debug-only, no locale keys.
  - Expanded body lists each evaluated condition row with the same green/red coloring.
- **Per-condition evaluation (country cards — Hand playability):**
  - Today `ActionCardEntry` exposes only aggregate `IsUnplayable` + `UnplayableReason`; it does **not** expose per-condition pass/fail (`src/Game.Main/VisualState.cs`).
  - Extend the visual-state (or a debug-only projection built alongside it) so each hand/deck entry can surface per-condition results.
  - Evaluate each `ActionDefinition.Conditions` `ExpressionNode` individually against the same `ExpressionContext` fields already assembled in `VisualStateConverter.BuildEntry` / `ActionPlayability.Evaluate` (`src/Game.Systems/ActionPlayability.cs`): green if `ExpressionNode.Evaluate(cond, ctx) != 0.0`, red if `== 0.0`.
  - Also append a cost-affordability row when `def.Cost` is non-empty, using `ActionPlayability.CanAfford` (or the same resource lookup): green if affordable, red if not. Note: `BuildEntry` currently folds conditions into `IsUnplayable` but does **not** set unplayable for unaffordable cost — play-side gold check also lives in `CountryActionsView`; the debug Hand row must still show cost as its own gate line so the developer can see affordability failures.
  - Optionally also surface the existing `sphere_of_pressure` `pool_full` gate (`usedTotal >= 100` in `BuildEntry`) as an extra condition-like row when relevant.
- **Draw eligibility + Deck chance (country cards):**
  - Draw eligibility is gated by the same `conditions` expressions in `DrawCardSystem.DrawCountryCards` (`src/Game.Systems/DrawCardSystem.cs`): only entities still in the deck (have `GameAction`+`OrgContext`+`CountryContext`, lack `CardInHand`) whose conditions all evaluate non-zero enter the eligible pool. Cost is **not** checked at draw time.
  - The eligible pool is Fisher–Yates shuffled; each eligible **entity** has equal weight.
  - Deck UI must not list duplicate rows for identical cards: collapse by display identity (see Ambiguities for ActionId-only vs ActionId+TargetCountryId), show `CardName xN (M%)` where:
    - `N` = count of deck entities in that group (eligible + ineligible).
    - `M%` = `100 * (count of eligible entities in that group) / (total eligible deck entities for this country/org)` when total eligible &gt; 0; `0%` when none are eligible.
  - Collapsed row green iff that group's eligible count &gt; 0; red otherwise.
  - Expanded body for a Deck row should evaluate conditions against a representative entity (or show per-copy breakdown if copies differ — prefer one representative when all copies share the same gates; relation-targeted instances with different targets must not be collapsed together if their conditions/context differ).
- **Org Deck / Hand:**
  - `UpdateOrgActions` currently builds bare `ActionCardEntry(actionId, slot, isInHand)` with no `IsUnplayable` / reason and no condition evaluation.
  - `DrawCardSystem.DrawOrgCards` does **not** filter by conditions — every org deck entity (no `CardInHand`, no `CountryContext`) is eligible at equal weight.
  - Debug Deck chance for org cards therefore uses the same equal-weight formula over all org deck entities (typically all eligible unless a future condition gate is added).
  - Debug Hand playability for org cards should call into `ActionPlayability.Evaluate` (which already supports org cards with `countryId: null` and checks conditions + `CanAfford`) so cost failures at least surface; see Ambiguities if org actions remain condition-empty.
- **Display names:**
  - Resolve card titles the same way the live Actions UI does (locale name keys / target-country formatting for relation cards such as `declare_war` / `stop_rivalry`), but condition row labels may use raw expression type strings (`opinion`, `relationStillExists`, `neitherSideAtWar`, `cost:gold`, etc.) since this is debug-only.
- **No scene/prefab work:** UXML + USS + `HUDDocument.cs` (and any VisualState / converter extensions needed for per-condition data) only.

## Out of Scope

- Any change to actual draw/play rules, `DrawCardSystem`, `ActionPlayability` gating semantics, or card configs — this feature only visualizes existing gates.
- Player-facing Actions panel UI changes (`CountryActionsView` / org actions UI) beyond what debug needs.
- Localization keys for debug labels.
- Selected province debug content or layout beyond staying full-width below the new two-column row.
- Moving relation debug controls into Characters, or moving Discover/Win/Lose into Characters.
- Web client / terminal debug surfaces for the same information.

## Ambiguities

- [NEEDS CLARIFICATION: For Deck deduplication of relation-targeted country cards (e.g. multiple `declare_war` instances naming different rivals), is the collapse key ActionId only (`declare_war x2 (40%)`), or ActionId + TargetCountryId so each named rival stays its own `Declare war on Spain x1 (20%)` row?]
- [NEEDS CLARIFICATION: Org cards today have empty `conditions` in `action_config.json` and `UpdateOrgActions` never sets playability — should org Hand/Deck debug still show only cost affordability (and treat every org deck copy as drawable), or should this feature also start wiring full condition evaluation into org visual state for future-proofing?]
- [NEEDS CLARIFICATION: In Deck expanded rows, should cost affordability still appear as a condition-like line even though draw ignores cost, or should cost rows appear only under Hand?]
- [NEEDS CLARIFICATION: When multiple deck copies of the same ActionId share a collapsed row but would evaluate differently (unlikely today except via TargetCountryId), should the expanded body show one representative's conditions or a per-copy breakdown?]
