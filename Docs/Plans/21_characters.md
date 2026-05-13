# Plan 21 — Characters

## Goal

Add a Characters feature where each country has five named characters occupying fixed role slots (Ruler, Military Advisor, Diplomacy Advisor, Economic Advisor, Secret Advisor). Each character has skill values (Power, Charm, Stinginess, Intrigue) appropriate to their role. Characters are randomly generated from a config pool on first game start, persisted in saves, and restored on load — the same characters stay across sessions.

## Approach

Work layer by layer from config → ECS → VisualState → UI:

1. Define config POCOs in `Game.Configs` and add a JSON file.
2. Add a `Character` ECS component in `Game.Components` (marked `[Savable]`).
3. Extend `GameLogicContext` to carry the new config source.
4. Extend `GameLogic` to spawn Character entities on startup.
5. Add `CountryCharactersState` to `VisualState` and wire it in `VisualStateConverter`.
6. Create Bootstrap SVG icons and import them.
7. Add a `CharactersView` sub-view with card layout and tooltips.
8. Extend `CountryInfoView` to host the new view.
9. Update UXML/USS for CountryInfo.
10. Add all localization keys.
11. Write tests.

Both `Character` and its skill entities (`ResourceOwner` + `Resource`) are `[Savable]`. On first game start the constructor rolls and creates them; on subsequent loads `LoadSystem.Apply` removes the constructor-created entities and replaces them with the saved ones — the same pattern used by `Country` and country resources. No special handling is needed.

---

## Steps

### Step 1 — Config POCOs (`src/Game.Configs/CharacterConfig.cs`)

Create `CharacterConfig.cs` in `src/Game.Configs/` with the following types:

- **`CharacterSkillDefinition`** — one entry per skill:
  - `SkillId` (string): `"power"`, `"charm"`, `"stinginess"`, `"intrigue"`
  - `NameKey` (string): locale key, e.g. `"skill.power"`
  - `DescriptionKey` (string): e.g. `"skill.power.description"`
  - `Icon` (string): Bootstrap SVG name, e.g. `"sword"`

- **`CharacterRoleDefinition`** — one entry per role slot:
  - `RoleId` (string): `"ruler"`, `"military_advisor"`, `"diplomacy_advisor"`, `"economic_advisor"`, `"secret_advisor"`
  - `NameKey` (string): e.g. `"role.ruler"`
  - `DescriptionKey` (string): e.g. `"role.ruler.description"`
  - `Icon` (string): Bootstrap SVG name
  - `SkillIds` (List\<string\>): which skills this role uses — ruler has all four; each advisor has exactly one

- **`SkillSettings`** — min/max range for a single skill in a character entry:
  - `MinValue` (int): floor for the rolled value
  - `MaxValue` (int): ceiling for the rolled value

- **`CharacterEntry`** — one character in a country's pool for a given slot:
  - `CharacterId` (string): unique, e.g. `"great_britain_ruler_1"`
  - `NamePartKeys` (List\<string\>): ordered locale keys for reusable name parts, e.g. `["character.name.british", "character.name.char_i"]`
  - `Skills` (Dictionary\<string, SkillSettings\>): skill range per skillId, e.g. `{ "power": { MinValue: 30, MaxValue: 70 } }`

- **`CountryCharacterPool`** — per-country character configuration:
  - `CountryId` (string)
  - `Slots` (Dictionary\<string, List\<CharacterEntry\>\>): keyed by roleId, value is list of available characters for that slot

- **`CharacterConfig`** — top-level config POCO:
  - `Skills` (List\<CharacterSkillDefinition\>): the four skill definitions
  - `Roles` (List\<CharacterRoleDefinition\>): the five role definitions
  - `CountryPools` (List\<CountryCharacterPool\>): one entry per available country
  - Helper: `FindSkill(string skillId)`, `FindRole(string roleId)`, `FindPool(string countryId)`

### Step 2 — JSON Config File (`Assets/Configs/character_config.json`)

Create `Assets/Configs/character_config.json` with camelCase field names (Newtonsoft.Json convention). Structure:

