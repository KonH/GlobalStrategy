# Plan: Action Log UI

## Spec

As a player, I want a persistent, scrolling log of important game-logic events (discoveries, control gains, opinion gains, new character appointments) visible on the HUD at all times, so I can follow the consequences of my own and rival organizations' actions.

Four line formats, all driven by observing existing state increases (not by hooking specific commands):
1. `<OrganizationDisplayName> discovered <CountryDisplayName>`
2. `<OrganizationDisplayName> increased control in <CountryDisplayName> to +<N>` (`N` = new resulting total, not delta)
3. `<OrganizationDisplayName> increased <CharacterRoleDisplayName> <CharacterDisplayName> opinion in <CountryDisplayName> to +<N>` (`N` = new resulting total, not delta)
4. `New <CharacterRoleDisplayName> in <CountryDisplayName> - <CharacterDisplayName>` (country-government role) **or** `New <CharacterRoleDisplayName> in <OrganizationDisplayName> - <CharacterDisplayName>` (org role) — exactly one variant renders, chosen by whether the character's `CountryId` or `OrgId` is set.

Acceptance criteria highlights:
- Numbers always render with exactly one decimal digit (`:F1`-style, e.g. `+3.5`, never `+3` or `+3.50`).
- Country/org display names in a line are bold and colored with that entity's existing per-entity color; role names are bold default-white; everything else (connector words, character names) is default white-with-shadow, unstyled.
- Panel shares `HUDPanelSettings.asset`, `sortingOrder < 1000` (below fly-text), not modal, doesn't block clicks.
- Panel top edge tracks the live rendered bottom of `.top-right-panel` (+ small gap); panel bottom edge is anchored at a **fixed** reserved offset representative of the bottom-bar panel's typical height — this offset never moves when the bottom-bar panel opens/closes.
- Panel width = `1.5 × W` where `W` is `.top-right-panel`'s live rendered width; right edge flush at `right: 6px`.
- Content is bottom-aligned and grows upward; overflow entries scroll off the top (clipped, not truncated); long lines wrap, never truncate/scroll horizontally.
- New entries fade in (short); entries evicted past `gameLog.maxLogEntries` fade out (longer) before removal.
- New `gameLog` config block on `GameSettings`: `includePlayerActions` (bool, default `true`), `maxLogEntries` (int, default `12`). When `includePlayerActions` is `false`, entries whose acting org matches the player's org are suppressed (never queued); AI-org entries and country-role character entries (no acting org) are always unaffected.
- No save persistence — the log and its diff baselines start empty/fresh every session; baselines are established from the freshly-loaded state on the first tick, without emitting a flood of "discovery" catch-up lines.
- A new `.claude/commands/*.md` skill walks a contributor through proposing a new log line type (format, locale keys, wiring point) without implementing it.

Out of scope: resource/gold/score log lines, province-ownership log lines, log entry click-to-navigate, sound effects, fly-text reuse, log filtering/search, additional `gameLog` settings beyond the two listed, save persistence.

## Goal

Add a persistent, non-modal HUD panel that surfaces the four event types above as a bottom-aligned, upward-growing, fading scroll log, backed by a new diff-detecting `GameLogState` in `src/Game.Main` that observes existing ECS state (no new game-logic systems, no new commands) and a new `gameLog` settings block.

## Approach

### Research findings that shape this design

