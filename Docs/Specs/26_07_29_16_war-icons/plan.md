# Plan: War Icons

## Spec

Source: `Docs/Specs/26_07_29_16_war-icons/spec.md`.

Add a compact HUD row for active wars that are relevant to the player organization because at
least one participant country has positive player-org Control. Each qualifying war appears once,
ordered deterministically, as a bounded primary-pointer button containing the primary attacker
flag, a replaceable crossed-swords placeholder, and the primary defender flag. Hovering uses the
existing HUD tooltip system to show a localized `A - B War` title and current fractional progress;
clicking forwards only the `WarId` to an injected, intentionally empty progress-window shell.

The actual war progress window, changes to war mechanics, permanent HUD text, map effects,
notifications, animation, audio, and generated artwork remain out of scope.

## Goal

Project the existing ECS war/control model into stable, testable presentation state in
`src/Game.Main`, render that state through the existing `HUDDocument` + plain-view UI Toolkit
architecture, and establish the future `WarId` navigation seam without implementing the
destination window.

## Approach

### 1. Pure war-icon projection and state (`src/Game.Main`)

Add these immutable/state types to `src/Game.Main/VisualState.cs`:

```csharp
public class WarIconEntryState {
	public string WarId { get; }
	public double Progress { get; }
	public string AttackerCountryId { get; }
	public string DefenderCountryId { get; }
}

public class WarIconsState : INotifyPropertyChanged {
	public event PropertyChangedEventHandler? PropertyChanged;
	public IReadOnlyList<WarIconEntryState> Entries { get; private set; } = Array.Empty<WarIconEntryState>();
	public void Set(List<WarIconEntryState> entries) { ... }
}
```

Expose it from `VisualState` as `public WarIconsState WarIcons { get; } = new WarIconsState();`.
`Set` compares content through a new `StateEquality.WarIconEntryStateEquals` helper and does not
raise `PropertyChanged` for an identical projection. Equality includes all four fields, so a
progress change refreshes the tooltip data while unrelated ticks remain no-ops.

Add a public pure projector at `src/Game.Main/WarIconsProjector.cs`:

```csharp
public static List<WarIconEntryState> Build(IReadOnlyWorld world, string playerOrgId)
```

The projector performs bounded, deterministic passes:

1. Return an empty list when `playerOrgId` is empty.
2. Scan `ControlEffect` once, filter to `OrgId == playerOrgId`, and sum `Value` by `CountryId`.
   A country is relevant only when its final aggregate is strictly greater than zero; positive
   and negative effects are allowed to cancel.
3. Scan `WarParticipant` once and group non-empty country ids by `WarId` and
   `WarParticipantKind`. De-duplicate country ids, sort attackers and defenders independently
   with `StringComparer.Ordinal`, and use the first id on each side as the two displayed flags.
   Every participant on both sides still participates in the relevance check.
4. Scan entities containing both `War` and `WarProgress`. Omit a war when the id is empty, either
   primary side is missing, or none of its participant countries has positive aggregated player
   Control. Create one entry for each remaining `WarId`.
5. Sort the final entries by `WarId` using `StringComparer.Ordinal`. ECS archetype iteration order
   must never affect the HUD order.

This keeps Control aggregation, relevance filtering, de-duplication, malformed-war handling, and
primary-side selection outside MonoBehaviours and independently testable. It consumes the war
model from issue #69 as-is and does not add components or change war systems.

Add `UpdateWarIcons(IReadOnlyWorld world)` to `VisualStateConverter`. Call it immediately after
`UpdatePlayerOrganization` in the main update sequence, passing the current
`VisualState.PlayerOrganization.OrgId` (or an empty id when invalid), then set
`_state.WarIcons`. The existing update order therefore guarantees the projection uses the
current player organization on every tick.

### 2. HUD template, stable map-control positioning, and placeholder asset

Add:

- `Assets/UI/HUD/WarIcons/WarIcons.uxml`
- `Assets/UI/HUD/WarIcons/WarIcons.uss`
- `Assets/Textures/Buttons/War/crossed_swords.png`

`WarIcons.uxml` contains only the named row/container; buttons are generated from state.
`WarIcons.uss` owns internal button/icon/flag dimensions, padding, and per-button spacing. Dynamic
buttons use the shared `gs-btn` class for color, border, and interaction styling. The row is a
single non-wrapping flex row. Code applies `margin-left` only after the first button because
Unity 6000.4.1f1 does not support `gap`.

