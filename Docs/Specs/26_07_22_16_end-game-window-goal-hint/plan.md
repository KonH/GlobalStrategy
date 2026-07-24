# Plan: End Game Window and Goal Hint

## Spec

Source: `Docs/Specs/26_07_22_16_end-game-window-goal-hint/spec.md`.

**Intent.** Add a black full-screen end-game window that shows once the game completes — a
win/lose header naming the player organization, the frozen final organization leaderboard, and
a ten-row score-comparison block (the player inserted among nine calibrated predefined
entries) — plus a top-right "Win conditions" goal-hint panel in the organization-selection
scene that explains the currently configured completion objective(s) before play starts.

**Acceptance criteria (summarized, see spec.md for full legend-form list).**
- No end-game window while in progress; it appears exactly once, over a full-screen black
  backdrop, with a localized `{orgName} owns all the World!` / `{orgName} doomed...` header for
  win/lose respectively, and reappears identically for a loaded completed save without a new
  update.
- The final organization leaderboard reuses frozen `VisualState.Leaderboard` semantics
  (descending score, stable tie-break, sequential places, localized empty state).
- The comparison block inserts the player among exactly nine predefined entries, sorted
  descending with stable ties; predefined rows show name + score, no icon; the player row may
  show its emblem; missing/empty config still shows a usable player-only row; untranslated
  comparison names fall back to something readable.
- The nine comparator scores are produced by a deterministic calibration procedure (win and
  lose scenario via headless runner + debug commands) recording an achievable maximum, then
  linear 5%–120% thresholds by researched Google Trends popularity rank (least popular →
  lowest, most popular → highest); the nine identities are conspiracy-folklore figures/orgs,
  framed explicitly as folklore/claims, with dated cited evidence for the ranking.
- The window blocks map/UI pointer input while open, cannot be dismissed except via `Exit`
  (bounded `PointerUpEvent`) which returns to the main menu.
- The organization-selection scene shows a top-right `Win conditions` panel, visible without
  selecting an organization, that recursively flattens `any` groups of the configured
  completion condition into localized rows for the two currently supported leaf types
  (`total_control` as a percentage phrase, `full_control_countries` as an
  `X/available-country-count` phrase), signals when satisfying any one row is sufficient,
  handles empty/unsupported config with a localized unavailable message, and recomputes its
  country denominator from `CountryConfig.Countries` availability with no separately maintained
  total.
- Everything new is localized for English and Russian with no raw keys ever shown.

## Goal

Add the end-game presentation layer and the pre-game goal-hint panel as pure additive
projections over the already-implemented ECS completion/leaderboard state
(`Docs/Specs/26_07_22_11_win-lose-logic/`), without touching win/lose evaluation, thresholds,
tie-breaking, or existing leaderboard-window behavior. This plan covers: a small comparison
config extension + pure projector, a goal-hint config-driven projector, two new UI Toolkit
surfaces wired through VContainer and Unity MCP, new localization keys, a calibration skill
that derives the nine real comparison scores from the committed config, and cited Google Trends
research that selects and ranks the nine comparison identities.

## Approach

### 1. Comparison configuration (`src/Game.Configs`, `Assets/Configs/game_settings.json`)

Add to `src/Game.Configs/GameSettings.cs`:

```csharp
public class EndGameComparisonEntry {
	public string ComparisonElementId { get; set; } = "";
	public double Score { get; set; }
}
```

and `public List<EndGameComparisonEntry> EndGameComparisons { get; set; } = new List<EndGameComparisonEntry>();`
on `GameSettings`. Add the matching `endGameComparisons: []` array to
`Assets/Configs/game_settings.json` (camelCase, per `.claude/rules/unity/plugins.md`). The nine
real entries are populated later (Step 11 below) once calibration and research produce real
values — landing the schema now, empty, keeps the pure-projector tests in Step 6
implementation-agnostic of the not-yet-computed real numbers.

### 2. Expose `GameSettings` from `GameLogic`

`src/Game.Main/GameLogic.cs`'s constructor already does `var settings = context.GameSettings.Load();`
(local variable, line ~67) and reads several fields off it into other properties (`MaxControlPool`,
`BotFeatures`, etc.) but never keeps the instance itself. Add
`public GameSettings GameSettings { get; private set; }` and assign it from that same local
variable, following the exact pattern already used for `ResourceConfig`/`CharacterConfig`/etc.
Register it in `Assets/Scripts/Unity/DI/GameLifetimeScope.cs` alongside the other forwarded
config properties:

```csharp
builder.Register(c => c.Resolve<GameLogic>().GameSettings, Lifetime.Singleton);
```

This avoids `EndGameWindowDocument` deserializing `game_settings.json` a second time.

### 3. Shared score-format helper