```json
{
  "skills": [
    { "skillId": "power",      "nameKey": "skill.power",      "descriptionKey": "skill.power.description",      "icon": "sword" },
    { "skillId": "charm",      "nameKey": "skill.charm",      "descriptionKey": "skill.charm.description",      "icon": "chat-heart" },
    { "skillId": "stinginess", "nameKey": "skill.stinginess", "descriptionKey": "skill.stinginess.description", "icon": "coin" },
    { "skillId": "intrigue",   "nameKey": "skill.intrigue",   "descriptionKey": "skill.intrigue.description",   "icon": "eye-slash" }
  ],
  "roles": [
    { "roleId": "ruler",             "nameKey": "role.ruler",             "descriptionKey": "role.ruler.description",             "icon": "crown",       "skillIds": ["power", "charm", "stinginess", "intrigue"] },
    { "roleId": "military_advisor",  "nameKey": "role.military_advisor",  "descriptionKey": "role.military_advisor.description",  "icon": "shield-fill", "skillIds": ["power"] },
    { "roleId": "diplomacy_advisor", "nameKey": "role.diplomacy_advisor", "descriptionKey": "role.diplomacy_advisor.description", "icon": "globe2",      "skillIds": ["charm"] },
    { "roleId": "economic_advisor",  "nameKey": "role.economic_advisor",  "descriptionKey": "role.economic_advisor.description",  "icon": "bank",        "skillIds": ["stinginess"] },
    { "roleId": "secret_advisor",    "nameKey": "role.secret_advisor",    "descriptionKey": "role.secret_advisor.description",    "icon": "incognito",   "skillIds": ["intrigue"] }
  ],
  "countryPools": [
    {
      "countryId": "Great_Britain",
      "slots": {
        "ruler": [
          {
            "characterId": "great_britain_ruler_1",
            "namePartKeys": ["character.name.british", "character.name.char_i"],
            "skills": {
              "power":      { "minValue": 30, "maxValue": 70 },
              "charm":      { "minValue": 40, "maxValue": 80 },
              "stinginess": { "minValue": 30, "maxValue": 70 },
              "intrigue":   { "minValue": 30, "maxValue": 70 }
            }
          },
          {
            "characterId": "great_britain_ruler_2",
            "namePartKeys": ["character.name.british", "character.name.char_ii"],
            "skills": {
              "power":      { "minValue": 20, "maxValue": 60 },
              "charm":      { "minValue": 20, "maxValue": 60 },
              "stinginess": { "minValue": 50, "maxValue": 90 },
              "intrigue":   { "minValue": 20, "maxValue": 60 }
            }
          },
          {
            "characterId": "great_britain_ruler_3",
            "namePartKeys": ["character.name.british", "character.name.char_iii"],
            "skills": {
              "power":      { "minValue": 40, "maxValue": 80 },
              "charm":      { "minValue": 30, "maxValue": 70 },
              "stinginess": { "minValue": 30, "maxValue": 70 },
              "intrigue":   { "minValue": 50, "maxValue": 90 }
            }
          }
        ],
        "military_advisor": [
          {
            "characterId": "great_britain_mil_1",
            "namePartKeys": ["character.name.british", "character.name.char_i"],
            "skills": {
              "power": { "minValue": 40, "maxValue": 90 }
            }
          },
          {
            "characterId": "great_britain_mil_2",
            "namePartKeys": ["character.name.british", "character.name.char_ii"],
            "skills": {
              "power": { "minValue": 20, "maxValue": 70 }
            }
          },
          {
            "characterId": "great_britain_mil_3",
            "namePartKeys": ["character.name.british", "character.name.char_iii"],
            "skills": {
              "power": { "minValue": 30, "maxValue": 80 }
            }
          }
        ],
        "diplomacy_advisor": [ "... 3 entries with charm skill only ..." ],
        "economic_advisor":  [ "... 3 entries with stinginess skill only ..." ],
        "secret_advisor":    [ "... 3 entries with intrigue skill only ..." ]
      }
    },
    "... (repeat pattern for all other available countries: Russian_Empire, France, Germany, etc.)"
  ]
}
```

Naming convention for placeholder characters: `"<country_id>_<role_short>_<N>"` (e.g. `great_britain_mil_1`, `russian_empire_dip_2`).

Use short role abbreviations for IDs: `ruler`, `mil`, `dip`, `eco`, `sec`.

Include pools for all countries where `isAvailable: true` in `country_config.json`. Countries without a pool simply get no character entities (no crash — the spawn loop skips them gracefully).

