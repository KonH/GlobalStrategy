# Plan: Action Log UI

## Spec

As a player, I want a persistent, scrolling log of important game-logic events (discoveries, control gains, opinion gains, new character appointments) visible on the HUD at all times, so I can follow the consequences of my own and rival organizations' actions.

Four line formats, all driven by reading one-shot ECS event components created at the exact point each effect is applied — the same transient-component pattern already used in this codebase for `ResourceChange`/`DiscoverCountryEffect` — **not** by diffing observed state over time:
1. `<OrganizationDisplayName> discovered <CountryDisplayName>`
2. `<OrganizationDisplayName> increased control in <CountryDisplayName> by +<DELTA> (<TOTAL>)` (`DELTA` = amount just added, `TOTAL` = new resulting total)
3. `<OrganizationDisplayName> increased <CharacterRoleDisplayName> <CharacterDisplayName> opinion in <CountryDisplayName> by +<DELTA> (<TOTAL>)` (`DELTA` = amount just added, `TOTAL` = new resulting total)
4. `New <CharacterRoleDisplayName> in <CountryDisplayName> - <CharacterDisplayName>` (country-government role) **or** `New <CharacterRoleDisplayName> in <OrganizationDisplayName> - <CharacterDisplayName>` (org role) — exactly one variant renders, chosen by whether the character's `CountryId` or `OrgId` is set.

Acceptance criteria highlights:
- Numbers (`DELTA` and `TOTAL`) always render with exactly one decimal digit (`:F1`-style, e.g. `+3.5`, `10.0`, never `+3`/`10` or `+3.50`/`10.00`).
- Country/org display names in a line are bold and colored with that entity's existing per-entity color; role names are bold default-white; everything else (connector words, character names) is default white-with-shadow, unstyled.
- Panel shares `HUDPanelSettings.asset`, `sortingOrder < 1000` (below fly-text), not modal, doesn't block clicks.
- Panel top edge tracks the live rendered bottom of `.top-right-panel` (+ small gap); panel bottom edge is anchored at a **fixed** reserved offset representative of the bottom-bar panel's typical height — this offset never moves when the bottom-bar panel opens/closes.
- Panel width = `1.5 × W` where `W` is `.top-right-panel`'s live rendered width; right edge flush at `right: 6px`.
- Content is bottom-aligned and grows upward; overflow entries scroll off the top (clipped, not truncated); long lines wrap, never truncate/scroll horizontally.
- New entries fade in (short); entries evicted past `gameLog.maxLogEntries` fade out (longer) before removal.
- New `gameLog` config block on `GameSettings`: `includePlayerActions` (bool, default `true`), `maxLogEntries` (int, default `12`). When `includePlayerActions` is `false`, entries whose acting org matches the player's org are suppressed (never queued); AI-org entries and country-role character entries (no acting org) are always unaffected.
- No save persistence — the log starts empty every session. Since entries are only ever produced by reading a freshly-created one-shot event component for something that *just happened this tick*, never by inferring "the loaded/seeded state differs from a remembered baseline," there is no flood risk on init or load — this holds by construction, not via a special-cased guard.
- A new `.claude/commands/*.md` skill walks a contributor through proposing a new log line type (format, locale keys, wiring point) without implementing it.

Out of scope: resource/gold/score log lines, province-ownership log lines, log entry click-to-navigate, sound effects, fly-text reuse, log filtering/search, additional `gameLog` settings beyond the two listed, save persistence.

## Goal

Add a persistent, non-modal HUD panel that surfaces the four event types above as a bottom-aligned, upward-growing, fading scroll log, backed by a new `GameLogState` in `src/Game.Main` that collects freshly-created one-shot ECS event components each tick (no diffing, no baselines) and a new `gameLog` settings block.

## Approach

### Research findings that shape this design

- **This codebase already has an established "one-shot transient event component" pattern**, used to carry "something just happened this tick" information from game-logic systems to `VisualStateConverter` without persisting it: `CreateActionEffectSystem.cs` creates a transient `ResourceChange{EffectId,ResourceId,OwnerId,Amount}` (control **and** opinion effects both go through this) and a transient `DiscoverCountryEffect{EffectId,OrgId}` — neither is `[Savable]` (confirmed: `src/Game.Components/ResourceChangeEffect.cs` has no `[Savable]` attribute on either struct, unlike the persistent `[Savable] ControlEffect` in `ControlEffect.cs`). Both are destroyed every tick by `CleanupActionEffectsSystem.Update(world)` (`src/Game.Systems/CleanupActionEffectsSystem.cs`), which — per `src/Game.Main/GameLogic.cs`'s `Update()` — runs at line 145, **before** `CreateActionEffectSystem.Update(...)` creates the new batch at line 150, and `_visualStateConverter.Update(...)` runs last, at line 159, after every game-logic system for the tick (`CreateActionEffectSystem` at 150, `DiscoverCountrySystem` at 152, plus the character-cycling command handlers at lines 116–121, all earlier in the same method). `VisualStateConverter.UpdateLastFrameEffects` (`src/Game.Main/VisualStateConverter.cs`, lines 53–68) already scans the `ResourceChange` archetype this exact way every tick to build `_state.LastFrameEffects` — this is the direct precedent the new `UpdateGameLog` scans are modeled on.
- This log needs **four** new one-shot components — the existing `ResourceChange`/`DiscoverCountryEffect` don't carry enough (no resulting total, no character/role identity, no "new appointment" signal) — created at the exact point each underlying effect is actually applied, each self-contained (delta + total, or full identity) so `UpdateGameLog` never needs to re-look-up or recompute a running sum:
  - `ControlEffectApplied{OrgId,CountryId,Delta,Total}` — created in `CreateActionEffectSystem.cs`'s `ControlChangeEffectParams` branch, only inside the existing `if (usedTotal < 100)` guard (i.e. only when a `ControlEffect` was actually created). `Delta = toAdd`, `Total = usedTotal + toAdd` — both values the branch already computes for the `ControlEffect`/`ResourceChange` it creates, no new computation needed.
  - `OpinionEffectApplied{OrgId,CharacterId,Delta,Total}` — created in the same file's `OpinionModifierEffectParams` branch. `Delta = opinionParams.InitialValue`. `Total` requires a **code change**: `EnsureOpinionResource` (currently `void`) must return the resulting `double` value so the branch can populate `Total` without a second lookup.
  - `DiscoveryApplied{OrgId,CountryId}` — created in `DiscoverCountrySystem.cs`'s `ResolveDiscoveryForOrg`, as a **separate sibling entity** immediately next to the existing `DiscoveredCountry` creation (not attached to the same entity — the persistent `DiscoveredCountry` record's lifecycle must stay untouched by the transient one's cleanup).
  - `RoleChangeApplied{CountryId,OrgId,RoleId,CharacterId}` — created in `src/Game.Main/GameLogic.cs`, once in `CycleOrgCharacterSlot` (`OrgId` set, `CountryId=""`) and once in `CycleCountryCharacter` (`CountryId` set, `OrgId=""`), matching `Character.OrgId`/`Character.CountryId`'s own xor convention (`Character.cs`: `OrgId` comment reads `// empty string = country character`). **Not** created in `ApplyDebugDropCharacter` — dropping a character is out of scope for logging (spec only covers "new" appointments).
  - None of the four are `[Savable]` — same rationale as `ResourceChange`/`DiscoverCountryEffect`: they exist for exactly one tick and are swept before the next.