`Assets/Scripts/Unity/UI/LeaderboardWindowView.cs` already owns `FormatScore`/`s_scoreFormat`
(thousands-grouped, rounded, invariant-ish `NumberFormatInfo`). Extract this into a small shared
static class `Assets/Scripts/Unity/UI/ScoreFormat.cs` (`GS.Unity.UI` namespace,
`public static string Format(double value)`), and update `LeaderboardWindowView` to call it. Both
the end-game leaderboard block and the comparison block reuse this helper — this is the "focused
shared row/score helper" the spec's Tech Notes call for, scoped to formatting only (flag lookup
stays inline per call site since comparator rows must omit it entirely).

### 4. Comparison projection (`src/Game.Main`)

Add to `src/Game.Main/VisualState.cs`, next to `LeaderboardEntryState`:

```csharp
public class EndGameComparisonRowState {
	public int Place { get; }
	public string ComparisonElementId { get; } // "" for the player row
	public bool IsPlayer { get; }
	public string DisplayName { get; }          // player org display name; "" for predefined rows (UI localizes by id)
	public double Score { get; }
	// constructor mirrors LeaderboardEntryState
}
```

Add a pure static `src/Game.Main/EndGameComparisonProjector.cs`:

```csharp
public static class EndGameComparisonProjector {
	public static List<EndGameComparisonRowState> Build(
		IReadOnlyList<EndGameComparisonEntry> configuredEntries,
		string playerOrgId, string playerDisplayName, double playerScore) { ... }
}
```

Behavior: build one row per configured entry (`IsPlayer = false`) plus one player row
(`IsPlayer = true`), sort descending by `Score`, tie-break by `ComparisonElementId` ordinal
(`""` sorts using a stable placeholder — use `IsPlayer` as a documented, deterministic
secondary key alongside id so the player row's tie-break is well-defined even though it has no
`ComparisonElementId`), assign 1-based `Place`. When `configuredEntries` is null/empty, return a
single-row player-only result — never a blank predefined row. This is called from
`EndGameWindowDocument`'s refresh (Unity-side), passing the injected `GameSettings.EndGameComparisons`,
`VisualState.PlayerOrganization`, and the player's `Score` looked up from
`VisualState.Leaderboard.Organizations` by matching `PlayerOrganization.OrgId` — not a new
converter pass, since both source states are already frozen correctly by the time the window is
visible.

### 5. Goal-hint projection (`src/Game.Main`)

Add to `src/Game.Main/VisualState.cs`:

```csharp
public enum WinConditionHintKind { TotalControl, FullControlCountries }

public class WinConditionHintRowState {
	public WinConditionHintKind Kind { get; }
	public double Value { get; }            // total_control: fraction (0.8); full_control_countries: threshold count
	public int AvailableCountryCount { get; } // only meaningful for FullControlCountries
}

public class WinConditionHintState {
	public bool IsAvailable { get; private set; }     // false => show localized unavailable message
	public bool IsAlternativeGroup { get; private set; } // true when 2+ rows, i.e. satisfying any one suffices
	public IReadOnlyList<WinConditionHintRowState> Rows { get; private set; } = Array.Empty<WinConditionHintRowState>();
	public void Set(bool isAvailable, bool isAlternativeGroup, List<WinConditionHintRowState> rows) { ... }
}
```

Add `public WinConditionHintState WinConditionHint { get; } = new WinConditionHintState();` to
`VisualState`. This state is unused by the Map scene's `GameLogic`/`VisualStateConverter` (it is
populated only by `SelectOrgLogic`, see Step 7) — consistent with `SelectedCountryState` and
other per-scene-only members already on the shared `VisualState` class.

Add a pure static `src/Game.Main/WinConditionHintProjector.cs`:

```csharp
public static class WinConditionHintProjector {
	public static (bool isAvailable, bool isAlternativeGroup, List<WinConditionHintRowState> rows) Build(
		CompletionConditionConfig? condition, int availableCountryCount) { ... }
}
```