### Step 3 — ECS Component (`src/Game.Components/Character.cs`)

Create `Character.cs` in `src/Game.Components/`:

```csharp
namespace GS.Game.Components {
    [Savable]
    public struct Character {
        public string   CharacterId;
        public string   CountryId;
        public string   RoleId;
        public string[] NamePartKeys;   // array of locale keys; length 1–N; assembled into display name at render time
    }
}
```

Skills are **not** stored as fields on `Character`. They are stored as separate ECS entities using the existing resource architecture: one entity per skill with `ResourceOwner(characterId)` + `Resource { ResourceId = skillId, Value = skillValue }`. This mirrors the same pattern used for country and org resources. Both `ResourceOwner` and `Resource` are already `[Savable]`, so skills are persisted alongside the `Character` entity automatically.

`NamePartKeys` is a string array of reusable locale keys. Each key can be shared across many characters (e.g. `character.name.british` is used by all British characters). Key naming convention: `character.name.<part>`. This avoids duplicating common name fragments in the locale files — ~10 part keys cover all placeholder names instead of ~75 unique per-character keys.

### Step 4 — Extend `GameLogicContext` (`src/Game.Main/GameLogicContext.cs`)

Add a new `IConfigSource<CharacterConfig> Character` property alongside the existing config sources. Update the constructor to accept and assign it. The parameter must be **optional with a null default** so existing callers and tests continue to compile without changes:

```csharp
IConfigSource<CharacterConfig>? character = null
```

Assign it as: `Character = character ?? new EmptyConfigSource<CharacterConfig>();` (check `GameLogicContext.cs` for the exact name of the existing no-op config source pattern — it may be `EmptyConfigSource<T>`, `StaticConfig<T>` with an empty value, or similar).

### Step 5 — Spawn Characters in `GameLogic` (`src/Game.Main/GameLogic.cs`)

Add a `public CharacterConfig CharacterConfig { get; private set; }` property to `GameLogic`. Populate it from the context's config source during construction (before `CreateCharacterEntities` is called). This property is how the Unity-side `GameLifetimeScope` accesses the config without a second Inspector assignment — see Step 15.

In the `GameLogic` constructor, after the country loop (all Country entities already exist), add a `CreateCharacterEntities` method call:

```
CreateCharacterEntities(countryConfig, characterConfig, rng)
```

Where `rng` is a `System.Random` created once in the constructor with a fixed or time-based seed (document the choice — fixed seed for reproducibility in dev, configurable later).

`CreateCharacterEntities` logic:
1. Load `CharacterConfig` from `context.Character`.
2. For each `CountryEntry` where `IsAvailable == true`:
   a. Find the `CountryCharacterPool` for that country (skip if none).
   b. For each role in `characterConfig.Roles`:
      - Find the slot list in the pool for this roleId.
      - If the list is empty, skip (graceful no-op).
      - Pick a random `CharacterEntry` from the list using `rng`.
      - Roll each skill value: `rng.Next(entry.Skills[skillId].MinValue, entry.Skills[skillId].MaxValue + 1)` for each skillId in the role's `SkillIds`.
      - Create the Character entity: `_world.Create()`, `_world.Add(entity, new Character { CharacterId = entry.CharacterId, CountryId = ..., RoleId = ..., NamePartKeys = entry.NamePartKeys.ToArray() })`.
      - For each skillId in the role's `SkillIds`, create a separate skill entity:
        ```
        int skillEntity = _world.Create();
        _world.Add(skillEntity, new ResourceOwner(entry.CharacterId));
        _world.Add(skillEntity, new Resource { ResourceId = skillId, Value = rolledValue });
        ```
      - No `ResourceEffect` entities are needed — character skills are static values with no monthly delta. `ResourceSystem` processes effects, not static resource values, so existing systems are unaffected.

### Step 6 — VisualState (`src/Game.Main/VisualState.cs`)

Add `CountryCharactersState` class:

```csharp
public class SkillEntry {
    public string SkillId { get; }
    public int    Value   { get; }

    public SkillEntry(string skillId, int value) { SkillId = skillId; Value = value; }
}

public class CharacterStateEntry {
    public string                    CharacterId  { get; }
    public string                    RoleId       { get; }
    public string[]                  NamePartKeys { get; }   // locale keys, assembled into display name at render time
    public IReadOnlyList<SkillEntry> Skills       { get; }

    public CharacterStateEntry(string characterId, string roleId, string[] namePartKeys,
                                IReadOnlyList<SkillEntry> skills) { ... }
}

public class CountryCharactersState : INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<CharacterStateEntry> Characters { get; private set; } = Array.Empty<CharacterStateEntry>();

    public void Set(List<CharacterStateEntry> characters) {
        Characters = characters;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
```

Add `CountryCharactersState SelectedCharacters { get; }` to `VisualState`.

Note: `VisualStateConverter` is in `Game.Main` (no Unity dependency), so it cannot call `ILocalization.Get()`. Display names are assembled from `NamePartKeys` locale keys by the Unity-side view at render time, not by the converter.

### Step 7 — VisualStateConverter (`src/Game.Main/VisualStateConverter.cs`)

Add `UpdateCharacters(IReadOnlyWorld world)` method:

1. Read `_state.SelectedCountry.CountryId`. If not valid, call `_state.SelectedCharacters.Set(new List<>())` and return.
2. Query archetypes with `TypeId<Character>.Value`. For each matching entity where `character.CountryId == selectedCountryId`, build a `Dictionary<string, CharacterStateEntry>` (keyed by `characterId`) with an initially empty skills list (use a `List<SkillEntry>` internally).
3. Query archetypes with both `TypeId<ResourceOwner>.Value` and `TypeId<Resource>.Value`. For each entity where `owner.OwnerId` matches a key in the collected dictionary, append a `SkillEntry(resource.ResourceId, resource.Value)` to that character's skills list.
4. Sort the resulting entries by canonical role order (ruler first, then military, diplomacy, economic, secret — define a static order array).
5. Call `_state.SelectedCharacters.Set(list)`.

Call `UpdateCharacters(world)` from `Update()`.

### Step 8 — Bootstrap SVG Icons