- **Control**'s running total is never stored as a single aggregate component — it is always the sum of `ControlEffect.Value` for a given `(OrgId, CountryId)` pair (see `CreateActionEffectSystem.GetTotalControlInCountry`, `ControlSystem`, `VisualStateConverter.UpdateOrgMap`/`BuildControlIncomesForOrg`, all of which independently re-sum `ControlEffect` archetypes). The log's diff logic does its own single-pass sum per tick into a `Dictionary<(orgId,countryId), int>` — this matches the existing pattern exactly, no new aggregate component is introduced.
- **Opinion** has no `CharacterOpinion` component. It is a generic `Resource{ResourceId="opinion_{orgId}", OwnerId=charId, OwnerType=Character}` (`CreateActionEffectSystem.EnsureOpinionResource`), with monthly decay applied via a `ResourceEffect`. `VisualStateConverter.UpdateCharacters` only tracks opinion for the **selected country's characters** and only for the **player's own org** (`playerOrgId` param) — insufficient for the log, which must observe opinion changes from **any** org toward **any** character. The log's diff logic does its own full scan of `Resource` entities whose `ResourceId` starts with `"opinion_"` and `OwnerType == Character`, keyed by `(orgId, charId)`.
- `CreateActionEffectSystem.GetTargetCharacterByCountryAndRole` proves opinion effects only ever target a character with `CountryId` set (country-government role characters) — org-role characters (Master/Agent) never receive `opinion_*` resources today. So opinion log lines always have a valid `<CountryDisplayName>` via `Character.CountryId`.
- **Discovery**: `VisualStateConverter.UpdateDiscoveredCountries` already diffs one org's (`viewOrgId` = player org) discoveries frame-to-frame into `DiscoveredCountriesState.RecentlyDiscovered` (a single string). This is scoped to one org and holds only the latest one — not reusable as-is. The log needs a diff **per org**, so it maintains its own `Dictionary<orgId, HashSet<countryId>>` baseline, independent of `_previousDiscoveredIds`.
- **New character in role**: confirmed via `GameLogic.CycleOrgCharacterSlot`/`CycleCountryCharacter`/`ApplyDebugDropCharacter` — cycling always destroys the old `Character` entity and creates a new one with a **different** `CharacterId`; dropping clears to `""` and never itself counts as "new". This means a simple "current occupant differs from baseline and is non-empty" diff, keyed by `(orgId, roleId, slotIndex)` for org roles (from `CharacterSlot.CharacterId`) and `(countryId, roleId)` for country roles (from `Character.CharacterId` where `CountryId == countryId && RoleId == roleId`; country roles have exactly one occupant, no slot index), correctly captures "new occupant" regardless of whether the trigger was `ApplyDebugCycleCharacter` or a future real system — satisfying the spec's "debug command still logs" criterion for free.
- `InitSystem.CreateCharacterEntities`/`CreateOrgCharacterEntities` seed all initial characters (and `InitSystem.Update`'s other seeding) **before** `VisualStateConverter.Update(...)` is first called within the same `GameLogic.Update()` tick (`InitSystem.Update` runs first, `_visualStateConverter.Update(...)` runs later in the same method). So establishing the log's baselines on the very first `UpdateGameLog` call (without emitting) — mirroring the save/load "start fresh" requirement — also naturally absorbs the initial seed with zero flood, for both a brand-new game and a freshly loaded save.
- Character display names are resolved elsewhere (`CharactersView.cs`) as `string.Join(" ", entry.NamePartKeys.Select(_loc.Get))`. The log DTO carries `NamePartKeys` verbatim (copied at diff time) rather than a character id to re-look-up later — the underlying `Character` entity may already have been replaced (re-cycled) by the time the UI renders/re-renders the entry, so the DTO must be a self-contained snapshot.
- `CountryVisualEntry.color` / `OrgVisualEntry.color` (`Assets/Scripts/Unity/Map/Config/`) are the only existing "per-entity name color" properties in the project (currently used only for map fill) — reused here for bold name coloring, per the spec's explicit invitation to pick an existing color source. `CharacterVisualEntry` has no color field (portrait only), confirming role/character names stay uncolored by design, not by omission.
- Org display name locale key prefix is `organization_name.{orgId}` (confirmed in `Assets/Localization/en.asset`), not `org_name.*`.
- `HUDDocument` is the existing binding MonoBehaviour for `hud-root`, already composing `CountryInfoView`/`PlayerOrgView`/`TimeView`/`LensSwitcherView`/`OrgLensCountryView` as plain view classes queried from its own `UIDocument.rootVisualElement`, and subscribing per-substate `PropertyChanged` handlers in `OnEnable`/`OnDisable`. The action log follows this exact composition (a new `ActionLogView` instantiated in `HUDDocument.Start()`) rather than a second standalone `UIDocument` GameObject (like `FlyTextUI`) — because the panel's positioning formulas need to query `.top-right-panel`'s live geometry, which only exists inside `HUDDocument`'s own document tree; a separate `UIDocument` would have no direct reference to that element.

### Data model (`src/Game.Main/VisualState.cs`)

```csharp
public enum GameLogEntryKind {
	Discovery,
	Control,
	Opinion,
	NewCharacter
}

public class GameLogEntry {
	public long SequenceId { get; }          // monotonic, for UI-side identity/diffing
	public GameLogEntryKind Kind { get; }
	public string OrgId { get; }             // acting org; "" for the country-role NewCharacter variant
	public string CountryId { get; }         // target/home country; "" when not applicable
	public string CharacterId { get; }
	public string RoleId { get; }
	public string[] NamePartKeys { get; }    // snapshot, not a re-lookup key
	public double Value { get; }             // new resulting total (Control/Opinion only)
	public bool IsOrgRole { get; }           // NewCharacter only: true = OrgId set/CountryId empty

	public GameLogEntry(long sequenceId, GameLogEntryKind kind, string orgId, string countryId,
		string characterId, string roleId, string[] namePartKeys, double value, bool isOrgRole) { ... }
}

public class GameLogState : INotifyPropertyChanged {
	public event PropertyChangedEventHandler? PropertyChanged;
	public IReadOnlyList<GameLogEntry> Entries { get; private set; } = Array.Empty<GameLogEntry>();
	public void Set(List<GameLogEntry> entries) {
		Entries = entries;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
	}
}
```
Add `public GameLogState GameLog { get; } = new GameLogState();` to `VisualState`.

### Diff logic (`src/Game.Main/VisualStateConverter.cs`)

New fields:
```csharp
readonly List<GameLogEntry> _gameLogEntries = new();
readonly Dictionary<(string orgId, string countryId), int> _controlBaseline = new();
readonly Dictionary<(string orgId, string charId), int> _opinionBaseline = new();
readonly Dictionary<string, HashSet<string>> _discoveryBaselineByOrg = new();
readonly Dictionary<(string ownerId, string roleId, int slotIndex), string> _slotBaseline = new(); // org roles
readonly Dictionary<(string countryId, string roleId), string> _countryRoleBaseline = new();
long _nextGameLogSequenceId = 1;
bool _gameLogBaselineInitialized;
readonly bool _gameLogIncludePlayerActions;
readonly int _gameLogMaxEntries;
```
Constructor gains two params (defaults matching spec defaults, so the one existing call site in `GameLogic` is the only required change and no test/caller breaks):
```csharp
internal VisualStateConverter(VisualState state, ActionConfig? actionConfig = null,
	bool gameLogIncludePlayerActions = true, int gameLogMaxEntries = 12) { ... }
```

`Update(...)` gains a call `UpdateGameLog(world, orgEntity);` placed **after** `UpdatePlayerOrganization` (so `_state.PlayerOrganization.OrgId` is already current for the `includePlayerActions` comparison) and after `UpdateCharacters`/`UpdateOrgCharacters` is unnecessary — `UpdateGameLog` does its own independent scans, it does not depend on those methods' output.

`UpdateGameLog` outline:
1. Build this-tick snapshots in four single-pass scans (mirroring existing archetype-iteration style elsewhere in this file):
   - `currentControl`: sum `ControlEffect.Value` grouped by `(OrgId, CountryId)`.
   - `currentOpinion` + `charLookup`: scan `Character` archetype once into `charLookup[charId] = (RoleId, CountryId, OrgId, NamePartKeys)`; scan `Resource`+`ResourceOwner` (`OwnerType.Character`) for `ResourceId` starting `"opinion_"`, extract `orgId = resourceId.Substring("opinion_".Length)`, clamp value to `[-100,100]` (matching `UpdateCharacters`' existing clamp) into `currentOpinion[(orgId, charId)]`.
   - `currentDiscovery`: scan `DiscoveredCountry`, group into `Dictionary<orgId, HashSet<countryId>>`.
   - `currentSlots` (org roles): scan `CharacterSlot` into `(OwnerId, RoleId, SlotIndex) -> CharacterId`.
   - `currentCountryRoles`: from the same `charLookup` pass, for entries with non-empty `CountryId`, key `(CountryId, RoleId) -> CharacterId`.
2. If `!_gameLogBaselineInitialized`: copy all four "current" snapshots directly into the baseline dictionaries, set the flag, **emit nothing**, return.
3. Otherwise, diff each snapshot against its baseline; for every key where the new value is "greater"/"newly non-empty and different" (see per-kind rule below), construct a `GameLogEntry`, **unless** it is suppressed by `includePlayerActions` (below); always overwrite the baseline entry to the new current value regardless of suppression or direction (so decay/decrease is tracked silently and the next real increase compares against the true last-known value, correctly yielding the new resulting total — verified against `ResourceEffect`'s monthly opinion decay, which only ever decreases, so no spurious emits during pure decay).
   - Control: emit when `current > baseline` (baseline missing = `0`). `Value = current`.
   - Opinion: emit when `current > baseline` (baseline missing = `0`). `Value = current`. Resolve `RoleId`/`CountryId`/`NamePartKeys` from `charLookup[charId]`; skip (no entry, but still update baseline) if the character is no longer present.
   - Discovery: emit when a `countryId` is present in `currentDiscovery[orgId]` but absent from `_discoveryBaselineByOrg[orgId]` (missing org key = empty set).
   - NewCharacter (org roles): emit when `current != baseline` (baseline missing = `""`) **and** `current != ""`.
   - NewCharacter (country roles): same rule, keyed by `(countryId, roleId)`, `IsOrgRole = false`, `OrgId = ""`.
4. `includePlayerActions` suppression: for entries with `OrgId` set (Discovery/Control/Opinion/NewCharacter-org-role), suppress (do not construct/append) when `!_gameLogIncludePlayerActions && orgId == _state.PlayerOrganization.OrgId`. NewCharacter country-role entries (`OrgId == ""`) are never suppressed, per spec.
5. Append all newly-constructed entries (each assigned `_nextGameLogSequenceId++`) to `_gameLogEntries`; while `_gameLogEntries.Count > _gameLogMaxEntries`, remove at index `0` (oldest-first eviction).
6. If any entries were appended or evicted this tick, call `_state.GameLog.Set(new List<GameLogEntry>(_gameLogEntries))` (defensive copy, matching the `List<>` pass-by-reference pattern already used by other `Set(...)` calls in this file, e.g. `OrgMap.Set`).

### `GameLogic` wiring (`src/Game.Main/GameLogic.cs`)

In the constructor, after `var settings = context.GameSettings.Load();`:
```csharp
_visualStateConverter = new VisualStateConverter(VisualState, _actionConfig,
	settings.GameLog.IncludePlayerActions, settings.GameLog.MaxLogEntries);
```
(Move the `_visualStateConverter = new VisualStateConverter(...)` line, currently constructed one line before `settings` is loaded, to after `var settings = context.GameSettings.Load();` — a small reordering, no other field depends on this ordering.)

### `gameLog` config block (`src/Game.Configs/`)

New file `src/Game.Configs/GameLogSettings.cs` (one type per file, matching every other config class in this folder):
```csharp
namespace GS.Game.Configs {
	public class GameLogSettings {
		public bool IncludePlayerActions { get; set; } = true;
		public int MaxLogEntries { get; set; } = 12;
	}
}
```
Add to `GameSettings.cs`:
```csharp
public GameLogSettings GameLog { get; set; } = new GameLogSettings();
```
Newtonsoft.Json deserializes nested POCOs by default (no extra converter needed, same as the existing `SpeedMultipliers int[]`); camelCase property matching is automatic per `.claude/rules/unity/plugins.md`.

Update `Assets/Configs/game_settings.json`:
```json
{
  "startYear": 1880,
  "speedMultipliers": [1, 24, 720],
  "defaultLocale": "en",
  "autoSaveInterval": "monthly",
  "populationGrowthPercentPerMonth": 0.075,
  "countryScoreCoefficient": 0.0001,
  "botActionLogRetentionCap": 500,
  "gameLog": {
    "includePlayerActions": true,
    "maxLogEntries": 12
  }
}
```

### Locale keys

Four format templates (reusing `{0}`/`{1}`/... `string.Format` placeholders, matching the `ILocalization.Get(key)` + `string.Format` convention from `FlyTextNotifierDocument`). Connector words live in the locale string (translatable); the Unity-side view wraps only the *name*/*role* segments in Unity rich-text tags (`<b>`, `<color=#RRGGBB>`) before substitution — a single `Label` with `enableRichText = true` renders the whole line, which is what allows per-segment bold/color without splitting the line into multiple elements (and keeps wrapping/no-truncation trivial, since it's still one text blob).

The two `NewCharacter` variants (country-role vs org-role) share **one** template — the only difference between them is which id/color feeds `{1}`, a C#-side concern, not a translation concern:

| Key | English value |
|---|---|
| `game_log.discovered_format` | `{0} discovered {1}` |
| `game_log.control_increased_format` | `{0} increased control in {1} to {2}` |
| `game_log.opinion_increased_format` | `{0} increased {1} {2} opinion in {3} to {4}` |
| `game_log.new_character_format` | `New {0} in {1} - {2}` |

Russian equivalents (added to `ru.asset` alongside `en.asset`, following existing localization workflow — connector words only, `{n}` placeholders unchanged):
- `game_log.discovered_format` → `{0} обнаружил {1}`
- `game_log.control_increased_format` → `{0} увеличил контроль в {1} до {2}`
- `game_log.opinion_increased_format` → `{0} повысил мнение {1} {2} в {3} до {4}`
- `game_log.new_character_format` → `Новый {0} в {1} - {2}`

Existing keys reused, not newly added: `country_name.{countryId}`, `organization_name.{orgId}`, `character.role.{roleId}.name`.

### Line composition (Unity side)

For each `GameLogEntry`, the view builds the rich-text string, e.g. for `Control`:
```csharp
string orgName = WrapColored(_loc.Get($"organization_name.{entry.OrgId}"), _orgVisualConfig.Find(entry.OrgId)?.color);
string countryName = WrapColored(_loc.Get($"country_name.{entry.CountryId}"), _countryVisualConfig.Find(entry.CountryId)?.color);
string valueText = "+" + entry.Value.ToString("F1", CultureInfo.InvariantCulture);
string line = string.Format(_loc.Get("game_log.control_increased_format"), orgName, countryName, valueText);

static string WrapColored(string text, Color? color) {
	string hex = ColorUtility.ToHtmlStringRGB(color ?? Color.white);
	return $"<b><color=#{hex}>{text}</color></b>";
}
```
Role names use `$"<b>{_loc.Get($\"character.role.{entry.RoleId}.name\")}</b>"` (bold, no color tag → inherits the label's default white). Character display names use the existing `string.Join(" ", entry.NamePartKeys.Select(_loc.Get))` pattern verbatim, unwrapped.

### UI: new template (`Assets/UI/HUD/ActionLog/`)

`ActionLog.uxml` — a template, following the `CountryInfo`/`Time`/`LensSwitcher` convention:
```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <ui:Style src="project://database/Assets/UI/Shared/SharedStyles.uss"/>
    <ui:Style src="project://database/Assets/UI/HUD/ActionLog/ActionLog.uss"/>
    <ui:VisualElement name="action-log-root" picking-mode="Ignore" class="action-log-root">
        <ui:VisualElement name="action-log-content" picking-mode="Ignore" class="action-log-content"/>
    </ui:VisualElement>
</ui:UXML>
```
`ActionLog.uss`:
```css
.action-log-root {
    position: absolute;
    right: 6px;
    overflow: hidden;
}

.action-log-content {
    flex-direction: column;
    justify-content: flex-end;
    height: 100%;
    width: 100%;
}

.action-log-entry {
    white-space: normal;
    margin-top: 4px;
}
```
`.action-log-entry` gets `.gs-content` added alongside it in C# (shared typography/shadow class), per the shared-UI-kit rule — no color/font redefinition in the feature USS. Initial `top`/`height`/`width` are left unset in USS (content-driven zero size until the C# positioning callback runs on the first `GeometryChangedEvent` from `.top-right-panel` — same "set once, adjust after layout" gotcha as the existing tooltip-positioning pattern) with a safe inline fallback set once in code at construction time to avoid a zero-size flash.

Add to `HUD.uxml` (alongside the other `<ui:Template>`/`<ui:Instance>` pairs):
```xml
<ui:Template name="ActionLog" src="project://database/Assets/UI/HUD/ActionLog/ActionLog.uxml"/>
...
<ui:Instance template="ActionLog" name="action-log" class="action-log-panel"/>
```
`sortingOrder` is inherited from the shared `HUDDocument`'s single `UIDocument` (`sortingOrder: 0`, same as every other HUD panel) — no per-element sorting concept applies here since it's one Label tree inside the existing HUD document, well below the separate `FlyTextUI` document's `1000`. This trivially satisfies "sortingOrder < 1000, shares HUDPanelSettings."

### `ActionLogView` (`Assets/Scripts/Unity/UI/ActionLogView.cs`)

Plain C# view class (not a MonoBehaviour), instantiated in `HUDDocument.Start()`. Diverges from the project's usual "full-rebuild `Refresh()`" view pattern (documented in `.claude/rules/unity/uitoolkit.md`) because entries must independently fade in (arriving) and fade out (evicted) rather than being cleared/rebuilt wholesale — this is a new, intentionally different pattern for accumulating/animating lists, worth calling out since it is likely to recur for future log-like UI.

```csharp
class ActionLogView {
	const float FadeInSeconds = 0.25f;
	const float FadeOutSeconds = 0.6f;
	const float TopGapPx = 6f;
	const float BottomReservedOffsetPx = 160f; // representative closed-state height of the bottom-bar panel
	const float WidthMultiplier = 1.5f;
	const float RightPx = 6f;

	readonly VisualElement _root;
	readonly VisualElement _content;
	readonly VisualElement _topRightPanel;
	readonly VisualElement _hudRoot;
	readonly ILocalization _loc;
	readonly CountryVisualConfig _countryVisualConfig;
	readonly OrgVisualConfig _orgVisualConfig;
	readonly Dictionary<long, Label> _rendered = new();

	public ActionLogView(VisualElement hudRoot, VisualElement root, VisualElement topRightPanel,
		ILocalization loc, CountryVisualConfig countryVisualConfig, OrgVisualConfig orgVisualConfig) {
		_hudRoot = hudRoot;
		_root = root;
		_topRightPanel = topRightPanel;
		_loc = loc;
		_countryVisualConfig = countryVisualConfig;
		_orgVisualConfig = orgVisualConfig;
		_content = root.Q<VisualElement>("action-log-content");
		_root.style.bottom = BottomReservedOffsetPx;
		_topRightPanel.RegisterCallback<GeometryChangedEvent>(_ => RepositionAndResize());
		RepositionAndResize();
	}

	void RepositionAndResize() {
		var hudBound = _hudRoot.worldBound;
		var trBound = _topRightPanel.worldBound;
		float width = trBound.width * WidthMultiplier;
		_root.style.width = width;
		_root.style.right = RightPx;
		_root.style.top = (trBound.yMax - hudBound.yMin) + TopGapPx;
	}

	public void Refresh(GameLogState state) {
		var currentIds = new HashSet<long>();
		foreach (var entry in state.Entries) {
			currentIds.Add(entry.SequenceId);
			if (_rendered.ContainsKey(entry.SequenceId)) { continue; }
			var label = BuildLabel(entry);
			_content.Add(label);
			_rendered[entry.SequenceId] = label;
			label.style.opacity = 0f;
			label.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("opacity") };
			label.style.transitionDuration = new List<TimeValue> { new TimeValue(FadeInSeconds, TimeUnit.Second) };
			label.schedule.Execute(() => label.style.opacity = 1f).ExecuteLater(20);
		}
		var toEvict = new List<long>();
		foreach (var id in _rendered.Keys) {
			if (!currentIds.Contains(id)) { toEvict.Add(id); }
		}
		foreach (var id in toEvict) {
			var label = _rendered[id];
			_rendered.Remove(id);
			label.style.transitionDuration = new List<TimeValue> { new TimeValue(FadeOutSeconds, TimeUnit.Second) };
			label.style.opacity = 0f;
			label.schedule.Execute(() => label.RemoveFromHierarchy()).ExecuteLater((long)(FadeOutSeconds * 1000));
		}
	}

	Label BuildLabel(GameLogEntry entry) {
		string text = entry.Kind switch {
			GameLogEntryKind.Discovery => BuildDiscoveryLine(entry),
			GameLogEntryKind.Control => BuildControlLine(entry),
			GameLogEntryKind.Opinion => BuildOpinionLine(entry),
			GameLogEntryKind.NewCharacter => BuildNewCharacterLine(entry),
			_ => ""
		};
		var label = new Label(text) { enableRichText = true };
		label.AddToClassList("gs-content");
		label.AddToClassList("action-log-entry");
		return label;
	}
	// BuildDiscoveryLine / BuildControlLine / BuildOpinionLine / BuildNewCharacterLine — per Line composition section above.
}
```
`RepositionAndResize` computes `top` as the top-right-panel's world-space bottom edge minus the HUD root's world-space top edge (both from `worldBound`, converting screen-space into hud-root-relative coordinates), following the exact `worldBound`-conversion technique already documented for tooltip positioning in `.claude/rules/unity/uitoolkit.md` — `bottom` stays a fixed constant per style assignment in the constructor and is never touched again, guaranteeing it never shifts when the bottom-bar panel toggles visibility (that panel is a sibling, not a layout ancestor, so its `display: none` never affects the action log panel's geometry regardless).

### `HUDDocument` wiring

In `Awake()` or `Start()` (following the existing convention that `rootVisualElement` access is deferred to `Start()`):
```csharp
_actionLog = new ActionLogView(_root, _root.Q("action-log"), _root.Q("top-right-panel"), _loc, _countryVisualConfig, _orgVisualConfig);
```
In `OnEnable()`/`OnDisable()`:
```csharp
_state.GameLog.PropertyChanged += HandleGameLogChanged;   // OnEnable
_state.GameLog.PropertyChanged -= HandleGameLogChanged;   // OnDisable
```
```csharp
void HandleGameLogChanged(object sender, PropertyChangedEventArgs e) => _actionLog?.Refresh(_state.GameLog);
```
Also call `_actionLog?.Refresh(_state.GameLog);` once in `OnEnable()` (alongside the other immediate-sync calls already there, e.g. `_timeView.Refresh(_state.Time);`) so a document re-enable doesn't lose already-emitted entries.

No `GameLifetimeScope` changes are needed beyond what already exists — `HUDDocument` already receives `CountryVisualConfig`/`OrgVisualConfig`/`ILocalization` via its existing `[Inject] Construct(...)` signature; the action log reuses those same injected references rather than adding new registrations.

### Menu scenes

Confirmed out of scope: the spec frames this entirely as a "HUD"/in-game feature (all four event types only ever occur mid-game), and `MainMenu.unity`/`CountrySelection.unity` have no `GameLogic`/`VisualState.GameLog` to observe (`StaticGameLogic` per `.claude/rules/unity/localization.md` has no equivalent state). No wiring is added to `MainMenuLifetimeScope`/`SelectCountryLifetimeScope`.

### Log-type-proposal skill

New file `.claude/commands/propose-log-type.md`, following the `add-character.md` convention (free-form `$ARGUMENTS`, numbered `## Steps`, produces a written definition only — no code changes):

```markdown
Define a new Action Log line type for `Docs/Specs/54_action-log-ui/` (or its successor spec) to implement later.

## Arguments

`$ARGUMENTS` may be free-form. Examples:
- `Province ownership changes` — propose a log line for a province changing hands
- `Country score milestones` — propose a log line for a country crossing a score threshold

If `$ARGUMENTS` is empty, ask the user what game-logic event should get a log line before proceeding.

## Steps

1. **Identify the underlying state** the line should observe — an existing ECS component/resource that already changes when this event happens (per this feature's "observe state, don't hook commands" convention — see `Docs/Specs/54_action-log-ui/plan.md`). If no such state exists yet, say so explicitly; this skill does not design new game-logic systems.
2. **Define the trigger direction** — what specific change (e.g. "value increases", "a new non-empty occupant appears") counts as loggable, matching the increase-only/appearance-only convention already established (no removal/decrease lines).
3. **Write the line format** — the exact `string.Format` template with `{n}` placeholders, plus which segments (if any) are bold+colored (name-class entities only, via an existing per-entity color source) vs bold-white (role-class labels) vs default (everything else).
4. **List the locale keys needed** — the new `game_log.*_format` key (English + Russian text) plus any already-existing keys it reuses (`country_name.*`, `organization_name.*`, `character.role.*.name`, etc.).
5. **Note the data DTO fields** the new `GameLogEntry`-equivalent needs (or confirm the existing `GameLogEntry` shape already covers it).
6. **Write the definition** to a short section the user can hand to `/plan` — do not edit `src/` or `Assets/` code.
```

## Steps

### Agent Steps

- [ ] **Add `GameLogSettings` config class** — new file `src/Game.Configs/GameLogSettings.cs`; `IncludePlayerActions` (bool, default `true`), `MaxLogEntries` (int, default `12`).
- [ ] **Add `GameLog` property to `GameSettings`** — `src/Game.Configs/GameSettings.cs`; `public GameLogSettings GameLog { get; set; } = new GameLogSettings();`.
- [ ] **Update `Assets/Configs/game_settings.json`** — add the `gameLog` block per the Approach section.
- [ ] **Add `GameLogEntryKind`/`GameLogEntry`/`GameLogState`** — `src/Game.Main/VisualState.cs`; plus `GameLog` property on `VisualState`.
- [ ] **Implement `UpdateGameLog` diff logic in `VisualStateConverter`** — `src/Game.Main/VisualStateConverter.cs`; new baseline dictionaries, constructor params (`gameLogIncludePlayerActions`, `gameLogMaxEntries`), the four per-kind scan+diff+suppress+cap+evict steps, baseline-init-without-emit guard, call site wired into `Update(...)` after `UpdatePlayerOrganization`.
- [ ] **Wire `GameLogic` constructor** — `src/Game.Main/GameLogic.cs`; move `_visualStateConverter = new VisualStateConverter(...)` to after `settings` is loaded, pass `settings.GameLog.IncludePlayerActions`/`settings.GameLog.MaxLogEntries`.
- [ ] **Add locale keys** — `Assets/Localization/en.asset` and `ru.asset`; the four `game_log.*_format` keys per the Locale keys table/list above.
- [ ] **Create `ActionLog.uxml` + `ActionLog.uss`** — `Assets/UI/HUD/ActionLog/`; per the UI section above.
- [ ] **Wire `ActionLog` template into `HUD.uxml`** — add `<ui:Template>`/`<ui:Instance name="action-log" class="action-log-panel">`.
- [ ] **Create `ActionLogView`** — `Assets/Scripts/Unity/UI/ActionLogView.cs`; diff-based `Refresh`, rich-text line builders (one per `GameLogEntryKind`), `RepositionAndResize` geometry tracking, fade-in/fade-out via `IStyle.transitionDuration` + `VisualElement.schedule`.
- [ ] **Wire `ActionLogView` into `HUDDocument`** — `Assets/Scripts/Unity/UI/HUDDocument.cs`; instantiate in `Start()`, subscribe/unsubscribe `_state.GameLog.PropertyChanged` in `OnEnable`/`OnDisable`, initial `Refresh` call in `OnEnable`.
- [ ] **Compile check** — after all script/UXML/USS changes, `refresh_unity` then `read_console(types=["error"])`.
- [ ] **Verify `action-log` UXML instance resolves in the `Map.unity` scene** — since `HUDDocument`'s `UIDocument` already points at the existing `HUD.uxml` source asset, no scene-file edit should be required; confirm via `read_console` after `refresh_unity` that `root.Q("action-log")`/`root.Q("top-right-panel")` are non-null at runtime (no silent null-ref).
- [ ] **Create the log-type-proposal skill** — `.claude/commands/propose-log-type.md`; per the Log-type-proposal skill section above.
- [ ] **Add a short "incremental diff Refresh" note to `.claude/rules/unity/uitoolkit.md`** — documenting `ActionLogView`'s pattern (identity-keyed diff, independent per-element fade transitions via `IStyle.transitionDuration` + `schedule.Execute().ExecuteLater(...)`) as the accumulating/animating-list alternative to the existing full-rebuild `Refresh()` convention, for future log-like UI to reuse without rediscovering it.

### User Steps

### 1. Visually verify panel placement and behavior
Enter Play mode in the `Map` scene. Confirm: the panel sits immediately below the time/speed controls, right-aligned with them, roughly `1.5×` their width; toggling country/org selection (which shows/hides the bottom bar) does not move the log panel at all; triggering a discovery, a control-raising card, an opinion-raising card, and the `Next: <role>` debug buttons each produce a correctly worded, correctly styled (bold/colored names, bold-white role, plain rest) new line at the bottom that fades in; once more than `maxLogEntries` (12) lines have appeared, the oldest visibly fades out before disappearing rather than vanishing instantly; a line longer than the panel width wraps rather than truncating or scrolling.

### 2. Verify `includePlayerActions: false` suppression
Temporarily set `gameLog.includePlayerActions` to `false` in `Assets/Configs/game_settings.json`, enter Play mode, trigger a player-org action (e.g. the discover-all debug button) and confirm no line appears for it, while confirming (via a multi-org test save or the bot-driven AI orgs, if observable in the build under test) that AI-org lines still appear. Revert the config change afterward.

## Tests

Touches `src/Game.Main/VisualState.cs`, `src/Game.Main/VisualStateConverter.cs`, `src/Game.Main/GameLogic.cs`, `src/Game.Configs/GameSettings.cs` — all testable from `src/Game.Tests/` without any Unity dependency. New file `src/Game.Tests/GameLogStateTests.cs`, following the `DiscoverAndControlFeatureTests.cs`/`CharacterVisualStateTests.cs` convention (build a minimal `GameLogicContext` with bespoke `CountryConfig`/`OrganizationConfig`/`ActionConfig`/`EffectConfig`/`CharacterConfig`/`GameSettings` via `MultiOrgTestSupport.StaticConfig<T>`, construct `GameLogic`, call `Update(0f)` to seed, mutate world state or push commands, call `Update(0f)` again, assert on `logic.VisualState.GameLog.Entries`):

- **Discovery diff**: adding a `DiscoveredCountry{OrgId,CountryId}` entity between two `Update()` calls produces exactly one `GameLogEntryKind.Discovery` entry with the right `OrgId`/`CountryId`; a second `Update()` with no new discovery produces no additional entry.
- **Control diff — new resulting total, not delta**: two sequential control-raising actions for the same org+country produce two `Control` entries whose `Value` is the running total after each raise (e.g. `5`, then `10`), not the per-action delta.
- **Opinion diff — new resulting total, not delta, and decay does not spuriously re-trigger**: raise opinion once (entry emitted with the new total), advance time across a month boundary so the monthly decay effect fires (no entry emitted, value decreases), then raise opinion again — confirms the second entry's `Value` reflects the correct new (decayed-then-raised) total and that the decay tick alone produced zero entries.
- **New character — org role and country role**: push `DebugCycleCharacterCommand` for both an org role (e.g. `master`) and a country role (e.g. `ruler`); confirm each produces exactly one `NewCharacter` entry with `IsOrgRole` set correctly and `OrgId`/`CountryId` populated per the country-vs-org rule; confirm `DebugDropCharacterCommand` (clearing a slot) produces **no** entry.
- **Baseline-on-load produces zero entries**: after the first `Update(0f)` call alone (which seeds initial characters via `InitSystem`), `GameLog.Entries` is empty — confirms no flood from initial seeding.
- **`includePlayerActions: false` suppresses only player-org entries**: with the flag off, a player-org discovery produces no entry, an AI-org discovery (in a multi-org test setup, per `MultiOrgTestSupport`) still produces one, and a country-role `NewCharacter` entry (no acting org) still produces one.
- **`maxLogEntries` cap and eviction order**: with `maxLogEntries` set to a small test value (e.g. `2`), triggering three distinct loggable events leaves exactly the two newest in `GameLog.Entries`, in original (oldest-first) order, with the first (oldest) evicted.
- **`GameLogSettings` JSON round-trip and defaults**: a small config-loading test (colocated with existing `GameSettings`-adjacent tests, or a new `GameLogSettingsTests.cs`) deserializes a JSON snippet with an explicit `gameLog` block and confirms both fields; a snippet omitting `gameLog` entirely confirms `IncludePlayerActions == true` and `MaxLogEntries == 12` (the C# property defaults apply when the JSON key is absent).

## Constitution Check

No conflicts found — plan aligns with all principles:

- **ECS for all game logic, living in `src/`:** All new state (diff baselines, entry construction, suppression, cap/eviction) lives in `src/Game.Main/VisualStateConverter.cs` and `src/Game.Main/VisualState.cs`, observing existing ECS components/resources — no new commands, no new systems, no simulation rules. `ActionLogView`/`HUDDocument` are presentation-only: they format already-resolved DTOs into text and animate opacity, mirroring exactly how `CountryInfoView`/`PlayerOrgView`/`FlyTextNotifierDocument` already consume `VisualState` without containing game logic.
- **VContainer is the sole DI mechanism:** No new registrations are required — `ActionLogView` is a plain C# object constructed by `HUDDocument` (itself already resolved via the container, same as `CountryInfoView` et al. are already constructed the same way today), reusing `HUDDocument`'s existing injected `ILocalization`/`CountryVisualConfig`/`OrgVisualConfig`. No `new` for a singleton, no `FindObjectOfType`, no static mutable state.
- **UI Toolkit only:** New UXML/USS template (`ActionLog.uxml`/`.uss`) sharing the existing `HUDPanelSettings.asset`; no Canvas/uGUI.
- **Plan before implement / Spec before plan:** This plan follows the already-approved `Docs/Specs/54_action-log-ui/spec.md`.
- **`Docs/Specs/<index>_<name>/plan.md` only:** This file is written to `Docs/Specs/54_action-log-ui/plan.md`, alongside the existing `spec.md`, sharing the already-assigned index `54`.
- **One `.asmdef` per feature folder:** `ActionLogView.cs` is added to the existing `Assets/Scripts/Unity/UI/` folder (assembly `GS.Unity.UI`), which already references everything needed (`VContainer`, `GS.Unity.Common`, `GS.Unity.Map`) — no new folder, no new asmdef. `GameLogSettings.cs` is added to the existing `src/Game.Configs` project, no new project.
- **C# code style:** All sketched code uses tabs, `_`-prefixed private fields, same-line opening braces, and omits redundant access modifiers, consistent with `.claude/rules/csharp/code_style.md`.

Use /implement to start working on the plan or request changes.