Behavior: recursively walk `condition.Type == "any"`, flattening nested `any` groups into a
single ordered leaf list (configuration order preserved, depth-first). For each leaf, map
`"total_control"` → `WinConditionHintKind.TotalControl` (`Value = leaf.Value`) and
`"full_control_countries"` → `WinConditionHintKind.FullControlCountries`
(`Value = leaf.Value`, `AvailableCountryCount = availableCountryCount`); any other leaf type is
silently skipped (future-proofing per the spec's "adding a future supported leaf requires one
mapping case" note — an unrecognized type must not throw or blank the whole panel). If
`condition` is null, has no members, or every leaf is unsupported, return
`(isAvailable: false, isAlternativeGroup: false, rows: [])`. `isAlternativeGroup` is true
whenever the resulting row count is ≥ 2 (multiple leaves under a top-level/nested `any` means
any one suffices; a single supported leaf has no "alternative" framing to show).

`AvailableCountryCount` is computed by the caller from
`countryConfig.Countries.Count(c => c.IsAvailable)` (no denominator field added to `GameSettings`
or `CompletionConditionConfig`, per the spec's explicit exclusion).

### 6. Core tests + first `dotnet test` pass

Add `src/Game.Tests/EndGameComparisonProjectorTests.cs` and
`src/Game.Tests/WinConditionHintProjectorTests.cs` covering the cases listed under **Tests**
below, using synthetic fixtures (not the not-yet-calibrated real config). Run via the
`dotnet-test` skill against `src/GlobalStrategy.Core.sln`.

### 7. Calibration skill + harness support

Add `.claude/skills/end-game-score-calibration/SKILL.md`, matching this repo's existing skill
format (YAML frontmatter `name`/`description` + numbered instructions, see
`.claude/skills/dotnet-test/SKILL.md` for the convention). **Not** `.codex/skills/...` — that
path in the spec's Tech Notes belongs to the separate Codex automation pipeline
(`.codex/skills/`); this repo's own skill lookup is `.claude/skills/`.

The skill documents:
- The exact build/run command for a deterministic calibration pass against the real committed
  `Assets/Configs/*.json` (via `Program.BuildContext`/`FileConfig<T>`, the same config-loading
  path `Game.ConsoleRunner` already uses — not `TextAssetConfig`, which is Unity-only).
- Fixed seed, org set, and HQ/starting config taken from the committed config (no synthetic
  fixture — the calibration must reflect what ships).
- The debug-command sequence: push `DebugDiscoverAllCountriesCommand` once so all countries are
  visible/controllable, then push `ChangeControlCommand` deltas per tick for the target org
  until either the `total_control` or `full_control_countries` threshold from
  `GameSettings.CompletionCondition` is reached, calling `GameLogic.Update` each tick.
- **Win scenario**: drive the org whose final score will be recorded to the winning threshold.
  **Lose scenario**: drive a *different* org to the winning threshold while the scored org stays
  a participant but loses — this establishes the score a losing-but-participating player can
  reach, which may exceed the winner's score early in a short run and must be considered.
- Terminal assertion: poll `GameLogic.IsCompleted` (equivalently the `VisualState.GameCompletion.IsCompleted`
  projection) after each `Update` and stop once true, with a hard tick-count/timeout ceiling
  matching `HeadlessOptions.TimeoutSeconds`'s existing safety pattern in
  `src/Game.ConsoleRunner/HeadlessOptions.cs`.
- Score read-out: `ResourceQuery.GetValue(logic.World, orgId, ResourceDefinitions.OrgScore)` —
  the exact same query `HeadlessRunner.BuildOrgMetrics`/`OrgMetricsResult.Score` already uses, so
  no parallel score calculation is introduced.
- The higher of the two scenarios' recorded player score is the calibration maximum.
- Threshold generation: `factor(i) = 0.05 + i * (1.20 - 0.05) / 8` for `i = 0..8`, multiplied by
  the calibration maximum, rounded with the same explicit policy the committed config/display
  use (whole numbers, `MidpointRounding.AwayFromZero`, matching `ScoreFormat`/`FormatScore`'s
  existing rounding behavior from Step 3).
- Output paths under `.claude/skills/end-game-score-calibration/references/` and the update
  procedure to follow when config or score rules change and calibration must be rerun (stale
  values must be caught, not silently accepted).

Add the minimal runner support the skill needs: a `CalibrationRunner` class in
`src/Game.ConsoleRunner/` (new file) with a method taking a config dir, scenario
(`win`/`lose`), and target org id, reusing `Program.BuildContext` exactly like `HeadlessRunner`
does, and a new `Program.cs` CLI verb (e.g. `calibrate-end-game --config <dir> --scenario
win|lose --org <id> --seed <n> --output <path>`) that invokes it and writes a small JSON result
(final score, tick count, terminal date) to `--output`. This is a thin, deterministic driver —
no new gameplay behavior, no changes to `GameLogic`/`GameCompletionSystem`.

### 8. Run calibration + commit evidence

Actually run the skill's documented command for both scenarios against the real committed
config. Record the calibration maximum and both scenarios' full inputs/outputs (command lines,
seeds, final scores, tick counts) under
`.claude/skills/end-game-score-calibration/references/calibration_results.md` (or `.json` +
short `.md` summary).

### 9. Google Trends research for the nine comparison identities

Using `WebSearch`, research nine figures/organizations associated with "control the world"
conspiracy folklore, comparing worldwide Google Trends popularity. Produce a dated, cited
ranking (least → most popular) under
`.claude/skills/end-game-score-calibration/references/trends_research.md`, explicitly recording:
worldwide geography scope, search type (web search vs. a specific Trends category) and time
window, term-vs-topic choice per query, and the normalization/shared-anchor method used to make
otherwise-incomparable relative Trends samples comparable (Trends values are per-query relative
scores, 0–100, not absolute — combining raw values from unrelated single-term queries is
explicitly disallowed by the spec). Every description of a conspiracy claim in this file and in
the shipped localization (Step 18) must read as folklore/mythology/allegation framing, never as
stated fact.

### 10. Populate the real `endGameComparisons` config

With the calibration maximum (Step 8) and researched rank order (Step 9) now available, compute
the nine thresholds (`factor(i)` from Step 7's formula) and fill in
`Assets/Configs/game_settings.json`'s `endGameComparisons` array with real
`comparisonElementId`/`score` pairs, ordered by ascending rank-derived score to match the
research ordering. Re-run the `EndGameComparisonProjectorTests`/`WinConditionHintProjectorTests`
(still fixture-based, unaffected) plus a small new deterministic-output test/assertion (see
**Tests**) proving the shipped nine scores match `factor(i) * calibrationMaximum` under the
documented rounding policy — this is what lets a future stale rerun be caught mechanically
instead of only by a reviewer reading `references/`.

### 11. End-game UI Toolkit surface

Add `Assets/UI/Modal/EndGameWindow/EndGameWindow.uxml` and `.uss` (import
`Assets/UI/Shared/SharedStyles.uss` first, per `.claude/rules/unity/uitoolkit.md`). Structure:
full-screen opaque black root (not `.gs-blackfade`'s 40%-alpha overlay — the spec requires
"fully black"), a header `Label` for the win/lose sentence, a leaderboard rows container/`ScrollView`
reusing the row shape from `LeaderboardWindow.uxml`'s `leaderboard-row`/`leaderboard-list`
classes (place, flag, name, score; empty-state label), a second rows container for the
ten-row comparison block (place, name, score; no flag column except the player row, which reuses
`OrgVisualConfig.Find(orgId)?.flag`), and an `Exit` `Button`.

Add `Assets/Scripts/Unity/UI/EndGameWindowView.cs` (plain view class): constructor takes the
root `VisualElement`, `ILocalization`, `OrgVisualConfig`; exposes
`Refresh(GameCompletionState completion, LeaderboardState leaderboard, PlayerOrganizationState player, IReadOnlyList<EndGameComparisonEntry> comparisons)`. Internally calls
`EndGameComparisonProjector.Build(...)`, formats scores via `ScoreFormat.Format`, and sets the
header text from `end_game.result.win`/`end_game.result.lose` locale templates formatted with
the player org's display name as data (never string-concatenated).

Add `Assets/Scripts/Unity/UI/EndGameWindowDocument.cs` (binding `MonoBehaviour`,
`[RequireComponent(typeof(UIDocument))]`):
- `[Inject] void Construct(VisualState state, GameSettings gameSettings, ILocalization loc, OrgVisualConfig orgVisualConfig, SceneLoader sceneLoader)`.
- `Awake`: cache `UIDocument`, set `_doc.sortingOrder` to a value above `FlyTextNotifierDocument`'s
  default `1000` (e.g. `1100`, serialized `[SerializeField] int _sortingOrder = 1100;` per the
  project's existing pattern for that field).
- `Start`: query root elements, construct `EndGameWindowView`, hide root by default, call
  `RefreshTexts()`/initial `Refresh()` directly (per `.claude/rules/unity/localization.md` —
  do not rely on `PropertyChanged` alone for first sync).
- `OnEnable`/`OnDisable`: subscribe/unsubscribe `state.GameCompletion.PropertyChanged`,
  `state.Leaderboard.PropertyChanged`, `state.PlayerOrganization.PropertyChanged`,
  `state.Locale.PropertyChanged`, each triggering the same refresh handler.
- Refresh handler: if `!state.GameCompletion.IsCompleted`, keep root hidden and
  `ModalState.IsModalOpen` untouched (unless this document itself last opened it — mirror
  `LeaderboardWindowDocument`'s "only clear modal state if this document owns it" guard). Once
  completed, show root, set `ModalState.IsModalOpen = true`, and call `_view.Refresh(...)`. There
  is no hide path other than scene exit (`Exit`), per spec.
- `Exit` button: `PointerUpEvent` + left-button + `ContainsPoint` handler calling
  `_sceneLoader.LoadMainMenu()`. Do not use `Button.clicked`/`ClickEvent`
  (`.claude/rules/unity/uitoolkit.md`).

Register `builder.RegisterComponentInHierarchy<EndGameWindowDocument>();` in
`Assets/Scripts/Unity/DI/GameLifetimeScope.cs`. Add the `UIDocument` GameObject to
`Assets/Scenes/Map.unity` via Unity MCP (`manage_gameobject` + `manage_components` following the
existing modal-document pattern, e.g. `LeaderboardWindowDocument`'s GameObject), save the scene,
`refresh_unity`, and `read_console(types=["error"])`.

### 12. Goal-hint panel in `SelectCountry.uxml`

Extend `Assets/UI/Modal/SelectCountry/SelectCountry.uxml`/`.uss` with a new top-right panel,
independently positioned from the existing `info-panel` (which is left/bottom-anchored per its
current layout) — add a `goal-hint-panel` `VisualElement` with `.gs-panel` styling, a header
`Label` (`win_conditions-header`, text `Win conditions`), a rows container (`VisualElement`,
`goal-hint-rows`), an alternative-condition cue `Label` (`goal-hint-alternative-cue`, hidden
unless `IsAlternativeGroup`), and an empty-state `Label` (`goal-hint-empty`). Position via USS
`position: absolute; top/right` (see `.claude/rules/unity/uitoolkit.md`'s layout gotchas for
absolute-positioned-only wrapper height caveats — keep at least one relative child inside the
panel, e.g. the header, if rows use absolute positioning; in practice a plain flex column avoids
this entirely and is preferred).

### 13. `SelectOrgLogic` + `SelectCountryLifetimeScope` wiring

Extend `src/Game.Main/SelectOrgLogic.cs`'s constructor to accept
`IConfigSource<GameSettings> gameSettingsConfig`, load it once, compute
`availableCountryCount` from the already-loaded country config entries' `IsAvailable`, call
`WinConditionHintProjector.Build(settings.CompletionCondition, availableCountryCount)`, and set
`VisualState.WinConditionHint.Set(...)` once in the constructor (immutable for the scene's
lifetime — no per-frame recompute needed, matching `HqCountryIds`' one-time-build pattern already
in this class).

Extend `Assets/Scripts/Unity/DI/SelectCountryLifetimeScope.cs` with
`[SerializeField] TextAsset _gameSettingsAsset;`, build
`new TextAssetConfig<GameSettings>(_gameSettingsAsset)`, and pass it into the updated
`SelectOrgLogic` constructor call.

### 14. `SelectOrgDocument` — goal-hint rendering + input-wiring fix

Extend `Assets/Scripts/Unity/UI/SelectOrgDocument.cs`: query the new goal-hint elements in
`Start`, add a small helper (either inline in `SelectOrgDocument` or a plain
`GoalHintView` class if the row-building logic is non-trivial enough to warrant one) that renders
`VisualState.WinConditionHint.Rows` into `goal-hint-rows`, formatting each row via localized
templates:
- `TotalControl`: `select_org.win_conditions.total_control` formatted with the percentage
  (`Value * 100`, e.g. "Control 80% of the World").
- `FullControlCountries`: `select_org.win_conditions.full_control_countries` formatted with
  `(int)Value` and `AvailableCountryCount` (e.g. "Control completely at least 15/20 countries").

Show/hide `goal-hint-alternative-cue` from `IsAlternativeGroup`, and `goal-hint-empty` /rows
container from `IsAvailable`. This panel has no dynamic state after scene load (config is
immutable for the scene's lifetime) but must still refresh `RefreshTexts()`-style on locale
change like the rest of the document, since the localized phrase text itself changes.

While touching this file, replace the existing `btnBack.clicked += ...` and
`_btnStart.clicked += OnStartGame;` handlers with bounded `PointerUpEvent` handlers (left-button
+ `ContainsPoint`), per the spec's explicit incidental-fix requirement and
`.claude/rules/unity/uitoolkit.md`'s "never use `Button.clicked`" rule.

### 15. Scene wiring for `CountrySelection.unity`

Add the new `game_settings.json` `TextAsset` reference to the `SelectCountryLifetimeScope`
component instance in `Assets/Scenes/CountrySelection.unity` via Unity MCP
(`manage_components` `set_property`, or direct scene YAML edit per
`.claude/rules/unity/scenes.md`'s "new `[SerializeField]` field on existing scene MonoBehaviour"
section if MCP property-set doesn't resolve a `TextAsset` reference cleanly). Save, refresh,
check console.

### 16. Localization

Add to both `Assets/Localization/en.asset` and `Assets/Localization/ru.asset`:
- `end_game.result.win` / `end_game.result.lose` — format templates taking the org display name
  as `{0}` (e.g. `"{0} owns all the World!"` / `"{0} doomed..."`).
- `end_game.exit` — `Exit` button text.
- `end_game.leaderboard.empty` — reuse wording style from `leaderboard.empty`.
- `end_game.comparison.<comparisonElementId>` for all nine researched identities (Step 9/10) —
  the localized display name; description/framing text if the design calls for more than a
  name goes under `end_game.comparison.<id>.description`, written as folklore/claim framing.
- `select_org.win_conditions.header` — `Win conditions`.
- `select_org.win_conditions.alternative_cue` — e.g. "Any one of the following is enough to win".
- `select_org.win_conditions.empty` — unavailable-config fallback text.
- `select_org.win_conditions.total_control` — percentage-control phrase template.
- `select_org.win_conditions.full_control_countries` — `X/Y` countries phrase template.

Follow the existing locale-state-refresh flow (`.claude/rules/unity/localization.md`): only the
scene's always-visible document calls `_loc.SetLocale(...)`; other documents just call
`RefreshTexts()`. Format organization/numeric values as data inside templates, never by
concatenating localized fragments.

### 17. Final build + Unity verification

Run `dotnet test src/GlobalStrategy.Core.sln`, then
`dotnet build src/GlobalStrategy.Core.sln -c Release` (refreshes `Assets/Plugins/Core/`). Let
Unity import the new/changed UXML/USS/scripts, `refresh_unity`, `read_console(types=["error"])`.

## Steps

### Agent Steps

- [ ] Add `EndGameComparisonEntry` + `GameSettings.EndGameComparisons` to `src/Game.Configs/GameSettings.cs`, with an empty `endGameComparisons: []` placeholder in `Assets/Configs/game_settings.json`.
- [ ] Expose `public GameSettings GameSettings { get; private set; }` from `src/Game.Main/GameLogic.cs` and register it in `Assets/Scripts/Unity/DI/GameLifetimeScope.cs`.
- [ ] Extract `Assets/Scripts/Unity/UI/ScoreFormat.cs` from `LeaderboardWindowView.FormatScore`/`s_scoreFormat` and update `LeaderboardWindowView` to use it.
- [ ] Add `EndGameComparisonRowState` to `src/Game.Main/VisualState.cs` and `src/Game.Main/EndGameComparisonProjector.cs` (insertion, descending sort, ordinal tie-break, player-only fallback).
- [ ] Add `WinConditionHintKind`/`WinConditionHintRowState`/`WinConditionHintState` to `src/Game.Main/VisualState.cs` and `src/Game.Main/WinConditionHintProjector.cs` (recursive `any` flattening, `total_control`/`full_control_countries` mapping, unsupported-leaf skip, empty/null fallback).
- [ ] Add `src/Game.Tests/EndGameComparisonProjectorTests.cs` and `src/Game.Tests/WinConditionHintProjectorTests.cs` with synthetic fixtures; run `dotnet test src/GlobalStrategy.Core.sln` via the `dotnet-test` skill.
- [ ] Add `.claude/skills/end-game-score-calibration/SKILL.md` documenting the build command, fixed inputs, debug-command sequence, terminal assertion, score read-out, threshold formula/rounding, output paths, and rerun/update procedure.
- [ ] Add `src/Game.ConsoleRunner/CalibrationRunner.cs` and a new `Program.cs` `calibrate-end-game` CLI verb that drives win/lose scenarios via `DebugDiscoverAllCountriesCommand`/`ChangeControlCommand`/`GameLogic.Update` against the real committed config and writes a JSON result.
- [ ] Run the calibration skill for both scenarios against the committed config; commit the calibration maximum and full reproduction inputs/outputs under `.claude/skills/end-game-score-calibration/references/`.
- [ ] Research the nine conspiracy/mythical comparison identities' worldwide Google Trends popularity via `WebSearch`; commit the dated, cited ranking and methodology under `.claude/skills/end-game-score-calibration/references/trends_research.md`, framed as folklore/claims throughout.
- [ ] Compute the nine linear thresholds from the calibration maximum and researched rank, and populate `Assets/Configs/game_settings.json`'s `endGameComparisons` with the real `comparisonElementId`/`score` values; add a deterministic-output test asserting the shipped scores match `factor(i) * calibrationMaximum` under the documented rounding policy.
- [ ] Add `Assets/UI/Modal/EndGameWindow/EndGameWindow.uxml` and `.uss`, `Assets/Scripts/Unity/UI/EndGameWindowView.cs`, and `Assets/Scripts/Unity/UI/EndGameWindowDocument.cs` (sorting order above `1000`, `ModalState` hold, `PointerUpEvent` Exit).
- [ ] Register `EndGameWindowDocument` in `GameLifetimeScope`, add its `UIDocument` to `Assets/Scenes/Map.unity` via Unity MCP, save, refresh, check console.
- [ ] Add the top-right goal-hint panel markup to `Assets/UI/Modal/SelectCountry/SelectCountry.uxml`/`.uss`.
- [ ] Extend `src/Game.Main/SelectOrgLogic.cs` to accept `GameSettings` and build `WinConditionHintState` once at construction; extend `Assets/Scripts/Unity/DI/SelectCountryLifetimeScope.cs` with the new `game_settings.json` `TextAsset` field and updated constructor call.
- [ ] Extend `Assets/Scripts/Unity/UI/SelectOrgDocument.cs` to render goal-hint rows/alternative cue/empty state with locale-refresh support, and replace its `btn-back.clicked`/`_btnStart.clicked` handlers with bounded `PointerUpEvent` handlers.
- [ ] Wire the new `game_settings.json` `TextAsset` field onto `SelectCountryLifetimeScope` in `Assets/Scenes/CountrySelection.unity` via Unity MCP, save, refresh, check console.
- [ ] Add `end_game.*` and `select_org.win_conditions.*` localization keys (including the nine researched comparison names) to `Assets/Localization/en.asset` and `Assets/Localization/ru.asset`.
- [ ] Run `dotnet test src/GlobalStrategy.Core.sln`, then `dotnet build src/GlobalStrategy.Core.sln -c Release`; refresh Unity and confirm a clean console.

### User Steps

### 1. Visual QA — English locale

In Play mode, drive (or use the calibration debug-command sequence manually) a player win and a
player lose to terminal state and confirm the end-game window's header, leaderboard block, and
comparison block render correctly, the backdrop is fully opaque black, and `Exit` returns to the
main menu. Separately open the organization-selection scene and confirm the `Win conditions`
panel appears without selecting a country and reads correctly for the shipped completion config.

### 2. Visual QA — Russian locale

Repeat both checks above with the locale switched to Russian; confirm no raw localization keys
appear anywhere in either new surface.

### 3. Restored completed save

Load a save captured after completion and confirm the matching end-game window appears
immediately without requiring another gameplay update.

### 4. Empty/fallback comparison and goal-hint config

Temporarily point the scene's `game_settings.json` reference at (or hand-edit a scratch copy
with) an empty `endGameComparisons` array and an empty/absent `completionCondition`, and confirm
the end-game comparison block still shows a usable player-only row and the goal-hint panel shows
its localized unavailable message rather than blank space or an error. Revert afterward.

### 5. Pointer-blocking and Exit behavior

With the end-game window open, confirm clicks on the map/HUD underneath have no effect, and that
there is no way to dismiss the window except `Exit`.

### 6. Goal-hint panel layout review

Confirm the top-right goal-hint panel does not visually collide with the existing selection
`info-panel` or other `SelectCountry` UI at common resolutions, and that the alternative-group
cue only appears when the configured condition actually has multiple rows.

## Tests

Core (`src/Game.Tests`), all pure C#, run via `dotnet test src/GlobalStrategy.Core.sln`:

- **`EndGameComparisonProjectorTests`**: player inserted among N configured entries and sorted
  descending; ties broken deterministically and reproducibly across repeated calls; null/empty
  configured-entries list produces a single player-only row; scores/ids pass through unchanged;
  place numbers are 1-based and consecutive after sorting.
- **`WinConditionHintProjectorTests`**: single `total_control` leaf; single `full_control_countries`
  leaf with a supplied available-country count; nested `any` groups flatten to the correct leaf
  order regardless of nesting depth; an unsupported leaf type is skipped without failing the
  whole projection; `condition == null`, an empty `any`, and an `any` containing only unsupported
  leaves all produce `isAvailable == false` with zero rows; `isAlternativeGroup` is true only
  when 2+ rows result; the country denominator reflects only `CountryEntry.IsAvailable == true`
  entries and does not grow from unavailable ones.
- **Deterministic calibration-output test** (added in Step 10): given a fixed calibration
  maximum and the shipped rank order, asserts the nine `Assets/Configs/game_settings.json`
  `endGameComparisons` scores equal `factor(i) * calibrationMaximum` under the documented
  rounding policy, so a config/score-rule change that invalidates the committed values is caught
  by CI rather than only by a reviewer reading `references/`.
- Existing `LeaderboardEntryState`/`SortAndAssignPlaces`, `GameCompletion`/`GameCompletionSystem`,
  and `SelectOrgLogicTests` suites must continue passing unmodified — this feature must not
  change win/lose evaluation, tie-breaking, or `SelectOrgLogic`'s existing selection behavior.

Calibration skill:

- Run the skill's documented `calibrate-end-game` command for both `win` and `lose` scenarios
  against the real committed config; both must reach terminal state (`GameLogic.IsCompleted`)
  within the skill's timeout, and must reproduce the values already committed under
  `.claude/skills/end-game-score-calibration/references/` bit-for-bit (deterministic seed).

Unity verification (see **User Steps** above): English/Russian layouts in both
`CountrySelection.unity` and `Map.unity`, both terminal outcomes, a restored completed save,
empty-config fallbacks, pointer blocking, and `Exit`-to-main-menu behavior. Refresh Unity and
require a clean console after every saved scene change.

## Constitution Check

Checked against `Docs/Constitution.md`.

**No conflicts found — plan aligns with all principles.**

- **Rendering (Unity 6 + URP only).** Not touched — this plan is UI Toolkit + pure C# projection
  work only; no shaders, materials, or camera-stack changes.
- **Game Logic (ECS in `src/`).** No win/lose evaluation, threshold, or terminal-state logic is
  added or changed — `GameCompletion`/`GameCompletionSystem`/`OrganizationGameOutcome` from the
  already-implemented `26_07_22_11_win-lose-logic` feature remain the sole source of truth. The
  new `EndGameComparisonProjector`/`WinConditionHintProjector` are pure, stateless C# functions
  in `src/Game.Main` operating on already-decided config/state — no MonoBehaviour holds game
  rules, matching the same pattern `VisualStateConverter.UpdateLeaderboards` already uses.
- **Dependency Injection (VContainer only).** `EndGameWindowDocument`, the `GameSettings`
  forwarding registration, and the extended `SelectOrgLogic`/`SelectCountryLifetimeScope` wiring
  all go through the existing VContainer composition roots (`GameLifetimeScope`,
  `SelectCountryLifetimeScope`) using the same `RegisterComponentInHierarchy`/resolver-lambda
  patterns already in those files. No `new` for a singleton service, no `FindObjectOfType`, no
  static mutable singleton is introduced (`ModalState`/`SceneTransitionArgs` are pre-existing
  static state this plan reuses, not new ones it adds).
- **UI (UI Toolkit only).** Both new surfaces (`EndGameWindow`, the `SelectCountry` goal-hint
  panel) are UXML/USS + MonoBehaviour/View class pairs, matching the project's established
  Document/View split; no Canvas/UGUI is introduced.
- **Planning Discipline (plan before implement).** This plan is the required approved-plan
  artifact before any code/asset change begins.
- **Specification Discipline (spec before plan).** `Docs/Specs/26_07_22_16_end-game-window-goal-hint/spec.md`
  already exists and was approved before this plan was written.
- **File Organisation.** This plan lives at
  `Docs/Specs/26_07_22_16_end-game-window-goal-hint/plan.md`, beside its spec, per convention.
- **Assembly Structure (one `.asmdef` per feature folder).** New Unity scripts
  (`EndGameWindowDocument.cs`, `EndGameWindowView.cs`, `ScoreFormat.cs`) land in the existing
  `Assets/Scripts/Unity/UI/` feature folder and its existing `.asmdef` — no new folder/assembly
  is introduced. `src/` additions stay within the existing `Game.Configs`, `Game.Main`, and
  `Game.ConsoleRunner` projects.
- **C# Code Style.** All new/edited code will use tabs, same-line braces, `_`-prefixed private
  members, no redundant access modifiers, and fail-fast, contextual error handling
  (e.g. the calibration runner's timeout/ceiling and the projector's explicit unsupported-leaf
  skip rather than a silent generic catch).

Use the implement skill to start working on the plan or request changes.

## Automation Notes

A `full-env-headless` Ralph run (`.ralph/prd.md`) has no Unity Editor/MCP connection and cannot
research Google Trends data with automated verification, so the following plan steps were left
out of `.ralph/prd.md` entirely and still need a human/interactive pass:

- Extract `Assets/Scripts/Unity/UI/ScoreFormat.cs` from `LeaderboardWindowView.FormatScore`/
  `s_scoreFormat` and update `LeaderboardWindowView` to use it (Approach Step 3) — Unity-side
  script with no `src/` counterpart, no headless gate.
- Register `builder.Register(c => c.Resolve<GameLogic>().GameSettings, Lifetime.Singleton);` in
  `Assets/Scripts/Unity/DI/GameLifetimeScope.cs` (Approach Step 2, second half) — Unity-side DI
  registration, no headless gate.
- Research nine conspiracy-folklore figures/organizations' worldwide Google Trends popularity via
  `WebSearch` and commit the dated, cited ranking under
  `.claude/skills/end-game-score-calibration/references/trends_research.md` (Approach Step 9) —
  no code/config gate can verify research correctness.
- Populate the real `endGameComparisons` `comparisonElementId`/`score` values in
  `Assets/Configs/game_settings.json` using the calibration maximum and researched rank order
  (Approach Step 10) — blocked on the trends research above; only the threshold-formula
  arithmetic itself was covered by a headless test using a synthetic calibration maximum.
- Add `Assets/UI/Modal/EndGameWindow/EndGameWindow.uxml`/`.uss`,
  `Assets/Scripts/Unity/UI/EndGameWindowView.cs`, and
  `Assets/Scripts/Unity/UI/EndGameWindowDocument.cs` (Approach Step 11) — Unity assets and
  Unity-side scripts with no `src/` counterpart, no headless gate.
- Register `EndGameWindowDocument` in `GameLifetimeScope`, add its `UIDocument` to
  `Assets/Scenes/Map.unity` via Unity MCP, save, refresh, check console (Approach Step 11
  scene-wiring half) — requires Unity MCP.
- Add the top-right goal-hint panel markup to
  `Assets/UI/Modal/SelectCountry/SelectCountry.uxml`/`.uss` (Approach Step 12) — Unity asset, no
  headless gate.
- Extend `Assets/Scripts/Unity/DI/SelectCountryLifetimeScope.cs` with the new
  `game_settings.json` `TextAsset` field and updated `SelectOrgLogic` constructor call (Approach
  Step 13, second half) — Unity-side script with no `src/` counterpart, no headless gate.
- Extend `Assets/Scripts/Unity/UI/SelectOrgDocument.cs` to render goal-hint rows/alternative
  cue/empty state and replace its `btn-back.clicked`/`_btnStart.clicked` handlers with bounded
  `PointerUpEvent` handlers (Approach Step 14) — Unity-side script with no `src/` counterpart, no
  headless gate.
- Wire the new `game_settings.json` `TextAsset` field onto `SelectCountryLifetimeScope` in
  `Assets/Scenes/CountrySelection.unity` via Unity MCP, save, refresh, check console (Approach
  Step 15) — requires Unity MCP.
- Add the nine `end_game.comparison.<comparisonElementId>` localization keys (and any
  description/framing text) to `Assets/Localization/en.asset`/`ru.asset` (part of Approach Step
  16) — blocked on the trends research above; the other `end_game.*`/`select_org.win_conditions.*`
  keys not tied to the nine identities were added headlessly.
- Unity import/refresh/console verification of all new/changed UXML/USS/scripts (Approach Step
  17, Unity half) — requires the Unity Editor.
- All six **User Steps** (visual QA in English and Russian, restored completed save, empty/
  fallback config, pointer-blocking and Exit behavior, goal-hint panel layout review) — require
  Play mode and human visual judgment.