Download from Bootstrap Icons (https://icons.getbootstrap.com) the following SVGs:

| Usage | Bootstrap icon name | Filename |
|---|---|---|
| Ruler role | `crown` | `crown.svg` |
| Military Advisor | `shield-fill` | `shield-fill.svg` |
| Diplomacy Advisor | `globe2` | `globe2.svg` |
| Economic Advisor | `bank` | `bank.svg` |
| Secret Advisor | `eye-slash-fill` | `eye-slash-fill.svg` |
| Power skill | `lightning-fill` | `lightning-fill.svg` |
| Charm skill | `chat-heart-fill` | `chat-heart-fill.svg` |
| Stinginess skill | `coin` | `coin.svg` |
| Intrigue skill | `eye-slash-fill` | (reuse secret advisor icon, or use `binoculars-fill`) |

Save SVGs to `Design/01_prototype/icons/` and copy to `Assets/UI/Icons/`.

Replace any `fill="currentColor"` with `fill="#FFFFFF"` in each SVG before import (matches existing icon import workflow).

Import each via MCP: `manage_asset(action="import", path="Assets/UI/Icons/<name>.svg", properties={"generatedAssetType": "UIToolkitVectorImage"})`.

After import, check `read_console` for `currentColor` warnings.

### Step 9 — CharactersView (`Assets/Scripts/Unity/UI/CharactersView.cs`)

Create `CharactersView.cs` as a plain C# class (not a MonoBehaviour).

Constructor: `CharactersView(VisualElement container, ILocalization loc, CharacterConfig characterConfig, TooltipSystem tooltip)`.

`Refresh(CountryCharactersState state)`:
1. Clear the container.
2. If `state.Characters.Count == 0`, hide container and return.
3. For each `CharacterStateEntry` in the list, call `BuildCharacterCard(entry)` and append to container.

`BuildCharacterCard(CharacterStateEntry entry)` returns a `VisualElement` with:
- Root: `character-card` USS class, flex-column
- **Top block** (`role-block`): flex-row, role icon + role name label, tooltip registered for the role block
  - Role icon: `VisualElement` with USS class `character-role-icon--{entry.RoleId}` (icon set via USS background-image)
  - Role name: `Label` with role name text via `_loc.Get(roleDef.NameKey)`
  - Tooltip for this block: shows `_loc.Get(roleDef.DescriptionKey)` as body text (plain non-inner tooltip)
- **Portrait block** (`portrait-area`): `VisualElement` with USS class `character-portrait`, fixed size; for now shows a grey placeholder rectangle
- **Skills block** (`skills-block`): flex-row, one skill chip per `entry.Skills`
  - For each `SkillEntry skill` in `entry.Skills`, look up `CharacterSkillDefinition skillDef = characterConfig.FindSkill(skill.SkillId)` and build a chip:
    - Each chip: small `VisualElement` containing skill icon + `Label` for value
    - Skill icon: `VisualElement` with USS class `character-skill-icon--{skill.SkillId}`
    - Value label: `Label` text = `skill.Value.ToString()`
    - Tooltip for each chip: shows skill name (`_loc.Get(skillDef.NameKey)`) as header and description (`_loc.Get(skillDef.DescriptionKey)`) as body

Name assembly in `BuildCharacterCard`:
```csharp
var parts = entry.NamePartKeys.Select(k => _loc.Get(k));
string displayName = string.Join(" ", parts);
```

Display the `displayName` in a `Label` (class `character-name`) positioned inside or just below the role block.

Card layout (from top to bottom):
1. Role icon + role name (top row)
2. Character name (below role row)
3. Portrait placeholder (fixed square, e.g. 80×80 px)
4. Skills row (bottom)

### Step 10 — Extend `CountryInfoView` (`Assets/Scripts/Unity/UI/CountryInfoView.cs`)

1. Add `CharactersView _charactersView` field.
2. Construct it in the constructor: `_charactersView = new CharactersView(root.Q("characters-container"), loc, characterConfig, tooltip)`.
3. Add `CharacterConfig characterConfig` parameter to the `CountryInfoView` constructor.
4. In `Refresh(...)`, add `CountryCharactersState characters` parameter and call `_charactersView.Refresh(characters)`.
5. Update all callers (`HUDDocument.Awake`, `HUDDocument.RefreshCountryViews`).

### Step 11 — Extend `HUDDocument` (`Assets/Scripts/Unity/UI/HUDDocument.cs`)

1. Add `CharacterConfig _characterConfig` injected field.
2. Update `[Inject] void Construct(...)` to accept `CharacterConfig characterConfig`.
3. Pass `_characterConfig` when constructing `CountryInfoView`.
4. Subscribe to `_state.SelectedCharacters.PropertyChanged += HandleCharactersChanged` in `OnEnable`.
5. Unsubscribe in `OnDisable`.
6. Add handler: `void HandleCharactersChanged(...) => RefreshCountryViews()`.
7. Pass `_state.SelectedCharacters` in the `RefreshCountryViews()` → `_countryInfo.Refresh(...)` call.

Register `CharacterConfig` as a singleton in the Unity-side `GameLifetimeScope` (inject via `[SerializeField] TextAsset _characterConfigJson` → deserialize in `Configure()` → `builder.RegisterInstance(characterConfig)`).

### Step 12 — UXML: Add `characters-container` (`Assets/UI/HUD/CountryInfo/CountryInfo.uxml`)

Add after `resources-container`:

```xml
<ui:VisualElement name="characters-container" class="characters-container" />
```

### Step 13 — USS Styles

**`Assets/UI/HUD/CountryInfo/CountryInfo.uss`** — add layout rules:

```css
.characters-container {
    flex-direction: row;
    flex-wrap: wrap;
    margin-top: 8px;
}

.character-card {
    flex-direction: column;
    align-items: center;
    width: 90px;
    margin: 4px;
    padding: 4px;
    border-width: 1px;
}

.role-block {
    flex-direction: row;
    align-items: center;
}

.character-portrait {
    width: 80px;
    height: 80px;
    background-color: rgb(100, 100, 100);
    margin-top: 4px;
    margin-bottom: 4px;
}

.skills-block {
    flex-direction: row;
    justify-content: center;
}

/* role icons */
.character-role-icon--ruler             { width: 16px; height: 16px; margin-right: 2px; }
.character-role-icon--military_advisor  { width: 16px; height: 16px; margin-right: 2px; }
.character-role-icon--diplomacy_advisor { width: 16px; height: 16px; margin-right: 2px; }
.character-role-icon--economic_advisor  { width: 16px; height: 16px; margin-right: 2px; }
.character-role-icon--secret_advisor    { width: 16px; height: 16px; margin-right: 2px; }

/* skill icons */
.character-skill-icon--power      { width: 14px; height: 14px; margin-right: 2px; }
.character-skill-icon--charm      { width: 14px; height: 14px; margin-right: 2px; }
.character-skill-icon--stinginess { width: 14px; height: 14px; margin-right: 2px; }
.character-skill-icon--intrigue   { width: 14px; height: 14px; margin-right: 2px; }
```

**`Assets/UI/HUD/HUD.uss`** — add `background-image` entries for role and skill icon classes using the imported SVG GUIDs (fill in GUIDs after import in Step 8).

### Step 14 — Localization Keys

Add to both `Assets/Localization/en.asset` and `Assets/Localization/ru.asset`:

**Roles:**
- `role.ruler` = "Ruler" / "Правитель"
- `role.ruler.description` = "The supreme leader of the country." / "Верховный правитель страны."
- `role.military_advisor` = "Military Advisor" / "Военный советник"
- `role.military_advisor.description` = "Commands the armed forces." / "Командует вооружёнными силами."
- `role.diplomacy_advisor` = "Diplomacy Advisor" / "Дипломатический советник"
- `role.diplomacy_advisor.description` = "Manages foreign relations." / "Управляет иностранными делами."
- `role.economic_advisor` = "Economic Advisor" / "Экономический советник"
- `role.economic_advisor.description` = "Oversees trade and treasury." / "Управляет торговлей и казной."
- `role.secret_advisor` = "Secret Advisor" / "Тайный советник"
- `role.secret_advisor.description` = "Operates in the shadows." / "Действует в тени."

**Skills:**
- `skill.power` = "Power" / "Сила"
- `skill.power.description` = "Military strength and command." / "Военная сила и командование."
- `skill.charm` = "Charm" / "Обаяние"
- `skill.charm.description` = "Diplomatic persuasion ability." / "Дипломатическое обаяние."
- `skill.stinginess` = "Stinginess" / "Скупость"
- `skill.stinginess.description` = "Fiscal prudence and resource management." / "Экономия и управление ресурсами."
- `skill.intrigue` = "Intrigue" / "Интриги"
- `skill.intrigue.description` = "Espionage and covert operations." / "Шпионаж и тайные операции."

**Reusable name part keys** — only ~10 keys needed to cover all placeholder names across all countries:

Nationality adjective keys (one per country adjective):
- `character.name.british` = "British" / "British"
- `character.name.russian` = "Russian" / "Russian"
- `character.name.french` = "French" / "French"
- `character.name.german` = "German" / "German"
- `character.name.ottoman` = "Ottoman" / "Ottoman"
- *(add one per available country)*

Ordinal suffix keys (shared across all characters):
- `character.name.char_i`   = "Character I"   / "Character I"
- `character.name.char_ii`  = "Character II"  / "Character II"
- `character.name.char_iii` = "Character III" / "Character III"

Each character in JSON references two of these keys, e.g. `["character.name.british", "character.name.char_i"]`. The rendered display name is `"British Character I"`. This means the locale file only needs ~10 name-part keys total instead of ~75 unique per-character keys.

### Step 15 — CharacterConfig Unity-Side Loading

In the Unity-side `GameLifetimeScope`, register `CharacterConfig` using the same pattern as `ResourceConfig` — no separate `[SerializeField] TextAsset` field on the scope and no second Inspector assignment needed:

```csharp
builder.Register(c => c.Resolve<GameLogic>().CharacterConfig, Lifetime.Singleton);
```

`GameLogic.CharacterConfig` is already populated during `GameLogic` construction (Step 5). The config source itself is passed via `GameLogicContext` using the existing `TextAssetConfigSource<T>` or equivalent pattern, wired in the same place where `GameLogicContext` is constructed in the scope.

### Step 16 — Tests (`src/Game.Tests/CharacterInitTests.cs`)

Write xUnit tests in `src/Game.Tests/`:

**`CharacterInitTests.cs`**:

Helper: `BuildLogic(CharacterConfig characterConfig)` — reuse the `StaticConfig<T>` pattern from `GameLogicOrgTests.cs`; pass a minimal `CountryConfig` with one available country (`Great_Britain`), and a `CharacterConfig` with pools for that country.

Tests:
1. `character_entities_created_for_available_country` — after `GameLogic` construction, query for `Character` components where `CountryId == "Great_Britain"`; assert exactly 5 entities exist (one per role).
2. `character_roles_match_expected_set` — collect all `RoleId` values for country; assert the set equals `{ "ruler", "military_advisor", "diplomacy_advisor", "economic_advisor", "secret_advisor" }`.
3. `ruler_has_all_four_skills_nonzero` — find the ruler character's `CharacterId`; query all Resource entities with `ResourceOwner.OwnerId == rulerId`; assert exactly 4 exist and all have `Value > 0`.
4. `military_advisor_has_only_power_skill` — find the military advisor's `CharacterId`; query Resource entities owned by that id; assert exactly 1 exists with `ResourceId == "power"` and `Value > 0`.
5. `skill_values_within_configured_range` — for each character, query its owned Resource entities; for each, assert `Value` is within `[entry.Skills[resourceId].MinValue, entry.Skills[resourceId].MaxValue]` from config.
6. `no_character_entities_for_unavailable_country` — add an unavailable country to config (`IsAvailable = false`), assert no Character entities exist for that countryId.
7. `country_without_pool_produces_no_characters` — available country with no entry in `countryPools`; assert no Character entities.
8. `name_part_keys_stored_correctly` — verify `character.NamePartKeys` is an array matching `entry.NamePartKeys` from config.

**`CharacterVisualStateTests.cs`**:

Tests for `VisualStateConverter` characters path (test via `GameLogic.Update()` → read `VisualState.SelectedCharacters`):

1. `characters_state_empty_when_no_country_selected` — no `IsSelected` marker; after `Update`, `SelectedCharacters.Characters` is empty.
2. `characters_state_populated_when_country_selected` — simulate selecting `Great_Britain` (create `IsSelected` component on the country entity), run `Update`, assert `SelectedCharacters.Characters.Count == 5`.
3. `character_state_entries_have_correct_role_ids` — assert the five entries have all five expected roleIds.
4. `character_state_entries_have_skills_populated` — assert each entry's `Skills` list is non-empty and contains entries with non-zero `Value`.

---

## Files to Create / Modify

| Action | Path |
|---|---|
| Create | `src/Game.Configs/CharacterConfig.cs` |
| Create | `Assets/Configs/character_config.json` |
| Create | `src/Game.Components/Character.cs` |
| Modify | `src/Game.Main/GameLogicContext.cs` |
| Modify | `src/Game.Main/GameLogic.cs` |
| Modify | `src/Game.Main/VisualState.cs` |
| Modify | `src/Game.Main/VisualStateConverter.cs` |
| Create | `Assets/Scripts/Unity/UI/CharactersView.cs` |
| Modify | `Assets/Scripts/Unity/UI/CountryInfoView.cs` |
| Modify | `Assets/Scripts/Unity/UI/HUDDocument.cs` |
| Modify | `Assets/UI/HUD/CountryInfo/CountryInfo.uxml` |
| Modify | `Assets/UI/HUD/CountryInfo/CountryInfo.uss` |
| Modify | `Assets/UI/HUD/HUD.uss` |
| Modify | `Assets/Localization/en.asset` |
| Modify | `Assets/Localization/ru.asset` |
| Create | `Assets/UI/Icons/crown.svg` (+ others from Step 8) |
| Create | `src/Game.Tests/CharacterInitTests.cs` |
| Create | `src/Game.Tests/CharacterVisualStateTests.cs` |

---

## Tests

All tests live in `src/Game.Tests/` and run with `dotnet test src/GlobalStrategy.Core.sln`.

Tests use `StaticConfig<T>` (inner helper class) to inject in-memory config objects directly — no JSON files on disk needed.

`CharacterInitTests` covers config-to-ECS wiring: entity count, role set completeness, skill range correctness (via Resource entities), graceful no-pool handling.

`CharacterVisualStateTests` covers the converter path: empty state when no selection, correct population and role coverage when a country is selected, skills list populated on each entry.

No Unity-side unit tests — the view layer (`CharactersView`) is covered by manual playmode inspection.

---

Use /implement to start working on the plan or request changes.