- **Cleanup wiring**: all four new types are added to `CleanupActionEffectsSystem.Update`'s existing `RemoveComponent<T>(world)` call list, alongside `DiscoverCountryEffect`/`ResourceChange`. This makes their lifecycle byte-for-byte identical to the existing transient pattern: created this tick by a game-logic system/command handler, read this tick by `VisualStateConverter` (which only has `IReadOnlyWorld` access and never destroys anything itself), swept at the very start of the *next* tick before that tick's new batch is created.
- **Ordering constraint** (already satisfied by current code, but worth calling out since nothing enforces it at compile time): `UpdateGameLog` must run after all four creation sites in the same `GameLogic.Update()` tick, and before the *next* tick's `CleanupActionEffectsSystem.Update` call. Confirmed satisfied: `_visualStateConverter.Update(...)` is the literal last call in `GameLogic.Update()` (line 159), after `CreateActionEffectSystem`/`DiscoverCountrySystem`/the character-cycling command handlers. A one-line comment is added at each of the four creation sites and at `CleanupActionEffectsSystem`'s registration, referencing this plan, so a future reordering of `GameLogic.Update()` doesn't silently break it. The **Tests** section adds a regression test that would fail loudly if this ordering were ever violated (see below).
- **No flood on init/load, and no special-cased guard needed to prevent it** — verified against `src/Game.Main/InitSystem.cs`: initial seeding (`CreateCharacterEntities`, `CreateOrgCharacterEntities`, initial `DiscoveredCountry` creation at line ~552–553, initial `ControlEffect` seeding at line ~80–89) creates every persistent component **directly** via `world.Add(...)` — it never calls `CreateActionEffectSystem.Update`, `DiscoverCountrySystem.ResolveDiscoveryForOrg`, `CycleOrgCharacterSlot`, or `CycleCountryCharacter`. Since the four new one-shot components are created *only* inside those four call paths, it is structurally impossible for `InitSystem`'s seeding (a fresh game or a freshly-loaded save) to produce any of them. This replaces the old design's `_gameLogBaselineInitialized` guard entirely — there is no baseline to initialize.
- **Opinion display clamp**: `VisualStateConverter.UpdateCharacters` (lines ~106) already clamps opinion for display: `Math.Clamp((int)resources[i].Value, -100, 100)`. Decision: `OpinionEffectApplied.Total` carries the **raw, unclamped** value returned by `EnsureOpinionResource` (the component describes what game logic actually did, unopinionated about display); `VisualStateConverter.UpdateGameLog` applies the **same** `Math.Clamp(..., -100, 100)` when building the `GameLogEntry.Total` for an Opinion entry, mirroring `UpdateCharacters`' existing clamp — so what the log says always agrees with what the character panel shows. `Delta` is never clamped (it's a per-event increment, e.g. `opinionParams.InitialValue`, not a running value).
- `CreateActionEffectSystem.GetTargetCharacterByCountryAndRole` proves opinion effects only ever target a character with `CountryId` set (country-government role characters) — org-role characters (Master/Agent) never receive `opinion_*` resources today. So opinion log lines always have a valid `<CountryDisplayName>` via `Character.CountryId`.
- **New character in role**: confirmed via `GameLogic.CycleOrgCharacterSlot`/`CycleCountryCharacter`/`ApplyDebugDropCharacter` — cycling always destroys the old `Character` entity and creates a new one with a **different** `CharacterId`; dropping clears to `""` and never itself counts as "new." Since `RoleChangeApplied` is created unconditionally at the point of a successful cycle (both the org-slot and country-role code paths), this correctly captures "new occupant" regardless of whether the trigger was a debug command or a future real system — satisfying the spec's "debug command still logs" criterion for free, exactly as the old diff-based design did, just via direct event creation instead of inference.
- Character display names are resolved elsewhere (`CharactersView.cs`) as `string.Join(" ", entry.NamePartKeys.Select(_loc.Get))`. `RoleChangeApplied`/`OpinionEffectApplied` only carry `CharacterId` (an id, not a name snapshot) — `UpdateGameLog` resolves `NamePartKeys`/`RoleId` (for Opinion; `RoleId` is already on `RoleChangeApplied` directly) via a single-pass `Character` archetype scan (`charLookup[charId] = (RoleId, CountryId, OrgId, NamePartKeys)`) built once per tick, **only when needed** (i.e. only if the `OpinionEffectApplied` or `RoleChangeApplied` archetypes are non-empty this tick — cheap early-exit on the common no-op tick). The resulting `GameLogEntry.NamePartKeys` is still a snapshot copy, not a re-lookup key — the underlying `Character` entity may already have been replaced (re-cycled) by the time the UI renders/re-renders the entry.
- `CountryVisualEntry.color` / `OrgVisualEntry.color` (`Assets/Scripts/Unity/Map/Config/`) are the only existing "per-entity name color" properties in the project (currently used only for map fill) — reused here for bold name coloring, per the spec's explicit invitation to pick an existing color source. `CharacterVisualEntry` has no color field (portrait only), confirming role/character names stay uncolored by design, not by omission.
- Org display name locale key prefix is `organization_name.{orgId}` (confirmed in `Assets/Localization/en.asset`), not `org_name.*`.
- `HUDDocument` is the existing binding MonoBehaviour for `hud-root`, already composing `CountryInfoView`/`PlayerOrgView`/`TimeView`/`LensSwitcherView`/`OrgLensCountryView` as plain view classes queried from its own `UIDocument.rootVisualElement`, and subscribing per-substate `PropertyChanged` handlers in `OnEnable`/`OnDisable`. The action log follows this exact composition (a new `ActionLogView` instantiated in `HUDDocument.Start()`) rather than a second standalone `UIDocument` GameObject (like `FlyTextUI`) — because the panel's positioning formulas need to query `.top-right-panel`'s live geometry, which only exists inside `HUDDocument`'s own document tree; a separate `UIDocument` would have no direct reference to that element.