Compose the template from `Assets/UI/HUD/HUD.uxml`. Also import `WarIcons.uss` from the parent
HUD document because USS classes applied to C#-created elements resolve against the parent
document, not merely the embedded template.

Replace the two independently absolute-positioned map controls with one absolute
`map-controls-panel` wrapper in `HUD.uxml`/`HUD.uss`:

```text
map-controls-panel (bottom: 280px; left: 8px; column)
├── war-icons
└── lens-switcher
```

The wrapper preserves the existing lens-switcher anchor. `war-icons` is immediately before it in
normal column layout and uses a small bottom margin only while visible. When there are no entries,
the view applies `DisplayStyle.None` to the war row, so it contributes no empty space and the lens
switcher returns to its current location.

For the owner-approved placeholder, duplicate the existing imported single-sprite map-lens image
`Assets/Textures/Icons/MapLens/lens-political.png` to
`Assets/Textures/Buttons/War/crossed_swords.png`, preserving its sprite import settings while
letting Unity generate a unique asset GUID. Reference that new asset from `WarIcons.uss`. The
owner can replace the PNG in place later without changing UXML, USS, or C#.

### 3. `WarIconsView` rendering and tooltip behavior

Add `Assets/Scripts/Unity/UI/WarIconsView.cs` as a plain C# view in the existing
`GS.Unity.UI` assembly. Its constructor receives the row root, `ILocalization`,
`CountryVisualConfig`, the existing `TooltipSystem`, and an `Action<string>` open callback.
Its sole public update method is `Refresh(WarIconsState state)`.

Keep stable rendered button records keyed by `WarId`, rather than registering fresh tooltip
callbacks on every progress tick. `Refresh` updates a current-entry lookup, removes buttons for
missing ids, creates buttons only for new ids, updates existing flag sprites, and re-adds the
stable buttons in state order. Each button's tooltip builder resolves its latest entry from that
lookup when the tooltip opens, so a progress-only refresh cannot duplicate callbacks or present
a stale captured value.

Each parent `Button` has three child visual elements in this order:

1. primary attacker flag;
2. crossed-swords placeholder;
3. primary defender flag.

The flag sprites come from `CountryVisualConfig.Find(countryId)?.flag`. A missing sprite hides
only that flag element. All three children use `PickingMode.Ignore`; the parent button is the only
pointer target. Each parent uses `PointerDownEvent` to arm only a primary press that begins inside
that button, then `PointerUpEvent` invokes the callback only when the same press is armed,
`e.button == 0`, and `button.ContainsPoint(e.localPosition)` remains true. Release outside and
`PointerCancelEvent` clear the armed state without emitting; a press begun outside can therefore
never trigger by releasing over the button. Do not use `Button.clicked` or `ClickEvent`.

Register each parent with `TooltipSystem.RegisterTrigger`, using a stable id based on `WarId`.
The content builder resolves country names at presentation time with
`country_name.{CountryId}` and creates:

- a `tooltip-header` label from `hud.war.title_format`, with attacker and defender names supplied
  as format arguments;
- a `tooltip-effect-name` label from `hud.war.progress_format`, with progress formatted using a
  non-integer numeric format (`"G"` with invariant culture is sufficient for the bounded
  `[-100, 100]` model value) so values such as `-2.5` remain fractional.

Add both keys to `Assets/Localization/en.asset` and `Assets/Localization/ru.asset` with real
translations. `HUDDocument` remains the scene's single owner of `_loc.SetLocale`; its locale
handler closes any currently-open tooltip through `TooltipSystem.HideAll` and calls
`WarIconsView.Refresh`, so no old-language tooltip remains visible and the next presentation uses
the active locale. If a country-name lookup returns its own key, fall back to the country id
rather than showing a raw localization key.

The view orders from the already-sorted state list. It hides its root for an empty list and never
performs ECS queries, relevance decisions, or window-state changes.

### 4. HUD binding and progress-window DI seam

Extend `Assets/Scripts/Unity/UI/HUDDocument.cs` to:

- construct `WarIconsView` from the `war-icons` template instance;
- subscribe/unsubscribe `VisualState.WarIcons.PropertyChanged` in `OnEnable`/`OnDisable`;
- refresh the row immediately in `OnEnable`, on war-icon state changes, and on locale changes;
- forward the view's `WarId` callback to the injected window shell.

Add `Assets/Scripts/Unity/UI/WarProgressWindowDocument.cs` as a
`MonoBehaviour` with exactly the future navigation contract:

```csharp
public void Open(string warId) {
}
```

The method intentionally does not display UI, mutate `VisualState`, set modal state, or interpret
the id. Add a dedicated active `WarProgressWindowUI` root GameObject carrying only this component
(no `UIDocument`) to `Assets/Scenes/Map.unity`, register it with
`builder.RegisterComponentInHierarchy<WarProgressWindowDocument>()` in
`Assets/Scripts/Unity/DI/GameLifetimeScope.cs`, and inject it into `HUDDocument.Construct`.
`HUDDocument` calls `_warProgressWindow.Open(warId)` directly. This follows the existing
`LeaderboardWindowDocument` hierarchy-registration pattern while keeping the future shell
discoverable and avoiding an empty progress-window layout.

Use Unity MCP for the scene component addition when available, save `Map.unity`, refresh, and
require a clean Unity console. If Unity MCP is unavailable in the implementation environment,
follow the repository's documented scene-YAML fallback and explicitly report that visual
verification remains manual.

### 5. Build artifacts and verification

After core projection/tests are complete, run:

```text
dotnet test src/GlobalStrategy.Core.sln
dotnet build src/GlobalStrategy.Core.sln -c Release
```

The Release build refreshes the tracked DLLs under `Assets/Plugins/Core/`. Then let Unity import
the new scripts, template, texture, localization, and scene component; refresh Unity and check
the console for errors.

## Steps

### Agent Steps

- [ ] Add `WarIconEntryState`/`WarIconsState` and `VisualState.WarIcons` in
  `src/Game.Main/VisualState.cs`, plus content equality in `src/Game.Main/StateEquality.cs`.
- [ ] Add `src/Game.Main/WarIconsProjector.cs` with one-pass player-control aggregation,
  participant grouping, ordinal primary-side selection, malformed-war omission, relevance
  filtering, de-duplication, and `WarId` ordering.
- [ ] Add `VisualStateConverter.UpdateWarIcons` and call it after player-organization projection.
- [ ] Add `src/Game.Tests/WarIconsProjectorTests.cs` covering the pure projection and state
  notification behavior described below.
- [ ] Add the replaceable `Assets/Textures/Buttons/War/crossed_swords.png` placeholder by
  duplicating the existing political-lens sprite with matching import settings and a unique GUID.
- [ ] Add `Assets/UI/HUD/WarIcons/WarIcons.uxml` and `.uss`; compose/import them from
  `Assets/UI/HUD/HUD.uxml`.
- [ ] Refactor the war row and lens switcher under the shared `map-controls-panel` wrapper in
  `HUD.uxml`/`HUD.uss`, preserving the current lens anchor and collapsing the empty war row.
- [ ] Add `Assets/Scripts/Unity/UI/WarIconsView.cs` with stable `WarId`-keyed buttons,
  flag/icon composition, missing-flag fallback, localized tooltip construction, one-row spacing,
  and primary-press/inside-release routing.
- [ ] Add genuine English/Russian `hud.war.title_format` and `hud.war.progress_format` entries to
  `Assets/Localization/en.asset` and `Assets/Localization/ru.asset`.
- [ ] Add the intentionally empty `WarProgressWindowDocument.Open(string warId)` shell, register
  it in `GameLifetimeScope`, add a component-only `WarProgressWindowUI` root in `Map.unity`, and
  inject/forward it from `HUDDocument`.
- [ ] Wire `HUDDocument` lifecycle and locale refreshes to `VisualState.WarIcons` and
  `WarIconsView`.
- [ ] Run the core test suite and Release build; refresh Unity, save the scene, and require a clean
  console.

### User Steps

These steps require visual inspection or hands-on interaction in the Unity Editor.

#### 1. Empty and populated row layout

Enter Play mode with no qualifying war and confirm the lens switcher remains at its existing
bottom-left position with no blank row above it. Create one qualifying war, then several
disjoint qualifying wars, and confirm exactly one non-wrapping row appears immediately above the
lenses with one button per war and no lens-switching regression.