### Event components (`src/Game.Components/GameLogEffects.cs`)

New file, grouping four plain (non-`[Savable]`) structs — same one-file-multiple-related-transient-structs convention already used by `ResourceChangeEffect.cs`:

```csharp
namespace GS.Game.Components {
	public struct ControlEffectApplied {
		public string OrgId;
		public string CountryId;
		public double Delta;
		public double Total;
	}

	public struct OpinionEffectApplied {
		public string OrgId;
		public string CharacterId;
		public double Delta;
		public double Total; // raw, unclamped — VisualStateConverter applies the display clamp
	}

	public struct DiscoveryApplied {
		public string OrgId;
		public string CountryId;
	}

	public struct RoleChangeApplied {
		public string CountryId; // set for country-government roles, "" for org roles
		public string OrgId;     // set for org roles, "" for country-government roles
		public string RoleId;
		public string CharacterId;
	}
}
```

### Creation sites

**Control** — `src/Game.Systems/CreateActionEffectSystem.cs`, `ControlChangeEffectParams` branch, inside `if (usedTotal < 100)`, alongside the existing `ControlEffect`/`ResourceChange` creation:
```csharp
int ge = world.Create();
world.Add(ge, new ControlEffectApplied {
	OrgId = orgId,
	CountryId = countryId,
	Delta = toAdd,
	Total = usedTotal + toAdd
}); // Game Log event — see Docs/Specs/54_action-log-ui/plan.md ordering note
```