#### 2. Button composition and missing-flag fallback

Confirm each button reads visually as attacker flag → placeholder icon → defender flag. In a
scratch editor state, temporarily remove one participant's flag assignment from
`CountryVisualConfig`; confirm only that flag disappears while the icon, other flag, tooltip, and
click target remain usable, then restore the assignment.

#### 3. Relevance and live refresh

Give the player organization positive Control in only one participant country and confirm the war
appears. Reduce the aggregate to zero (and separately below zero if practical) and confirm the
button disappears without a scene reload. Restore positive Control, change progress, start/stop a
war, and confirm buttons update once without duplicating or changing stable order.

#### 4. Tooltip localization and fractional progress

Hover a war in English and Russian. Confirm both localized country names appear in the title, no
raw localization key is visible, and a value such as `-2.5` is displayed fractionally rather than
as `-2` or `-3`.

#### 5. Click routing contract

Place a debugger breakpoint (or temporary non-committed instrumentation) in
`WarProgressWindowDocument.Open`, primary-click each war button, and confirm the exact
corresponding `WarId` arrives. Confirm release outside the button and non-primary pointer buttons
do not invoke it; also confirm that pressing outside and releasing over the button does not invoke
it. No visible progress window is expected in this feature.

#### 6. Replaceable artwork

Replace `Assets/Textures/Buttons/War/crossed_swords.png` in place with the final owner-provided
art and confirm Unity retains the reference and button sizing without UXML/C# changes.

## Tests

Add `src/Game.Tests/WarIconsProjectorTests.cs` using synthetic `World` fixtures:

- no valid player organization produces an empty state;
- a war is excluded when every participant's aggregated player Control is absent, zero, or
  negative;
- positive aggregate Control on either attacker or defender includes the war;
- positive and negative `ControlEffect` rows for the same org/country are summed before the
  strict `> 0` check;
- player Control on several participants still produces exactly one entry for the `WarId`;
- additional attackers/defenders all affect relevance, while ordinal country-id ordering chooses
  exactly one deterministic primary attacker and defender;
- several qualifying wars are ordered by `WarId`, independent of creation/archetype order;
- progress is copied without integer rounding and a changed `WarProgress.Value` appears on the
  next projection without duplicating/reordering the entry;
- missing `WarProgress`, missing attacker, missing defender, empty ids, and orphan participant
  groups are omitted without throwing;
- stopping a war or removing the last positive participant Control removes it on the next
  projection;
- `WarIconsState.Set` does not raise `PropertyChanged` for identical entry content, but does for
  add/remove, primary-country, or progress changes.

Run all existing tests with `dotnet test src/GlobalStrategy.Core.sln`. The current repository has
no Unity-side UI Toolkit test harness, so row layout, sprite fallback, tooltip content, and pointer
routing are covered by the Unity checks in **User Steps**, followed by a clean console check.

## Constitution Check

Checked against `Docs/Constitution.md`.

**No conflicts found.**

- **Rendering.** No render pipeline, shader, material, or camera changes; the feature is UI
  Toolkit presentation only.
- **Game Logic.** War/control rules remain in existing ECS systems. Relevance and deterministic
  projection live in pure `src/Game.Main` code; Unity-side code only renders the resulting state
  and emits `WarId`.
- **Dependency Injection.** The progress-window shell is a hierarchy component registered and
  injected through the existing VContainer composition root. No lookup, static singleton, or
  manual service construction is added.
- **UI.** The row is UXML/USS plus the existing `HUDDocument` binding and a plain
  `WarIconsView`. The empty `WarProgressWindowDocument` is only the approved future navigation
  seam and intentionally has no UI surface yet. No Canvas/UGUI is introduced.
- **Planning and specification discipline.** This plan follows the approved
  `Docs/Specs/26_07_29_16_war-icons/spec.md` and gates implementation on owner review.
- **File organization.** The plan is beside its spec. Unity scripts remain in the existing
  `Assets/Scripts/Unity/UI/` feature assembly; `src` additions remain in `Game.Main` and
  `Game.Tests`; no new `.asmdef` is required.
- **Assembly structure and style.** Existing assemblies are reused. Event/state/projector code
  follows tabs, same-line braces, `_`-prefixed private members, and explicit public test seams
  rather than `InternalsVisibleTo`.