**Opinion** — same file, `OpinionModifierEffectParams` branch. `EnsureOpinionResource` changes from `void` to `double` (returns the resulting `Resource.Value`, from either the `+=` branch or the newly-created entity's `initialValue`):
```csharp
static double EnsureOpinionResource(World world, string charId, string resourceId, int initialValue) {
	int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
	foreach (var arch in world.GetMatchingArchetypes(req, null)) {
		ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
		Resource[] resources = arch.GetColumn<Resource>();
		int count = arch.Count;
		for (int i = 0; i < count; i++) {
			if (owners[i].OwnerId == charId && resources[i].ResourceId == resourceId) {
				resources[i].Value += initialValue;
				return resources[i].Value;
			}
		}
	}
	int re = world.Create();
	world.Add(re, new ResourceOwner(charId, OwnerType.Character));
	world.Add(re, new Resource { ResourceId = resourceId, Value = initialValue });
	return initialValue;
}
```
Call site:
```csharp
double opinionTotal = EnsureOpinionResource(world, targetCharId, opinionResourceId, opinionParams.InitialValue);
int ge = world.Create();
world.Add(ge, new OpinionEffectApplied {
	OrgId = orgId,
	CharacterId = targetCharId,
	Delta = opinionParams.InitialValue,
	Total = opinionTotal
}); // Game Log event — see Docs/Specs/54_action-log-ui/plan.md ordering note
```

**Discovery** — `src/Game.Systems/DiscoverCountrySystem.cs`, `ResolveDiscoveryForOrg`, immediately after the existing `DiscoveredCountry` creation:
```csharp
int newEntity = world.Create();
world.Add(newEntity, new DiscoveredCountry { OrgId = orgId, CountryId = candidates[chosen] });
// Game Log event — separate sibling entity, not attached to DiscoveredCountry above.
// See Docs/Specs/54_action-log-ui/plan.md ordering note.
int ge = world.Create();
world.Add(ge, new DiscoveryApplied { OrgId = orgId, CountryId = candidates[chosen] });
```

**Role change** — `src/Game.Main/GameLogic.cs`. In `CycleOrgCharacterSlot`, after `slot.IsAvailable = false;`:
```csharp
slot.CharacterId = nextEntry.CharacterId;
slot.IsAvailable = false;
// Game Log event — see Docs/Specs/54_action-log-ui/plan.md ordering note.
_world.Add(_world.Create(), new RoleChangeApplied { OrgId = orgId, CountryId = "", RoleId = roleId, CharacterId = nextEntry.CharacterId });
```
In `CycleCountryCharacter`, after the skills-seeding loop at the end of the method:
```csharp
// Game Log event — see Docs/Specs/54_action-log-ui/plan.md ordering note.
_world.Add(_world.Create(), new RoleChangeApplied { OrgId = "", CountryId = countryId, RoleId = roleId, CharacterId = nextEntry.CharacterId });
```
`ApplyDebugDropCharacter` is **not** touched — no `RoleChangeApplied` on drop.

### Cleanup wiring (`src/Game.Systems/CleanupActionEffectsSystem.cs`)

```csharp
public static void Update(World world) {
	// GameAction is persistent card identity (Savable) — not cleaned here.
	RemoveComponent<ActionValid>(world);
	RemoveComponent<ActionSucceeded>(world);
	RemoveComponent<ActionFailed>(world);
	RemoveComponent<CardUse>(world);
	RemoveComponent<DiscoverCountryEffect>(world);
	RemoveComponent<ResourceChange>(world);
	// Game Log events — created this tick by CreateActionEffectSystem/DiscoverCountrySystem/
	// GameLogic's character-cycling handlers, read this tick by VisualStateConverter.UpdateGameLog,
	// swept here at the start of next tick. See Docs/Specs/54_action-log-ui/plan.md ordering note.
	RemoveComponent<ControlEffectApplied>(world);
	RemoveComponent<OpinionEffectApplied>(world);
	RemoveComponent<DiscoveryApplied>(world);
	RemoveComponent<RoleChangeApplied>(world);
}
```

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
	public double Delta { get; }             // Control/Opinion only; amount just applied
	public double Total { get; }             // Control/Opinion only; new resulting total (Opinion: clamped to [-100,100])
	public bool IsOrgRole { get; }           // NewCharacter only: true = OrgId set/CountryId empty

	public GameLogEntry(long sequenceId, GameLogEntryKind kind, string orgId, string countryId,
		string characterId, string roleId, string[] namePartKeys, double delta, double total, bool isOrgRole) { ... }
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

(This supersedes the previous single-`Value`-field design — Control and Opinion lines now need both the delta just applied and the resulting total, not one derived number.)

### Collection logic (`src/Game.Main/VisualStateConverter.cs`)

New fields — no baseline dictionaries, no init-guard:
```csharp
readonly List<GameLogEntry> _gameLogEntries = new();
long _nextGameLogSequenceId = 1;
readonly bool _gameLogIncludePlayerActions;
readonly int _gameLogMaxEntries;
```
Constructor gains two params (defaults matching spec defaults, so the one existing call site in `GameLogic` is the only required change and no test/caller breaks):
```csharp
internal VisualStateConverter(VisualState state, ActionConfig? actionConfig = null,
	bool gameLogIncludePlayerActions = true, int gameLogMaxEntries = 12) { ... }
```

`Update(...)` gains a call `UpdateGameLog(world, orgEntity);`. Placed as the **last** call inside `Update(...)`, after `UpdateCountryScore(world)` and before the animatable-ticking block — this guarantees it runs after every game-logic system for the tick has already created its event components (matches the file's existing bottom-of-method placement pattern for state that depends on the full tick having completed) and after `UpdatePlayerOrganization` has already run (so `_state.PlayerOrganization.OrgId` is current for the `includePlayerActions` comparison).

`UpdateGameLog` outline — a **collection** pass, not a diff pass, modeled directly on the existing `UpdateLastFrameEffects` (lines 53–68), which already scans a transient one-shot component archetype (`ResourceChange`) every tick the same way:

1. Resolve `playerOrgId = _state.PlayerOrganization.OrgId` (already current per the call-site placement above).
2. `newEntries = new List<GameLogEntry>()`.
3. Scan the `ControlEffectApplied` archetype. For each entity: if `!_gameLogIncludePlayerActions && OrgId == playerOrgId`, skip; else construct `GameLogEntry(Kind=Control, OrgId, CountryId, characterId:"", roleId:"", namePartKeys:Array.Empty<string>(), Delta, Total, isOrgRole:false)` and add to `newEntries`.
4. If the `OpinionEffectApplied` archetype is non-empty this tick, build `charLookup` (single-pass `Character` scan: `charLookup[charId] = (RoleId, CountryId, OrgId, NamePartKeys)`). For each `OpinionEffectApplied` entity: look up `charLookup[CharacterId]`; if missing, skip (character no longer present — shouldn't happen same-tick, but fail soft, not throw, matching the file's existing defensive style elsewhere); else apply the display clamp (`double clampedTotal = Math.Clamp(Total, -100, 100);` — matches `UpdateCharacters`' existing `Math.Clamp(..., -100, 100)`), and if not suppressed by `includePlayerActions`, construct `GameLogEntry(Kind=Opinion, OrgId, CountryId:charLookup[...].CountryId, CharacterId, RoleId:charLookup[...].RoleId, NamePartKeys:charLookup[...].NamePartKeys, Delta, Total:clampedTotal, isOrgRole:false)`.
5. Scan the `DiscoveryApplied` archetype. For each entity: suppression check as in step 3; else construct `GameLogEntry(Kind=Discovery, OrgId, CountryId, ..., Delta:0, Total:0, isOrgRole:false)`.
6. If the `RoleChangeApplied` archetype is non-empty this tick and `charLookup` wasn't already built in step 4, build it now. For each `RoleChangeApplied` entity: `isOrgRole = !string.IsNullOrEmpty(OrgId)`; suppress only when `isOrgRole && !_gameLogIncludePlayerActions && OrgId == playerOrgId` (country-role entries, `OrgId == ""`, are never suppressed, per spec); else resolve `NamePartKeys` from `charLookup[CharacterId]` (empty array if missing) and construct `GameLogEntry(Kind=NewCharacter, OrgId, CountryId, CharacterId, RoleId, NamePartKeys, Delta:0, Total:0, isOrgRole)`.
7. If `newEntries.Count == 0`, return without touching `_gameLogEntries`/calling `Set` (matches the "almost always a no-op tick" expectation — cheap early return, no redundant `PropertyChanged` notifications on ticks where nothing loggable happened).
8. Otherwise, assign each new entry `SequenceId = _nextGameLogSequenceId++` (in the scan order above — Control, Opinion, Discovery, NewCharacter — stable and deterministic within a tick), append to `_gameLogEntries`; while `_gameLogEntries.Count > _gameLogMaxEntries`, remove at index `0` (oldest-first eviction).
9. Call `_state.GameLog.Set(new List<GameLogEntry>(_gameLogEntries))` (defensive copy, matching the `List<>` pass-by-reference pattern already used by other `Set(...)` calls in this file, e.g. `OrgMap.Set`).

No baseline-initialization step exists, and none is needed — see the "no flood on init/load" research finding above.

### `GameLogic` wiring (`src/Game.Main/GameLogic.cs`)

Unchanged from the previous design (the constructor-ordering concern is about config loading, not the collection mechanism): in the constructor, after `var settings = context.GameSettings.Load();`:
```csharp
_visualStateConverter = new VisualStateConverter(VisualState, _actionConfig,
	settings.GameLog.IncludePlayerActions, settings.GameLog.MaxLogEntries);
```
(Move the `_visualStateConverter = new VisualStateConverter(...)` line, currently constructed one line before `settings` is loaded, to after `var settings = context.GameSettings.Load();` — a small reordering, no other field depends on this ordering.)

### `gameLog` config block (`src/Game.Configs/`)

Unchanged. New file `src/Game.Configs/GameLogSettings.cs` (one type per file, matching every other config class in this folder):
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

The two `NewCharacter` variants (country-role vs org-role) share **one** template — the only difference between them is which id/color feeds `{1}`, a C#-side concern, not a translation concern. Control and Opinion templates each gain one extra placeholder (delta *and* total, instead of a single resulting value) compared to the previous design:

| Key | English value |
|---|---|
| `game_log.discovered_format` | `{0} discovered {1}` |
| `game_log.control_increased_format` | `{0} increased control in {1} by {2} ({3})` |
| `game_log.opinion_increased_format` | `{0} increased {1} {2} opinion in {3} by {4} ({5})` |
| `game_log.new_character_format` | `New {0} in {1} - {2}` |

Placeholder order for the two changed templates: control = `{0}`=org, `{1}`=country, `{2}`=delta (`+F1`), `{3}`=total (`F1`); opinion = `{0}`=org, `{1}`=role, `{2}`=character, `{3}`=country, `{4}`=delta (`+F1`), `{5}`=total (`F1`).

Russian equivalents (added to `ru.asset` alongside `en.asset`, following existing localization workflow — connector words only, `{n}` placeholders unchanged). `увеличил ... до {n}` ("increased ... to N") no longer fits since there are now two numbers; using `на {delta} ({total})` ("by delta (total)") as a reasonably natural literal rendering — **flagging this for a native-speaker sanity check**, consistent with how the previous design already treated Russian strings as best-effort, not verified translation:
- `game_log.discovered_format` → `{0} обнаружил {1}`
- `game_log.control_increased_format` → `{0} увеличил контроль в {1} на {2} ({3})`
- `game_log.opinion_increased_format` → `{0} повысил мнение {1} {2} в {3} на {4} ({5})`
- `game_log.new_character_format` → `Новый {0} в {1} - {2}`

Existing keys reused, not newly added: `country_name.{countryId}`, `organization_name.{orgId}`, `character.role.{roleId}.name`.

### Line composition (Unity side)

For each `GameLogEntry`, the view builds the rich-text string, e.g. for `Control`:
```csharp
string orgName = WrapColored(_loc.Get($"organization_name.{entry.OrgId}"), _orgVisualConfig.Find(entry.OrgId)?.color);
string countryName = WrapColored(_loc.Get($"country_name.{entry.CountryId}"), _countryVisualConfig.Find(entry.CountryId)?.color);
string deltaText = "+" + entry.Delta.ToString("F1", CultureInfo.InvariantCulture);
string totalText = entry.Total.ToString("F1", CultureInfo.InvariantCulture);
string line = string.Format(_loc.Get("game_log.control_increased_format"), orgName, countryName, deltaText, totalText);

static string WrapColored(string text, Color? color) {
	string hex = ColorUtility.ToHtmlStringRGB(color ?? Color.white);
	return $"<b><color=#{hex}>{text}</color></b>";
}
```
`Opinion` is the same shape with the extra role/character segments:
```csharp
string roleName = $"<b>{_loc.Get($"character.role.{entry.RoleId}.name")}</b>";
string characterName = string.Join(" ", entry.NamePartKeys.Select(_loc.Get));
string line = string.Format(_loc.Get("game_log.opinion_increased_format"), orgName, roleName, characterName, countryName, deltaText, totalText);
```
Role names use `$"<b>{_loc.Get($\"character.role.{entry.RoleId}.name\")}</b>"` (bold, no color tag → inherits the label's default white). Character display names use the existing `string.Join(" ", entry.NamePartKeys.Select(_loc.Get))` pattern verbatim, unwrapped. `Discovery`/`NewCharacter` lines are unchanged from the previous design (no delta/total involved).

### UI: new template (`Assets/UI/HUD/ActionLog/`)

Unchanged. `ActionLog.uxml` — a template, following the `CountryInfo`/`Time`/`LensSwitcher` convention:
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

Unchanged in mechanism from the previous design — plain C# view class (not a MonoBehaviour), instantiated in `HUDDocument.Start()`, using the identity-keyed diff/fade `Refresh` pattern (not the project's usual full-rebuild `Refresh()`) because entries must independently fade in (arriving) and fade out (evicted) rather than being cleared/rebuilt wholesale. Only the line-builder bodies change, to read `entry.Delta`/`entry.Total` instead of the old single `entry.Value`:

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
	// BuildDiscoveryLine / BuildNewCharacterLine unchanged.
	// BuildControlLine / BuildOpinionLine now format entry.Delta AND entry.Total — per Line composition section above.
}
```
`RepositionAndResize` computes `top` as the top-right-panel's world-space bottom edge minus the HUD root's world-space top edge (both from `worldBound`, converting screen-space into hud-root-relative coordinates), following the exact `worldBound`-conversion technique already documented for tooltip positioning in `.claude/rules/unity/uitoolkit.md` — `bottom` stays a fixed constant per style assignment in the constructor and is never touched again, guaranteeing it never shifts when the bottom-bar panel toggles visibility (that panel is a sibling, not a layout ancestor, so its `display: none` never affects the action log panel's geometry regardless).

### `HUDDocument` wiring

Unchanged. In `Awake()` or `Start()` (following the existing convention that `rootVisualElement` access is deferred to `Start()`):
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

Unchanged. Confirmed out of scope: the spec frames this entirely as a "HUD"/in-game feature (all four event types only ever occur mid-game), and `MainMenu.unity`/`CountrySelection.unity` have no `GameLogic`/`VisualState.GameLog` to observe (`StaticGameLogic` per `.claude/rules/unity/localization.md` has no equivalent state). No wiring is added to `MainMenuLifetimeScope`/`SelectCountryLifetimeScope`.

### Log-type-proposal skill

New file `.claude/commands/propose-log-type.md`, following the `add-character.md` convention (free-form `$ARGUMENTS`, numbered `## Steps`, produces a written definition only — no code changes). Step 5 is updated from the previous "diff observed state" framing to the now-established event-component convention:

```markdown
Define a new Action Log line type for `Docs/Specs/54_action-log-ui/` (or its successor spec) to implement later.

## Arguments

`$ARGUMENTS` may be free-form. Examples:
- `Province ownership changes` — propose a log line for a province changing hands
- `Country score milestones` — propose a log line for a country crossing a score threshold

If `$ARGUMENTS` is empty, ask the user what game-logic event should get a log line before proceeding.

## Steps

1. **Identify the exact point in game logic where this event happens** — the system/method that applies the underlying effect (e.g. a `CreateActionEffectSystem` branch, a `GameLogic` command handler). This feature's established convention (see `Docs/Specs/54_action-log-ui/plan.md`) is a **one-shot, non-`[Savable]` ECS event component created at that exact site** (mirroring `ControlEffectApplied`/`OpinionEffectApplied`/`DiscoveryApplied`/`RoleChangeApplied`), cleaned up the following tick by `CleanupActionEffectsSystem`, and read by `VisualStateConverter.UpdateGameLog` the same tick it's created. Prefer this over diffing observed state over time. If no clear application site exists yet, say so explicitly; this skill does not design new game-logic systems.
2. **Define the trigger condition** — exactly when the new event component should be created (e.g. "only when an effect was actually applied, not attempted"), matching the "appearance of a fresh event, not inferred from a value comparison" convention already established.
3. **Write the line format** — the exact `string.Format` template with `{n}` placeholders, plus which segments (if any) are bold+colored (name-class entities only, via an existing per-entity color source) vs bold-white (role-class labels) vs default (everything else).
4. **List the locale keys needed** — the new `game_log.*_format` key (English + Russian text) plus any already-existing keys it reuses (`country_name.*`, `organization_name.*`, `character.role.*.name`, etc.).
5. **Note the data DTO fields** the new `GameLogEntry`-equivalent needs, and the new event component's fields (should carry any delta/total/identity data directly — no downstream recomputation) — or confirm the existing `GameLogEntry` shape already covers it.
6. **Write the definition** to a short section the user can hand to `/plan` — do not edit `src/` or `Assets/` code.
```

## Steps

### Agent Steps

- [ ] **Add `GameLogSettings` config class** — new file `src/Game.Configs/GameLogSettings.cs`; `IncludePlayerActions` (bool, default `true`), `MaxLogEntries` (int, default `12`).
- [ ] **Add `GameLog` property to `GameSettings`** — `src/Game.Configs/GameSettings.cs`; `public GameLogSettings GameLog { get; set; } = new GameLogSettings();`.
- [ ] **Update `Assets/Configs/game_settings.json`** — add the `gameLog` block per the Approach section.
- [ ] **Add the four one-shot event components** — new file `src/Game.Components/GameLogEffects.cs`; `ControlEffectApplied`, `OpinionEffectApplied`, `DiscoveryApplied`, `RoleChangeApplied` (none `[Savable]`), per the Event components section above.
- [ ] **Wire `ControlEffectApplied`/`OpinionEffectApplied` creation** — `src/Game.Systems/CreateActionEffectSystem.cs`; add creation inside the `if (usedTotal < 100)` control branch and the opinion branch; change `EnsureOpinionResource` from `void` to `double` (returns the resulting `Resource.Value`) and use its return value for `Total`.
- [ ] **Wire `DiscoveryApplied` creation** — `src/Game.Systems/DiscoverCountrySystem.cs`; add as a separate sibling entity immediately after the existing `DiscoveredCountry` creation in `ResolveDiscoveryForOrg`.
- [ ] **Wire `RoleChangeApplied` creation** — `src/Game.Main/GameLogic.cs`; add in both `CycleOrgCharacterSlot` (after `slot.IsAvailable = false;`) and `CycleCountryCharacter` (after the skills-seeding loop); confirm `ApplyDebugDropCharacter` is left untouched (no entry on drop).
- [ ] **Add ordering-invariant comments** — one-line comment at each of the four creation sites above and at `CleanupActionEffectsSystem`'s registration, referencing this plan's ordering constraint.
- [ ] **Register cleanup for the four new components** — `src/Game.Systems/CleanupActionEffectsSystem.cs`; add `RemoveComponent<T>(world)` calls for `ControlEffectApplied`, `OpinionEffectApplied`, `DiscoveryApplied`, `RoleChangeApplied`.
- [ ] **Add `GameLogEntryKind`/`GameLogEntry`/`GameLogState`** — `src/Game.Main/VisualState.cs`; `GameLogEntry` now carries `Delta`+`Total` (replacing the previous single `Value` field) plus `GameLog` property on `VisualState`.
- [ ] **Implement `UpdateGameLog` collection logic in `VisualStateConverter`** — `src/Game.Main/VisualStateConverter.cs`; remove all diff-baseline fields/guard from the previous design; add `_gameLogEntries`/`_nextGameLogSequenceId`/`_gameLogIncludePlayerActions`/`_gameLogMaxEntries`; scan the four new archetypes each tick (modeled on the existing `UpdateLastFrameEffects`), build `charLookup` on demand for Opinion/NewCharacter, apply `includePlayerActions` suppression and the opinion display clamp, cap/evict, call site wired in as the last step of `Update(...)`.
- [ ] **Wire `GameLogic` constructor** — `src/Game.Main/GameLogic.cs`; move `_visualStateConverter = new VisualStateConverter(...)` to after `settings` is loaded, pass `settings.GameLog.IncludePlayerActions`/`settings.GameLog.MaxLogEntries`.
- [ ] **Add locale keys** — `Assets/Localization/en.asset` and `ru.asset`; the four `game_log.*_format` keys, with `control_increased_format`/`opinion_increased_format` carrying one extra `{n}` placeholder each per the Locale keys table above.
- [ ] **Create `ActionLog.uxml` + `ActionLog.uss`** — `Assets/UI/HUD/ActionLog/`; per the UI section above.
- [ ] **Wire `ActionLog` template into `HUD.uxml`** — add `<ui:Template>`/`<ui:Instance name="action-log" class="action-log-panel">`.
- [ ] **Create `ActionLogView`** — `Assets/Scripts/Unity/UI/ActionLogView.cs`; diff-based `Refresh`, rich-text line builders (one per `GameLogEntryKind`; `BuildControlLine`/`BuildOpinionLine` format both `Delta` and `Total`), `RepositionAndResize` geometry tracking, fade-in/fade-out via `IStyle.transitionDuration` + `VisualElement.schedule`.
- [ ] **Wire `ActionLogView` into `HUDDocument`** — `Assets/Scripts/Unity/UI/HUDDocument.cs`; instantiate in `Start()`, subscribe/unsubscribe `_state.GameLog.PropertyChanged` in `OnEnable`/`OnDisable`, initial `Refresh` call in `OnEnable`.
- [ ] **Compile check** — after all script/UXML/USS changes, `refresh_unity` then `read_console(types=["error"])`.
- [ ] **Verify `action-log` UXML instance resolves in the `Map.unity` scene** — since `HUDDocument`'s `UIDocument` already points at the existing `HUD.uxml` source asset, no scene-file edit should be required; confirm via `read_console` after `refresh_unity` that `root.Q("action-log")`/`root.Q("top-right-panel")` are non-null at runtime (no silent null-ref).
- [ ] **Create the log-type-proposal skill** — `.claude/commands/propose-log-type.md`; per the Log-type-proposal skill section above.
- [ ] **Add a short "incremental diff Refresh" note to `.claude/rules/unity/uitoolkit.md`** — documenting `ActionLogView`'s pattern (identity-keyed diff, independent per-element fade transitions via `IStyle.transitionDuration` + `schedule.Execute().ExecuteLater(...)`) as the accumulating/animating-list alternative to the existing full-rebuild `Refresh()` convention, for future log-like UI to reuse without rediscovering it.

### User Steps

### 1. Visually verify panel placement and behavior
Enter Play mode in the `Map` scene. Confirm: the panel sits immediately below the time/speed controls, right-aligned with them, roughly `1.5×` their width; toggling country/org selection (which shows/hides the bottom bar) does not move the log panel at all; triggering a discovery, a control-raising card, an opinion-raising card, and the `Next: <role>` debug buttons each produce a correctly worded, correctly styled (bold/colored names, bold-white role, plain rest) new line at the bottom that fades in — control/opinion lines show both a `+delta` and a `(total)` number, both to one decimal digit; once more than `maxLogEntries` (12) lines have appeared, the oldest visibly fades out before disappearing rather than vanishing instantly; a line longer than the panel width wraps rather than truncating or scrolling. Also confirm two sequential control raises on the same org+country produce two distinct lines with increasing totals (e.g. `+5.0 (5.0)` then `+5.0 (10.0)`), not a single line that jumps.

### 2. Verify `includePlayerActions: false` suppression
Temporarily set `gameLog.includePlayerActions` to `false` in `Assets/Configs/game_settings.json`, enter Play mode, trigger a player-org action (e.g. the discover-all debug button) and confirm no line appears for it, while confirming (via a multi-org test save or the bot-driven AI orgs, if observable in the build under test) that AI-org lines still appear. Revert the config change afterward.

## Tests

Touches `src/Game.Components/GameLogEffects.cs`, `src/Game.Systems/CreateActionEffectSystem.cs`, `src/Game.Systems/DiscoverCountrySystem.cs`, `src/Game.Systems/CleanupActionEffectsSystem.cs`, `src/Game.Main/VisualState.cs`, `src/Game.Main/VisualStateConverter.cs`, `src/Game.Main/GameLogic.cs`, `src/Game.Configs/GameSettings.cs` — all testable from `src/Game.Tests/` without any Unity dependency. New file `src/Game.Tests/GameLogStateTests.cs`, following the `DiscoverAndControlFeatureTests.cs`/`CharacterVisualStateTests.cs` convention (build a minimal `GameLogicContext` with bespoke `CountryConfig`/`OrganizationConfig`/`ActionConfig`/`EffectConfig`/`CharacterConfig`/`GameSettings` via `MultiOrgTestSupport.StaticConfig<T>`, construct `GameLogic`, call `Update(0f)` to seed, mutate world state or push commands, call `Update(0f)` again, assert on `logic.VisualState.GameLog.Entries`):

- **Discovery**: adding a `DiscoverCountryEffect` (or pushing whatever command routes to `DiscoverCountrySystem`) between two `Update()` calls produces exactly one `GameLogEntryKind.Discovery` entry with the right `OrgId`/`CountryId`; a second `Update()` with no new discovery produces no additional entry. Implicitly also verifies `DiscoveryApplied` and `DiscoveredCountry` are created as independent sibling entities, not conflated — a bug that would surface as either a duplicate/missing persistent record or a duplicate/missing log entry.
- **Control — delta and total, both correct, not just total**: two sequential control-raising actions for the same org+country produce two `Control` entries: first `Delta=5,Total=5` (example values), second `Delta=5,Total=10` — reading `Delta` straight off `ControlEffectApplied` rather than inferring it from a before/after comparison is the core behavior change from the previous design; this test asserts both fields independently.
- **Opinion — delta, total, and decay produces zero entries**: raise opinion once (entry emitted with correct `Delta`/`Total`), advance time across a month boundary so the monthly decay effect fires (decay does not go through `CreateActionEffectSystem`, so no `OpinionEffectApplied` is ever created for it — assert zero additional entries after the decay-only tick), then raise opinion again — the second entry's `Total` reflects the decayed-then-raised value while `Delta` is just the raise amount (not total-so-far). This is a materially stronger assertion than the previous diff-based design since `Delta` is read directly off the component instead of inferred from two snapshots.
- **New character — org role and country role**: push `DebugCycleCharacterCommand` for both an org role (e.g. `master`) and a country role (e.g. `ruler`); confirm each produces exactly one `NewCharacter` entry with `IsOrgRole` set correctly and `OrgId`/`CountryId` populated per the country-vs-org rule; confirm `DebugDropCharacterCommand` (clearing a slot) produces **no** entry (no `RoleChangeApplied` is ever created by `ApplyDebugDropCharacter`).
- **No-flood-on-init**: after the first `Update(0f)` call alone (which seeds initial characters/discoveries/control via `InitSystem`), `GameLog.Entries` is empty. Unlike the previous design, this is not testing a special-cased guard — it is testing the *absence* of a bug class: `InitSystem` never creates any of the four new event components (it seeds `Character`/`CharacterSlot`/`DiscoveredCountry`/`ControlEffect` directly), so there is structurally nothing for `UpdateGameLog` to collect.
- **Ordering-invariant regression coverage**: no dedicated test targets system ordering directly (ordering isn't independently observable from outside `GameLogic.Update()`); the Control test above (two `Update()` calls, one triggering mutation between them, asserting exactly the expected entries appear each time) already fails if `UpdateGameLog` ever ran before `CreateActionEffectSystem`/`DiscoverCountrySystem` in the same tick, or if `CleanupActionEffectsSystem` ever ran after them instead of before — call this out in a doc comment on the test rather than adding a redundant dedicated test.
- **`EnsureOpinionResource` return-value change**: not given a separate unit test — it's fully exercised by the Opinion test above (the `Total` assertions only pass if the new `double` return value is correct on both the create-new-resource path and the increment-existing-resource path, since the test raises opinion twice).
- **`includePlayerActions: false` suppresses only player-org entries**: with the flag off, a player-org discovery produces no entry, an AI-org discovery (in a multi-org test setup, per `MultiOrgTestSupport`) still produces one, and a country-role `NewCharacter` entry (no acting org) still produces one.
- **`maxLogEntries` cap and eviction order**: with `maxLogEntries` set to a small test value (e.g. `2`), triggering three distinct loggable events leaves exactly the two newest in `GameLog.Entries`, in original (oldest-first) order, with the first (oldest) evicted.
- **`GameLogSettings` JSON round-trip and defaults**: a small config-loading test (colocated with existing `GameSettings`-adjacent tests, or a new `GameLogSettingsTests.cs`) deserializes a JSON snippet with an explicit `gameLog` block and confirms both fields; a snippet omitting `gameLog` entirely confirms `IncludePlayerActions == true` and `MaxLogEntries == 12` (the C# property defaults apply when the JSON key is absent).

## Constitution Check

Re-checked against `Docs/Constitution.md`. No conflicts found — plan aligns with all principles, and the revised mechanism arguably tightens the separation further than the previous design:

- **ECS for all game logic, living in `src/`:** All new state — the four event components, their creation at the exact effect-application site, and their cleanup — lives entirely in `src/Game.Components/`, `src/Game.Systems/`, and `src/Game.Main/GameLogic.cs`. `VisualStateConverter.UpdateGameLog` (also `src/Game.Main/`) only *reads* (`IReadOnlyWorld`) already-created components and formats a DTO — it introduces no new game-logic rule, no new command, no new simulation behavior; it strictly narrates what game logic already decided. This is a cleaner separation than the previous diff-based design, which required `VisualStateConverter` to independently re-derive "did this increase" by maintaining its own baseline state — now that inference lives nowhere; the event component *is* the record of "this happened." `ActionLogView`/`HUDDocument` remain presentation-only: they format already-resolved DTOs into text and animate opacity, mirroring exactly how `CountryInfoView`/`PlayerOrgView`/`FlyTextNotifierDocument` already consume `VisualState` without containing game logic.
- **VContainer is the sole DI mechanism:** No new registrations are required — `ActionLogView` is a plain C# object constructed by `HUDDocument` (itself already resolved via the container, same as `CountryInfoView` et al. are already constructed the same way today), reusing `HUDDocument`'s existing injected `ILocalization`/`CountryVisualConfig`/`OrgVisualConfig`. No `new` for a singleton, no `FindObjectOfType`, no static mutable state.
- **UI Toolkit only:** New UXML/USS template (`ActionLog.uxml`/`.uss`) sharing the existing `HUDPanelSettings.asset`; no Canvas/uGUI.
- **Plan before implement / Spec before plan:** This plan follows the already-approved `Docs/Specs/54_action-log-ui/spec.md`; this revision changes only the internal mechanism and line formats, not the feature's scope or acceptance criteria beyond the two explicitly-updated line formats.
- **`Docs/Specs/<index>_<name>/plan.md` only:** This file is written to `Docs/Specs/54_action-log-ui/plan.md`, alongside the existing `spec.md`, sharing the already-assigned index `54`.
- **One `.asmdef` per feature folder:** `ActionLogView.cs` is added to the existing `Assets/Scripts/Unity/UI/` folder (assembly `GS.Unity.UI`), which already references everything needed (`VContainer`, `GS.Unity.Common`, `GS.Unity.Map`) — no new folder, no new asmdef. `GameLogSettings.cs` is added to the existing `src/Game.Configs` project and `GameLogEffects.cs` to the existing `src/Game.Components` project — no new projects.
- **C# code style:** All sketched code uses tabs, `_`-prefixed private fields, same-line opening braces, and omits redundant access modifiers, consistent with `.claude/rules/csharp/code_style.md`.

Use /implement to start working on the plan or request changes.
