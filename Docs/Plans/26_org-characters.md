# Plan 26: Org Characters

## Goal

Give organizations their own character roster with two new roles: **master** (charm-focused, 1 slot) and **agent** (intrigue-focused, configurable multi-slot). The Illuminati gets a full set of lore-appropriate characters. A new Selected Org panel (accessed by clicking the org info block) shows the org's characters using the same expand/collapse pattern as the country panel. Debug menu buttons let developers cycle and drop characters in any slot for both country and org owners. Empty slots show in the UI as greyed-out cards; player-org empty slots show as "Available".

## Approach

1. Extend configs and ECS components to support org ownership and slot-state tracking.
2. Add `CharacterSlot` ECS component (one per slot, whether filled or empty). The struct is generic by design — `OwnerId` holds either a `countryId` or an `orgId` — though for now only org slots are created.
3. Populate org character entities in `InitSystem`.
4. Extend `VisualState` with `OrgCharactersState` and a `PlayerOrgCharacters` property.
5. Extend `VisualStateConverter` to populate it.
6. Add two debug commands (`DebugCycleCharacter` / `DebugDropCharacter`) processed in `GameLogic.Update`.
7. Build the Selected Org panel UI and wire click on the player org info block.
8. Extend `CharactersView` / add `OrgCharactersView` to render filled + empty/available slots.
9. Add debug buttons to the HUD debug panel.
10. Populate Illuminati character data in JSON and generate portrait images.

---

## Plan Review Notes

1. **Debug button wiring approach**: Debug buttons for country and org characters are built dynamically in `HUDDocument.Start()` (and rebuilt for org chars on state change). Reviewer should confirm: is building buttons dynamically in code the right approach for this project, or should they be declared in UXML and toggled by name? Check how existing debug buttons (e.g. "Open ECS Viewer") are wired in the current `HUDDocument.cs` and ensure the new buttons follow the same pattern.

2. **Org agent slot count is runtime-driven**: Debug buttons for agent slots must reflect the live `_state.PlayerOrgCharacters.Slots` count (not a hardcoded number or config value), because the number of slots can grow during gameplay. Reviewer should verify that `RebuildOrgCharDebugButtons()` is called whenever `PlayerOrgCharacters` changes, and that the container is cleared before rebuilding so stale buttons don't accumulate.

---

## Numbered Steps

### Step 1 — `src/Game.Configs/CharacterConfig.cs`: add `OrgCharacterPool` and `maxCount` to roles

Add `MaxCount` field to `CharacterRoleDefinition` (default 1 for backward-compat). Add `OrgCharacterPool` class (mirrors `CountryCharacterPool` but keyed by `orgId`). Add `OrgPools` list to `CharacterConfig` and a `FindOrgPool(string orgId)` helper.

```csharp
public class CharacterRoleDefinition {
    public string RoleId { get; set; } = "";
    public string NameKey { get; set; } = "";
    public string DescriptionKey { get; set; } = "";
    public string Icon { get; set; } = "";
    public List<string> SkillIds { get; set; } = new();
    public int MaxCount { get; set; } = 1;   // ← new
}

public class OrgCharacterPool {                // ← new class
    public string OrgId { get; set; } = "";
    public Dictionary<string, List<CharacterEntry>> Slots { get; set; } = new();
}

public class CharacterConfig {
    // existing fields …
    public List<OrgCharacterPool> OrgPools { get; set; } = new();   // ← new

    public OrgCharacterPool? FindOrgPool(string orgId) {
        foreach (var p in OrgPools) {
            if (p.OrgId == orgId) return p;
        }
        return null;
    }
}
```

---

### Step 2 — `src/Game.Configs/OrganizationConfig.cs`: add `InitialAgentSlots`

```csharp
public class OrganizationEntry {
    // existing fields …
    public int InitialAgentSlots { get; set; } = 0;   // ← new
}
```

---

### Step 3 — `src/Game.Components/Character.cs`: add `OrgId`

```csharp
[Savable]
public struct Character {
    public string CharacterId;
    public string CountryId;
    public string OrgId;       // ← new; empty string = country character
    public string RoleId;
    public string[] NamePartKeys;
}
```

---

### Step 4 — Create `src/Game.Components/CharacterSlot.cs`

One entity per role slot (filled or empty). `CharacterId` is empty string when slot has no character assigned. The struct is generic — `OwnerId` holds either a `countryId` or an `orgId`. For now only org slots are created; country characters could use slots in the future (future-proof design).

```csharp
namespace GS.Game.Components {
    [Savable]
    public struct CharacterSlot {
        public string OwnerId;     // countryId or orgId
        public string RoleId;
        public int SlotIndex;
        public bool IsAvailable;   // true = ready-for-hire (player org only)
        public string CharacterId; // "" if no character assigned
    }
}
```

---

### Step 5 — `src/Game.Main/InitSystem.cs`: add `CreateOrgCharacterEntities()` call

In `Run()`, after the block that creates the org entity and gold resource (currently lines 56–79), add a call to `CreateOrgCharacterEntities(world, context, rng)`.

The call order must be:

```
// After the org entity + gold block closes
CreateOrgCharacterEntities(world, context, rng);   // ← insert here
CreateCharacterEntities(world, context, rng);      // ← existing call

int initEntity = world.Create();
world.Add(initEntity, new IsInitialized());        // ← must NOT be before this
```

The new call must come BEFORE the `IsInitialized` sentinel entity creation, and BEFORE (or immediately after) the existing `CreateCharacterEntities` call — either order is fine, but the sentinel must remain last.

Add the static method at the bottom of `InitSystem`:

```csharp
static void CreateOrgCharacterEntities(World world, GameLogicContext context, Random rng) {
    var characterConfig = context.Character.Load();
    var orgConfig = context.Organization.Load();

    string orgId = context.InitialOrganizationId;
    if (string.IsNullOrEmpty(orgId)) { return; }
    var orgEntry = orgConfig.FindById(orgId);
    if (orgEntry == null) { return; }

    bool isPlayerOrg = true;
    var pool = characterConfig.FindOrgPool(orgId);

    CreateOrgSlots(world, characterConfig, rng, orgId, "master", 1, pool, isPlayerOrg);

    int agentSlots = orgEntry.InitialAgentSlots;
    if (agentSlots > 0) {
        CreateOrgSlots(world, characterConfig, rng, orgId, "agent", agentSlots, pool, isPlayerOrg);
    }
}

static void CreateOrgSlots(
    World world, CharacterConfig characterConfig, Random rng,
    string orgId, string roleId, int totalSlots,
    OrgCharacterPool? pool, bool isPlayerOrg) {

    List<CharacterEntry>? candidates = null;
    if (pool != null) {
        pool.Slots.TryGetValue(roleId, out candidates);
    }

    for (int slotIndex = 0; slotIndex < totalSlots; slotIndex++) {
        bool filled = slotIndex == 0 && candidates != null && candidates.Count > 0;
        string charId = "";

        if (filled) {
            var charEntry = candidates![rng.Next(candidates.Count)];
            charId = charEntry.CharacterId;

            // Create Character entity
            int charEntity = world.Create();
            var namePartKeys = charEntry.NamePartKeys.ToArray();
            world.Add(charEntity, new Character {
                CharacterId = charId,
                CountryId = "",
                OrgId = orgId,
                RoleId = roleId,
                NamePartKeys = namePartKeys
            });

            // Create skill resource entities (only role-relevant skills)
            var roleDef = characterConfig.FindRole(roleId);
            var roleSkillIds = roleDef != null
                ? new HashSet<string>(roleDef.SkillIds)
                : new HashSet<string>();

            foreach (var skillDef in characterConfig.Skills) {
                if (!roleSkillIds.Contains(skillDef.SkillId)) {
                    continue;
                }
                int skillValue;
                if (charEntry.Skills.TryGetValue(skillDef.SkillId, out var ss)) {
                    skillValue = rng.Next(ss.MinValue, ss.MaxValue + 1);
                } else {
                    skillValue = rng.Next(5, 31);
                }
                int skillEntity = world.Create();
                world.Add(skillEntity, new ResourceOwner(charId));
                world.Add(skillEntity, new Resource { ResourceId = skillDef.SkillId, Value = skillValue });
            }
        }

        // Create CharacterSlot entity (always — represents the slot itself)
        int slotEntity = world.Create();
        world.Add(slotEntity, new CharacterSlot {
            OwnerId = orgId,
            RoleId = roleId,
            SlotIndex = slotIndex,
            IsAvailable = !filled && isPlayerOrg,
            CharacterId = charId
        });
    }
}
```

Note: `candidates.ToArray()` requires `using System.Linq;` or use a manual copy. Use the existing pattern from `CreateCharacterEntities` and avoid LINQ to stay consistent — use `new string[charEntry.NamePartKeys.Count]` copy loop instead.

---

### Step 6 — `src/Game.Main/VisualState.cs`: add org character state classes

Add after `CountryCharactersState`:

```csharp
public class OrgCharacterSlotEntry {
    public string RoleId { get; }
    public int SlotIndex { get; }
    public CharacterStateEntry? Character { get; }  // null if slot empty
    public bool IsAvailable { get; }
    public OrgCharacterSlotEntry(string roleId, int slotIndex, CharacterStateEntry? character, bool isAvailable) {
        RoleId = roleId;
        SlotIndex = slotIndex;
        Character = character;
        IsAvailable = isAvailable;
    }
}

public class OrgCharactersState : INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<OrgCharacterSlotEntry> Slots { get; private set; } = Array.Empty<OrgCharacterSlotEntry>();
    public void Set(List<OrgCharacterSlotEntry> slots) {
        Slots = slots;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
```

In `VisualState`, add property:

```csharp
public OrgCharactersState PlayerOrgCharacters { get; } = new OrgCharactersState();
```

---

### Step 7 — `src/Game.Main/VisualStateConverter.cs`: add `UpdateOrgCharacters()`

In the `Update()` method, add `UpdateOrgCharacters(world);` after `UpdateCharacters(world);`.

Add the role order array including new roles at the top of the class:

```csharp
static readonly string[] s_orgRoleOrder = { "master", "agent" };
```

Add the method:

```csharp
void UpdateOrgCharacters(IReadOnlyWorld world) {
    if (!_state.PlayerOrganization.IsValid) {
        _state.PlayerOrgCharacters.Set(new List<OrgCharacterSlotEntry>());
        return;
    }
    string orgId = _state.PlayerOrganization.OrgId;

    // Build character data lookup
    var charData = new Dictionary<string, (string roleId, string[] namePartKeys)>();
    var charSkills = new Dictionary<string, List<SkillEntry>>();

    int[] charRequired = { TypeId<Character>.Value };
    foreach (Archetype arch in world.GetMatchingArchetypes(charRequired, null)) {
        Character[] chars = arch.GetColumn<Character>();
        int count = arch.Count;
        for (int i = 0; i < count; i++) {
            if (chars[i].OrgId != orgId) {
                continue;
            }
            charData[chars[i].CharacterId] = (chars[i].RoleId, chars[i].NamePartKeys);
            charSkills[chars[i].CharacterId] = new List<SkillEntry>();
        }
    }

    int[] resRequired = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
    foreach (Archetype arch in world.GetMatchingArchetypes(resRequired, null)) {
        ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
        Resource[] resources = arch.GetColumn<Resource>();
        int count = arch.Count;
        for (int i = 0; i < count; i++) {
            if (charSkills.TryGetValue(owners[i].OwnerId, out var skillList)) {
                skillList.Add(new SkillEntry(resources[i].ResourceId, (int)resources[i].Value));
            }
        }
    }

    // Collect slots
    var slotEntries = new List<OrgCharacterSlotEntry>();
    int[] slotRequired = { TypeId<CharacterSlot>.Value };
    foreach (Archetype arch in world.GetMatchingArchetypes(slotRequired, null)) {
        CharacterSlot[] slots = arch.GetColumn<CharacterSlot>();
        int count = arch.Count;
        for (int i = 0; i < count; i++) {
            if (slots[i].OwnerId != orgId) {
                continue;
            }
            CharacterStateEntry? charEntry = null;
            string cid = slots[i].CharacterId;
            if (!string.IsNullOrEmpty(cid) && charData.TryGetValue(cid, out var cd)) {
                charEntry = new CharacterStateEntry(cid, cd.roleId, cd.namePartKeys, charSkills[cid]);
            }
            slotEntries.Add(new OrgCharacterSlotEntry(
                slots[i].RoleId, slots[i].SlotIndex, charEntry, slots[i].IsAvailable));
        }
    }

    slotEntries.Sort((a, b) => {
        int ai = Array.IndexOf(s_orgRoleOrder, a.RoleId);
        int bi = Array.IndexOf(s_orgRoleOrder, b.RoleId);
        if (ai < 0) { ai = int.MaxValue; }
        if (bi < 0) { bi = int.MaxValue; }
        int rc = ai.CompareTo(bi);
        return rc != 0 ? rc : a.SlotIndex.CompareTo(b.SlotIndex);
    });

    _state.PlayerOrgCharacters.Set(slotEntries);
}
```

---

### Step 8 — Create `src/Game.Commands/DebugCycleCharacterCommand.cs`

```csharp
namespace GS.Game.Commands {
    public struct DebugCycleCharacterCommand : ICommand {
        public string OwnerId;   // countryId or orgId
        public string RoleId;
        public int SlotIndex;    // 0 for country roles (single slot)
    }
}
```

---

### Step 9 — Create `src/Game.Commands/DebugDropCharacterCommand.cs`

```csharp
namespace GS.Game.Commands {
    public struct DebugDropCharacterCommand : ICommand {
        public string OwnerId;
        public string RoleId;
        public int SlotIndex;
    }
}
```

---

### Step 10 — `src/Game.Main/GameLogic.cs`: process debug commands

In `Update()`, before `_commandAccessor.Clear()`, add:

```csharp
foreach (var cmd in _commandAccessor.ReadDebugCycleCharacterCommand().AsSpan()) {
    ApplyDebugCycleCharacter(cmd.OwnerId, cmd.RoleId, cmd.SlotIndex);
}
foreach (var cmd in _commandAccessor.ReadDebugDropCharacterCommand().AsSpan()) {
    ApplyDebugDropCharacter(cmd.OwnerId, cmd.RoleId, cmd.SlotIndex);
}
```

The source generator will auto-produce `ReadDebugCycleCharacterCommand()` and `ReadDebugDropCharacterCommand()` from the new command structs.

Add private helper methods. These methods operate on the world directly:

```csharp
void ApplyDebugCycleCharacter(string ownerId, string roleId, int slotIndex) {
    // Determine whether this is a country or org owner
    bool isOrg = IsOrgOwner(ownerId);

    if (isOrg) {
        CycleOrgCharacterSlot(ownerId, roleId, slotIndex);
    } else {
        CycleCountryCharacter(ownerId, roleId);
    }
}

void CycleOrgCharacterSlot(string orgId, string roleId, int slotIndex) {
    // Find the CharacterSlot entity for this org/role/slotIndex
    // Find the OrgCharacterPool in CharacterConfig for this orgId/roleId
    // Find the current character (if any), determine next index in pool, wrap
    // Remove old Character entity + its skill resources if any
    // Create new Character entity + skills
    // Update CharacterSlot.CharacterId
    var pool = CharacterConfig.FindOrgPool(orgId);
    if (pool == null || !pool.Slots.TryGetValue(roleId, out var candidates) || candidates.Count == 0) {
        return;
    }

    // Find current slot entity
    int slotEntityId = FindCharacterSlotEntity(orgId, roleId, slotIndex);
    if (slotEntityId < 0) { return; }

    ref CharacterSlot slot = ref _world.Get<CharacterSlot>(slotEntityId);
    string currentCharId = slot.CharacterId;

    // Find current index in candidates list
    int currentIdx = -1;
    for (int i = 0; i < candidates.Count; i++) {
        if (candidates[i].CharacterId == currentCharId) { currentIdx = i; break; }
    }
    int nextIdx = (currentIdx + 1) % candidates.Count;
    var nextEntry = candidates[nextIdx];

    // Remove old character entity if exists
    if (!string.IsNullOrEmpty(currentCharId)) {
        RemoveCharacterEntity(currentCharId);
    }

    // Create new character entity
    CreateOrgCharacterEntity(_world, CharacterConfig, _rng, orgId, roleId, nextEntry);

    // Update slot
    slot.CharacterId = nextEntry.CharacterId;
    slot.IsAvailable = false;
}



void CycleCountryCharacter(string countryId, string roleId) {
    var pool = CharacterConfig.FindPool(countryId);
    if (pool == null || !pool.Slots.TryGetValue(roleId, out var candidates) || candidates.Count == 0) {
        return;
    }

    // Find current Character entity for this country + role
    string currentCharId = FindCountryCharacterId(countryId, roleId);

    int currentIdx = -1;
    for (int i = 0; i < candidates.Count; i++) {
        if (candidates[i].CharacterId == currentCharId) { currentIdx = i; break; }
    }
    int nextIdx = (currentIdx + 1) % candidates.Count;
    var nextEntry = candidates[nextIdx];

    if (!string.IsNullOrEmpty(currentCharId)) {
        RemoveCharacterEntity(currentCharId);
    }

    // Create new Character entity (country style)
    int charEntity = _world.Create();
    var namePartKeys = new string[nextEntry.NamePartKeys.Count];
    for (int i = 0; i < nextEntry.NamePartKeys.Count; i++) {
        namePartKeys[i] = nextEntry.NamePartKeys[i];
    }
    _world.Add(charEntity, new Character {
        CharacterId = nextEntry.CharacterId,
        CountryId = countryId,
        OrgId = "",
        RoleId = roleId,
        NamePartKeys = namePartKeys
    });
    foreach (var skillDef in CharacterConfig.Skills) {
        int sv;
        if (nextEntry.Skills.TryGetValue(skillDef.SkillId, out var ss)) {
            sv = _rng.Next(ss.MinValue, ss.MaxValue + 1);
        } else {
            sv = _rng.Next(5, 31);
        }
        int se = _world.Create();
        _world.Add(se, new ResourceOwner(nextEntry.CharacterId));
        _world.Add(se, new Resource { ResourceId = skillDef.SkillId, Value = sv });
    }
}

void ApplyDebugDropCharacter(string ownerId, string roleId, int slotIndex) {
    bool isOrg = IsOrgOwner(ownerId);
    bool isPlayerOwner = isOrg
        ? ownerId == _world.Get<Organization>(_orgEntity).OrganizationId
        : false; // countries don't have IsAvailable concept yet (always drop = empty)

    if (isOrg) {
        int slotEntityId = FindCharacterSlotEntity(ownerId, roleId, slotIndex);
        if (slotEntityId < 0) { return; }
        ref CharacterSlot slot = ref _world.Get<CharacterSlot>(slotEntityId);
        if (!string.IsNullOrEmpty(slot.CharacterId)) {
            RemoveCharacterEntity(slot.CharacterId);
            slot.CharacterId = "";
        }
        slot.IsAvailable = isPlayerOwner;
    } else {
        string charId = FindCountryCharacterId(ownerId, roleId);
        if (!string.IsNullOrEmpty(charId)) {
            RemoveCharacterEntity(charId);
        }
    }
}

bool IsOrgOwner(string ownerId) {
    int[] req = { TypeId<Organization>.Value };
    foreach (var arch in _world.GetMatchingArchetypes(req, null)) {
        Organization[] orgs = arch.GetColumn<Organization>();
        for (int i = 0; i < arch.Count; i++) {
            if (orgs[i].OrganizationId == ownerId) { return true; }
        }
    }
    return false;
}

int FindCharacterSlotEntity(string ownerId, string roleId, int slotIndex) {
    int[] req = { TypeId<CharacterSlot>.Value };
    foreach (var arch in _world.GetMatchingArchetypes(req, null)) {
        CharacterSlot[] slots = arch.GetColumn<CharacterSlot>();
        for (int i = 0; i < arch.Count; i++) {
            if (slots[i].OwnerId == ownerId && slots[i].RoleId == roleId && slots[i].SlotIndex == slotIndex) {
                return arch.Entities[i];
            }
        }
    }
    return -1;
}

string FindCountryCharacterId(string countryId, string roleId) {
    int[] req = { TypeId<Character>.Value };
    foreach (var arch in _world.GetMatchingArchetypes(req, null)) {
        Character[] chars = arch.GetColumn<Character>();
        for (int i = 0; i < arch.Count; i++) {
            if (chars[i].CountryId == countryId && chars[i].RoleId == roleId) {
                return chars[i].CharacterId;
            }
        }
    }
    return "";
}

void RemoveCharacterEntity(string charId) {
    // Remove Character entity
    int[] charReq = { TypeId<Character>.Value };
    foreach (var arch in _world.GetMatchingArchetypes(charReq, null)) {
        Character[] chars = arch.GetColumn<Character>();
        for (int i = 0; i < arch.Count; i++) {
            if (chars[i].CharacterId == charId) {
                _world.Destroy(arch.Entities[i]);
                break;
            }
        }
    }
    // Remove skill resource entities owned by this char
    int[] resReq = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
    var toDestroy = new List<int>();
    foreach (var arch in _world.GetMatchingArchetypes(resReq, null)) {
        ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
        for (int i = 0; i < arch.Count; i++) {
            if (owners[i].OwnerId == charId) {
                toDestroy.Add(arch.Entities[i]);
            }
        }
    }
    foreach (int e in toDestroy) { _world.Destroy(e); }
}

static void CreateOrgCharacterEntity(World world, CharacterConfig characterConfig, Random rng, string orgId, string roleId, CharacterEntry charEntry) {
    var namePartKeys = new string[charEntry.NamePartKeys.Count];
    for (int i = 0; i < charEntry.NamePartKeys.Count; i++) {
        namePartKeys[i] = charEntry.NamePartKeys[i];
    }
    int charEntity = world.Create();
    world.Add(charEntity, new Character {
        CharacterId = charEntry.CharacterId,
        CountryId = "",
        OrgId = orgId,
        RoleId = roleId,
        NamePartKeys = namePartKeys
    });
    var roleDef = characterConfig.FindRole(roleId);
    var roleSkillIds = roleDef != null
        ? new HashSet<string>(roleDef.SkillIds)
        : new HashSet<string>();
    foreach (var skillDef in characterConfig.Skills) {
        if (!roleSkillIds.Contains(skillDef.SkillId)) { continue; }
        int sv;
        if (charEntry.Skills.TryGetValue(skillDef.SkillId, out var ss)) {
            sv = rng.Next(ss.MinValue, ss.MaxValue + 1);
        } else {
            sv = rng.Next(5, 31);
        }
        int se = world.Create();
        world.Add(se, new ResourceOwner(charEntry.CharacterId));
        world.Add(se, new Resource { ResourceId = skillDef.SkillId, Value = sv });
    }
}
```

Also expose `CharacterConfig` as a public property (like `ResourceConfig`) so debug helpers can access it:

```csharp
public CharacterConfig CharacterConfig { get; private set; } = null!;
```

(This already exists per the current file — confirm it's already public, which it is on line 26.)

---

### Step 11 — `Assets/Configs/character_config.json`: add new roles and Illuminati org pool

**Add to `"roles"` array** (after `secret_advisor`):

```json
{
  "roleId": "master",
  "nameKey": "character.role.master.name",
  "descriptionKey": "character.role.master.description",
  "icon": "master",
  "skillIds": ["charm"],
  "maxCount": 1
},
{
  "roleId": "agent",
  "nameKey": "character.role.agent.name",
  "descriptionKey": "character.role.agent.description",
  "icon": "agent",
  "skillIds": ["intrigue"],
  "maxCount": 3
}
```

**Add `"orgPools"` array** at the end of the root object:

```json
"orgPools": [
  {
    "orgId": "Illuminati",
    "slots": {
      "master": [
        {
          "characterId": "illuminati_master_1",
          "namePartKeys": ["character.name.part.adam", "character.name.part.weishaupt"],
          "skills": {
            "charm": { "minValue": 70, "maxValue": 95 }
          }
        },
        {
          "characterId": "illuminati_master_2",
          "namePartKeys": ["character.name.part.cornelius", "character.name.part.vondrak"],
          "skills": {
            "charm": { "minValue": 65, "maxValue": 90 }
          }
        },
        {
          "characterId": "illuminati_master_3",
          "namePartKeys": ["character.name.part.sebastian", "character.name.part.moreau"],
          "skills": {
            "charm": { "minValue": 68, "maxValue": 92 }
          }
        }
      ],
      "agent": [
        {
          "characterId": "illuminati_agent_1",
          "namePartKeys": ["character.name.part.xavier", "character.name.part.delvaux"],
          "skills": {
            "intrigue": { "minValue": 60, "maxValue": 90 }
          }
        },
        {
          "characterId": "illuminati_agent_2",
          "namePartKeys": ["character.name.part.elise", "character.name.part.voss"],
          "skills": {
            "intrigue": { "minValue": 55, "maxValue": 85 }
          }
        },
        {
          "characterId": "illuminati_agent_3",
          "namePartKeys": ["character.name.part.nikolai", "character.name.part.krauss"],
          "skills": {
            "intrigue": { "minValue": 50, "maxValue": 80 }
          }
        },
        {
          "characterId": "illuminati_agent_4",
          "namePartKeys": ["character.name.part.liselotte", "character.name.part.stern"],
          "skills": {
            "intrigue": { "minValue": 58, "maxValue": 88 }
          }
        },
        {
          "characterId": "illuminati_agent_5",
          "namePartKeys": ["character.name.part.remy", "character.name.part.lacroix"],
          "skills": {
            "intrigue": { "minValue": 52, "maxValue": 82 }
          }
        },
        {
          "characterId": "illuminati_agent_6",
          "namePartKeys": ["character.name.part.otto", "character.name.part.brandt"],
          "skills": {
            "intrigue": { "minValue": 48, "maxValue": 78 }
          }
        }
      ]
    }
  }
]
```

---

### Step 12 — `Assets/Configs/organizations.json`: add `initialAgentSlots`

```json
{
  "Organizations": [
    {
      "OrganizationId": "Illuminati",
      "DisplayName": "Illuminati",
      "HqCountryId": "United_Kingdom_of_Great_Britain_and_Ireland",
      "InitialGold": 1000.0,
      "InitialAgentSlots": 3
    }
  ]
}
```

---

### Step 13 — `Assets/Localization/en.asset` + `ru.asset`: add locale keys

Add the following keys to both locale assets:

| Key | English | Russian |
|-----|---------|---------|
| `character.role.master.name` | Master | Мастер |
| `character.role.master.description` | Leader of the organization, master of persuasion | Лидер организации, мастер убеждения |
| `character.role.agent.name` | Agent | Агент |
| `character.role.agent.description` | Field operative, skilled in covert affairs | Полевой оперативник, мастер тайных дел |
| `character.name.part.adam` | Adam | Адам |
| `character.name.part.weishaupt` | Weishaupt | Вейсхаупт |
| `character.name.part.cornelius` | Cornelius | Корнелий |
| `character.name.part.vondrak` | von Drak | фон Драк |
| `character.name.part.sebastian` | Sebastian | Себастьян |
| `character.name.part.moreau` | Moreau | Моро |
| `character.name.part.xavier` | Xavier | Ксавьер |
| `character.name.part.delvaux` | Delvaux | Дельво |
| `character.name.part.elise` | Elise | Элиза |
| `character.name.part.voss` | Voss | Фосс |
| `character.name.part.nikolai` | Nikolai | Николай |
| `character.name.part.krauss` | Krauss | Краус |
| `character.name.part.liselotte` | Liselotte | Лизелотта |
| `character.name.part.stern` | Stern | Штерн |
| `character.name.part.remy` | Remy | Реми |
| `character.name.part.lacroix` | Lacroix | Лакруа |
| `character.name.part.otto` | Otto | Отто |
| `character.name.part.brandt` | Brandt | Брандт |
| `hud.org_characters` | Characters | Персонажи |
| `hud.slot_empty` | Empty | Пусто |
| `hud.slot_available` | Available | Доступно |

---

### Step 14 — Create `Assets/UI/Overlay/OrgInfo/OrgInfo.uxml`

Mirror the CountryInfo layout pattern — characters slide on top, then an org info bar below:

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" editor-extension-mode="False">
    <ui:Style src="project://database/Assets/UI/Shared/SharedStyles.uss"/>
    <ui:Style src="project://database/Assets/UI/Overlay/OrgInfo/OrgInfo.uss"/>
    <ui:VisualElement name="characters-slide" class="org-characters-slide">
        <ui:VisualElement name="characters-container" class="characters-container" />
    </ui:VisualElement>
    <ui:VisualElement name="org-bar" class="org-bar">
        <ui:VisualElement name="org-main-block" class="org-main-block">
            <ui:Label name="org-name" class="org-name" text="" />
            <ui:VisualElement name="resources-container" class="resources-container" />
        </ui:VisualElement>
        <ui:Button name="chars-toggle-btn" class="chars-toggle-btn gs-btn gs-btn--small" text="▲ Characters" />
    </ui:VisualElement>
</ui:UXML>
```

---

### Step 15 — Create `Assets/UI/Overlay/OrgInfo/OrgInfo.uss`

```css
.org-bar {
    flex-direction: row;
    align-items: center;
    padding: 4px 8px;
    background-color: rgba(30, 20, 10, 0.85);
    border-radius: 6px;
    min-width: 260px;
}

.org-main-block {
    flex: 1;
    flex-direction: column;
}

.org-name {
    font-size: 22px;
    color: rgb(230, 200, 130);
    -unity-font-style: bold;
}

.org-characters-slide {
    max-height: 0;
    overflow: hidden;
    transition-property: max-height;
    transition-duration: 0.3s;
    transition-timing-function: ease-in-out;
}

.org-characters-slide--open {
    max-height: 600px;
}
```

---

### Step 16 — Create `Assets/Scripts/Unity/UI/OrgCharactersView.cs`

This view renders `OrgCharactersState` including empty and available slots:

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Main;
using GS.Game.Configs;
using GS.Unity.Common;

namespace GS.Unity.UI {
    class OrgCharactersView {
        readonly VisualElement _container;
        readonly ILocalization _loc;
        readonly CharacterConfig _characterConfig;
        readonly TooltipSystem _tooltip;
        readonly CharacterVisualConfig _visualConfig;

        public OrgCharactersView(VisualElement container, ILocalization loc, CharacterConfig characterConfig, TooltipSystem tooltip, CharacterVisualConfig visualConfig) {
            _container = container;
            _loc = loc;
            _characterConfig = characterConfig;
            _tooltip = tooltip;
            _visualConfig = visualConfig;
        }

        public void Refresh(OrgCharactersState state) {
            _container.Clear();
            if (state.Slots.Count == 0) {
                _container.style.display = DisplayStyle.None;
                return;
            }
            _container.style.display = DisplayStyle.Flex;
            foreach (var slot in state.Slots) {
                _container.Add(slot.Character != null
                    ? BuildFilledCard(slot)
                    : BuildEmptyCard(slot));
            }
        }

        VisualElement BuildFilledCard(OrgCharacterSlotEntry slot) {
            // Same structure as CharactersView.BuildCharacterCard, reuse pattern
            var entry = slot.Character!;
            var card = new VisualElement();
            card.AddToClassList("character-card");

            var roleDef = _characterConfig.FindRole(entry.RoleId);
            string roleName = roleDef != null ? _loc.Get(roleDef.NameKey) : entry.RoleId;
            string roleDesc = roleDef != null ? _loc.Get(roleDef.DescriptionKey) : "";

            var nameParts = new List<string>();
            foreach (var key in entry.NamePartKeys) {
                nameParts.Add(_loc.Get(key));
            }
            var nameLabel = new Label(string.Join(" ", nameParts));
            nameLabel.AddToClassList("character-name");
            card.Add(nameLabel);

            var portrait = new VisualElement();
            portrait.AddToClassList("character-portrait");
            var sprite = _visualConfig?.FindPortrait(entry.CharacterId);
            if (sprite != null) {
                portrait.style.backgroundImage = new StyleBackground(sprite);
            } else {
                portrait.style.display = DisplayStyle.None;
            }
            card.Add(portrait);

            var roleBlock = new VisualElement();
            roleBlock.AddToClassList("role-block");
            var roleIcon = new VisualElement();
            roleIcon.AddToClassList($"character-role-icon--{entry.RoleId}");
            roleBlock.Add(roleIcon);
            var roleLabel = new Label(roleName);
            roleLabel.AddToClassList("gs-hint");
            roleBlock.Add(roleLabel);
            card.Add(roleBlock);

            if (!string.IsNullOrEmpty(roleDesc)) {
                string capturedDesc = roleDesc;
                _tooltip.RegisterTrigger(roleBlock, $"role-{entry.RoleId}-{entry.CharacterId}", _ => BuildSimpleTooltip(roleName, capturedDesc), new System.Collections.Generic.HashSet<string>());
            }

            var skillsBlock = new VisualElement();
            skillsBlock.AddToClassList("skills-block");
            var roleSkillIds = roleDef != null
                ? new System.Collections.Generic.HashSet<string>(roleDef.SkillIds)
                : new System.Collections.Generic.HashSet<string>();

            foreach (var skillDef in _characterConfig.Skills) {
                if (!roleSkillIds.Contains(skillDef.SkillId)) { continue; }
                SkillEntry skill = null;
                foreach (var s in entry.Skills) {
                    if (s.SkillId == skillDef.SkillId) { skill = s; break; }
                }
                if (skill == null) { continue; }

                string skillName = _loc.Get(skillDef.NameKey);
                string skillDesc = _loc.Get(skillDef.DescriptionKey);
                var chip = new VisualElement();
                chip.AddToClassList("skill-chip");
                var skillIcon = new VisualElement();
                skillIcon.AddToClassList($"character-skill-icon--{skill.SkillId}");
                chip.Add(skillIcon);
                var valueLabel = new Label(skill.Value.ToString());
                valueLabel.AddToClassList("skill-value");
                chip.Add(valueLabel);
                string csn = skillName;
                string csd = skillDesc;
                _tooltip.RegisterTrigger(chip, $"skill-{skill.SkillId}-{entry.CharacterId}", _ => BuildSimpleTooltip(csn, csd), new System.Collections.Generic.HashSet<string>());
                skillsBlock.Add(chip);
            }
            card.Add(skillsBlock);
            return card;
        }

        VisualElement BuildEmptyCard(OrgCharacterSlotEntry slot) {
            var card = new VisualElement();
            card.AddToClassList("character-card");
            card.AddToClassList("character-card--empty");

            var roleDef = _characterConfig.FindRole(slot.RoleId);
            string roleName = roleDef != null ? _loc.Get(roleDef.NameKey) : slot.RoleId;

            var portrait = new VisualElement();
            portrait.AddToClassList("character-portrait");
            portrait.AddToClassList("character-portrait--empty");
            card.Add(portrait);

            var roleBlock = new VisualElement();
            roleBlock.AddToClassList("role-block");
            var roleIcon = new VisualElement();
            roleIcon.AddToClassList($"character-role-icon--{slot.RoleId}");
            roleBlock.Add(roleIcon);
            var roleLabel = new Label(roleName);
            roleLabel.AddToClassList("gs-hint");
            roleBlock.Add(roleLabel);
            card.Add(roleBlock);

            string statusKey = slot.IsAvailable ? "hud.slot_available" : "hud.slot_empty";
            var statusLabel = new Label(_loc.Get(statusKey));
            statusLabel.AddToClassList("character-slot-status");
            statusLabel.AddToClassList(slot.IsAvailable ? "character-slot-status--available" : "character-slot-status--empty");
            card.Add(statusLabel);

            return card;
        }

        VisualElement BuildSimpleTooltip(string header, string body) {
            var root = new VisualElement();
            var headerLabel = new Label(header);
            headerLabel.AddToClassList("tooltip-header");
            root.Add(headerLabel);
            if (!string.IsNullOrEmpty(body)) {
                var bodyLabel = new Label(body);
                bodyLabel.AddToClassList("tooltip-effect-name");
                root.Add(bodyLabel);
            }
            return root;
        }
    }
}
```

---

### Step 17 — `Assets/UI/HUD/CountryInfo/CountryInfo.uss` (or `SharedStyles.uss`): add empty/available slot styles

Add to `Assets/UI/Shared/SharedStyles.uss`:

```css
.character-card--empty {
    opacity: 0.5;
}

.character-portrait--empty {
    background-color: rgba(50, 40, 30, 0.6);
    border-color: rgba(100, 80, 60, 0.4);
}

.character-slot-status {
    font-size: 14px;
    -unity-text-align: middle-center;
    margin-top: 4px;
}

.character-slot-status--available {
    color: rgb(150, 200, 100);
}

.character-slot-status--empty {
    color: rgb(130, 120, 110);
}
```

---

### Step 18 — Create `Assets/Scripts/Unity/UI/OrgInfoDocument.cs`

This is the binding MonoBehaviour for the Selected Org panel. Pattern mirrors `CountryInfoView` but shows org data with org characters:

```csharp
using System.ComponentModel;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;
using GS.Main;
using GS.Game.Configs;
using GS.Unity.Common;

namespace GS.Unity.UI {
    public class OrgInfoDocument : MonoBehaviour {
        UIDocument _document;
        VisualState _state;
        ILocalization _loc;
        ResourceConfig _resourceConfig;
        CharacterConfig _characterConfig;
        CharacterVisualConfig _characterVisualConfig;
        TooltipSystem _tooltip;

        // View state
        VisualElement _root;
        Label _orgName;
        VisualElement _charsSlide;
        Button _charsToggleBtn;
        ResourcesView _resourcesView;
        OrgCharactersView _charactersView;
        bool _charsOpen;

        [Inject]
        void Construct(VisualState state, ILocalization loc, ResourceConfig resourceConfig, CharacterConfig characterConfig, CharacterVisualConfig characterVisualConfig) {
            _state = state;
            _loc = loc;
            _resourceConfig = resourceConfig;
            _characterConfig = characterConfig;
            _characterVisualConfig = characterVisualConfig;
        }

        void Awake() {
            _document = GetComponent<UIDocument>();
            var docRoot = _document.rootVisualElement;
            _root = docRoot.Q("org-bar");  // query the org-bar container
            // wrap in the full root so Hide() hides entire panel
            _tooltip = new TooltipSystem(docRoot);

            _orgName = docRoot.Q<Label>("org-name");
            _charsSlide = docRoot.Q("characters-slide");
            _charsToggleBtn = docRoot.Q<Button>("chars-toggle-btn");

            _resourcesView = new ResourcesView(docRoot.Q("resources-container"), _loc, _resourceConfig, _tooltip);
            _charactersView = new OrgCharactersView(docRoot.Q("characters-container"), _loc, _characterConfig, _tooltip, _characterVisualConfig);

            if (_charsSlide != null) {
                _charsSlide.pickingMode = PickingMode.Ignore;
            }
            if (_charsToggleBtn != null) {
                _charsToggleBtn.clicked += ToggleChars;
            }

            // Start hidden
            docRoot.style.display = DisplayStyle.None;
        }

        void Start() {
            // Nothing extra needed; visibility driven by PlayerOrganization state
        }

        void OnEnable() {
            if (_state == null) { return; }
            _state.PlayerOrganization.PropertyChanged += HandleOrgChanged;
            _state.PlayerResources.PropertyChanged    += HandleResourcesChanged;
            _state.PlayerOrgCharacters.PropertyChanged += HandleCharactersChanged;
            Refresh();
        }

        void OnDisable() {
            if (_state == null) { return; }
            _state.PlayerOrganization.PropertyChanged -= HandleOrgChanged;
            _state.PlayerResources.PropertyChanged    -= HandleResourcesChanged;
            _state.PlayerOrgCharacters.PropertyChanged -= HandleCharactersChanged;
        }

        void Update() {
            _tooltip?.Update(Time.deltaTime);
        }

        public void Show() {
            _document.rootVisualElement.style.display = DisplayStyle.Flex;
        }

        public void Hide() {
            _document.rootVisualElement.style.display = DisplayStyle.None;
            SetCharsOpen(false);
        }

        public bool IsVisible => _document.rootVisualElement.style.display == DisplayStyle.Flex;

        void Refresh() {
            var org = _state?.PlayerOrganization;
            if (org == null || !org.IsValid) {
                return;
            }
            if (_orgName != null) {
                _orgName.text = org.DisplayName;
            }
            _resourcesView.Refresh(_state.PlayerResources);
            _charactersView.Refresh(_state.PlayerOrgCharacters);

            bool hasChars = _state.PlayerOrgCharacters.Slots.Count > 0;
            if (_charsToggleBtn != null) {
                _charsToggleBtn.style.display = hasChars ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        void ToggleChars() {
            SetCharsOpen(!_charsOpen);
        }

        void SetCharsOpen(bool open) {
            _charsOpen = open;
            if (_charsSlide != null) {
                if (open) {
                    _charsSlide.AddToClassList("org-characters-slide--open");
                    _charsSlide.pickingMode = PickingMode.Position;
                } else {
                    _charsSlide.RemoveFromClassList("org-characters-slide--open");
                    _charsSlide.pickingMode = PickingMode.Ignore;
                }
            }
            if (_charsToggleBtn != null) {
                _charsToggleBtn.text = open ? "▼ Characters" : "▲ Characters";
            }
        }

        void HandleOrgChanged(object sender, PropertyChangedEventArgs e) => Refresh();
        void HandleResourcesChanged(object sender, PropertyChangedEventArgs e) => Refresh();
        void HandleCharactersChanged(object sender, PropertyChangedEventArgs e) => Refresh();
    }
}
```

---

### Step 19 — `Assets/Scripts/Unity/UI/HUDDocument.cs`: wire org info click + debug buttons

**Add fields:**
```csharp
OrgInfoDocument _orgInfoDocument;
VisualElement _root;
int _lastOrgAgentSlotCount = -1;
```

**Extend `[Inject]` method** to accept `OrgInfoDocument orgInfoDocument`:
```csharp
[Inject]
void Construct(..., GameMenuDocument gameMenu, OrgInfoDocument orgInfoDocument) {
    ...
    _orgInfoDocument = orgInfoDocument;
}
```

**In `Awake()`**, assign `_root` from the document root element and use it for all subsequent queries. Change the existing local variable so `_root` is captured for later use by `RebuildOrgCharDebugButtons()`:
```csharp
_root = _document.rootVisualElement;
var root = _root;
```

Then register click on the player-country panel to toggle the org info document:
```csharp
var playerOrgRoot = root.Q("player-country");
if (playerOrgRoot != null) {
    playerOrgRoot.RegisterCallback<ClickEvent>(_ => ToggleOrgInfo());
}
```

**Add toggle method:**
```csharp
void ToggleOrgInfo() {
    if (_orgInfoDocument == null) { return; }
    if (_orgInfoDocument.IsVisible) {
        _orgInfoDocument.Hide();
    } else {
        _orgInfoDocument.Show();
    }
}
```

**In `Start()`**, after existing debug button wiring, add country character debug buttons and perform the initial org char debug button population:

```csharp
// Country character debug buttons
var characterDebugContainer = root.Q("character-debug-container");
if (characterDebugContainer != null && _characterConfig != null) {
    foreach (var role in _characterConfig.Roles) {
        // Only country roles (skip roles not present in any CountryPool.Slots)
        bool usedInCountryPool = false;
        foreach (var cp in _characterConfig.CountryPools) {
            if (cp.Slots.ContainsKey(role.RoleId)) { usedInCountryPool = true; break; }
        }
        if (!usedInCountryPool) { continue; }
        string capturedRoleId = role.RoleId;
        var nextBtn = new Button(() => PushCycleCharacter(_state?.PlayerCountry?.CountryId ?? "", capturedRoleId, 0));
        nextBtn.text = $"Next: {role.RoleId}";
        nextBtn.AddToClassList("gs-btn");
        nextBtn.AddToClassList("gs-btn--small");
        nextBtn.AddToClassList("debug-panel-button");
        characterDebugContainer.Add(nextBtn);

        var dropBtn = new Button(() => PushDropCharacter(_state?.PlayerCountry?.CountryId ?? "", capturedRoleId, 0));
        dropBtn.text = $"Drop: {role.RoleId}";
        dropBtn.AddToClassList("gs-btn");
        dropBtn.AddToClassList("gs-btn--small");
        dropBtn.AddToClassList("debug-panel-button");
        characterDebugContainer.Add(dropBtn);
    }
}

// Initial population of org character debug buttons
RebuildOrgCharDebugButtons();
```

**Add `RebuildOrgCharDebugButtons()` method** (called from `Start()` and from the `PlayerOrgCharacters.PropertyChanged` handler):

```csharp
void RebuildOrgCharDebugButtons() {
    var orgCharDebugContainer = _root?.Q("org-char-debug-container");
    if (orgCharDebugContainer == null) { return; }
    orgCharDebugContainer.Clear();

    // master slot (always 1)
    var masterNextBtn = new Button(() => PushCycleCharacter(GetPlayerOrgId(), "master", 0));
    masterNextBtn.text = "Next: master";
    masterNextBtn.AddToClassList("gs-btn");
    masterNextBtn.AddToClassList("gs-btn--small");
    masterNextBtn.AddToClassList("debug-panel-button");
    orgCharDebugContainer.Add(masterNextBtn);

    var masterDropBtn = new Button(() => PushDropCharacter(GetPlayerOrgId(), "master", 0));
    masterDropBtn.text = "Drop: master";
    masterDropBtn.AddToClassList("gs-btn");
    masterDropBtn.AddToClassList("gs-btn--small");
    masterDropBtn.AddToClassList("debug-panel-button");
    orgCharDebugContainer.Add(masterDropBtn);

    // agent slots: count is determined from current runtime state
    int agentCount = 0;
    if (_state?.PlayerOrgCharacters?.Slots != null) {
        foreach (var slot in _state.PlayerOrgCharacters.Slots) {
            if (slot.RoleId == "agent") { agentCount++; }
        }
    }
    for (int si = 0; si < agentCount; si++) {
        int capturedSlot = si;
        var agentNextBtn = new Button(() => PushCycleCharacter(GetPlayerOrgId(), "agent", capturedSlot));
        agentNextBtn.text = $"Next: agent [{capturedSlot + 1}]";
        agentNextBtn.AddToClassList("gs-btn");
        agentNextBtn.AddToClassList("gs-btn--small");
        agentNextBtn.AddToClassList("debug-panel-button");
        orgCharDebugContainer.Add(agentNextBtn);

        var agentDropBtn = new Button(() => PushDropCharacter(GetPlayerOrgId(), "agent", capturedSlot));
        agentDropBtn.text = $"Drop: agent [{capturedSlot + 1}]";
        agentDropBtn.AddToClassList("gs-btn");
        agentDropBtn.AddToClassList("gs-btn--small");
        agentDropBtn.AddToClassList("debug-panel-button");
        orgCharDebugContainer.Add(agentDropBtn);
    }
}
```

**In `OnEnable()`**, subscribe to `PlayerOrgCharacters.PropertyChanged` (if not already done for the main `OrgInfoDocument` flow) and call `RebuildOrgCharDebugButtons()` from the handler. Also reset `_lastOrgAgentSlotCount` in `OnDisable` so the first `OnEnable` after a scene reload always rebuilds:

```csharp
void OnEnable() {
    if (_state == null) { return; }
    // ... existing subscriptions ...
    _state.PlayerOrgCharacters.PropertyChanged += HandleOrgCharactersChanged;
}

void OnDisable() {
    if (_state == null) { return; }
    // ... existing unsubscriptions ...
    _state.PlayerOrgCharacters.PropertyChanged -= HandleOrgCharactersChanged;
    _lastOrgAgentSlotCount = -1;
}

void HandleOrgCharactersChanged(object sender, PropertyChangedEventArgs e) {
    int agentCount = 0;
    if (_state?.PlayerOrgCharacters?.Slots != null) {
        foreach (var slot in _state.PlayerOrgCharacters.Slots) {
            if (slot.RoleId == "agent") { agentCount++; }
        }
    }
    if (agentCount == _lastOrgAgentSlotCount) { return; }
    _lastOrgAgentSlotCount = agentCount;
    RebuildOrgCharDebugButtons();
}
```

**Add helper methods:**
```csharp
string GetPlayerOrgId() => _state?.PlayerOrganization?.OrgId ?? "";

void PushCycleCharacter(string ownerId, string roleId, int slotIndex) {
    if (string.IsNullOrEmpty(ownerId) || _commands == null) { return; }
    _commands.Push(new DebugCycleCharacterCommand { OwnerId = ownerId, RoleId = roleId, SlotIndex = slotIndex });
}

void PushDropCharacter(string ownerId, string roleId, int slotIndex) {
    if (string.IsNullOrEmpty(ownerId) || _commands == null) { return; }
    _commands.Push(new DebugDropCharacterCommand { OwnerId = ownerId, RoleId = roleId, SlotIndex = slotIndex });
}
```

---

### Step 20 — `Assets/UI/HUD/HUD.uxml`: add debug container elements

Inside the `debug-panel` element, add two new named containers after the existing buttons:

```xml
<ui:VisualElement name="character-debug-container" style="flex-direction: column;" />
<ui:VisualElement name="org-char-debug-container" style="flex-direction: column;" />
```

---

### Step 20b — Create `Assets/UI/Overlay/OverlayPanelSettings.asset`

Run `manage_ui` with action `create_panel_settings`, output path `Assets/UI/Overlay/OverlayPanelSettings.asset`, sorting order = 1. After creation, note the GUID from the generated `.meta` file — it is needed in Step 21.

---

### Step 21 — Register `OrgInfoDocument` in the scene (`Assets/Scenes/Map.unity`)

Add a new GameObject `OrgInfoUI` with `UIDocument` + `OrgInfoDocument` MonoBehaviours, similar to the existing `GameMenuUI` block. Use `OverlayPanelSettings.asset` (GUID `<OverlayPanelSettings-guid>` — substitute the actual GUID from the `.meta` file created in Step 20b). Assign the new `OrgInfo.uxml` as the `sourceAsset`.

The YAML block follows the same pattern as `GameMenuUI`:

```yaml
--- !u!1 &<new-go-fileID>
GameObject:
  m_Name: OrgInfoUI
  m_Component:
  - component: {fileID: <transform-fileID>}
  - component: {fileID: <orginfodoc-fileID>}
  - component: {fileID: <uidocument-fileID>}
  ...
--- !u!114 &<orginfodoc-fileID>
MonoBehaviour:
  m_Script: {fileID: 11500000, guid: <OrgInfoDocument-script-guid>, type: 3}
  m_EditorClassIdentifier: GS.Unity.UI::GS.Unity.UI.OrgInfoDocument
--- !u!114 &<uidocument-fileID>
MonoBehaviour:
  m_Script: {fileID: 19102, guid: 0000000000000000e000000000000000, type: 0}
  m_PanelSettings: {fileID: 11400000, guid: <OverlayPanelSettings-guid>, type: 2}
  sourceAsset: {fileID: 9197481963319205126, guid: <OrgInfo.uxml-guid>, type: 3}
```

Get the actual GUIDs after creating the new files via `refresh_unity`.

---

### Step 22 — Wire `OrgInfoDocument` in `GameLifetimeScope`

In `Assets/Scripts/Unity/DI/GameLifetimeScope.cs`, add:

```csharp
builder.RegisterComponentInHierarchy<OrgInfoDocument>();
```

---

### Step 23 — Image generation for Illuminati characters

After all code and config changes compile successfully, generate portraits for all nine Illuminati characters (3 masters + 6 agents).

Create `.tmp/images.json`:

```json
[
  {
    "outputPath": "Assets/Textures/Characters/illuminati_master_1.png",
    "size": "512x512",
    "prompt": "portrait of Adam Weishaupt, European, Bavarian, 18th century occultist statesman, dark robes, candlelit, secret society, charming commanding presence, mysterious shadowy background, historical oil painting style, formal attire, serious dignified expression, bust portrait, highly detailed, realistic painting"
  },
  {
    "outputPath": "Assets/Textures/Characters/illuminati_master_2.png",
    "size": "512x512",
    "prompt": "portrait of Cornelius von Drak, European nobleman, 19th century occult grand master, silver-streaked hair, dark ceremonial coat, candlelit, secret society, persuasive authoritative bearing, shadowy background, historical oil painting style, commanding expression, bust portrait, highly detailed, realistic painting"
  },
  {
    "outputPath": "Assets/Textures/Characters/illuminati_master_3.png",
    "size": "512x512",
    "prompt": "portrait of Sebastian Moreau, French, 19th century secret order leader, sharp features, black cravat, candlelit, illuminati inner circle, charismatic sophisticated presence, shadowy background, historical oil painting style, piercing gaze, bust portrait, highly detailed, realistic painting"
  },
  {
    "outputPath": "Assets/Textures/Characters/illuminati_agent_1.png",
    "size": "512x512",
    "prompt": "portrait of Xavier Delvaux, French, covert operative, dark cloak, candlelit, secret society, 19th century occult, shadowy background, historical oil painting style, mysterious scheming expression, bust portrait, highly detailed, realistic painting"
  },
  {
    "outputPath": "Assets/Textures/Characters/illuminati_agent_2.png",
    "size": "512x512",
    "prompt": "portrait of Elise Voss, German woman, intelligence operative, 19th century, dark attire, candlelit, secret society, shadowy background, historical oil painting style, cold calculating expression, bust portrait, highly detailed, realistic painting"
  },
  {
    "outputPath": "Assets/Textures/Characters/illuminati_agent_3.png",
    "size": "512x512",
    "prompt": "portrait of Nikolai Krauss, Russian, covert spy, 19th century, dark uniform, candlelit, secret society, shadowy background, historical oil painting style, cold determined expression, bust portrait, highly detailed, realistic painting"
  },
  {
    "outputPath": "Assets/Textures/Characters/illuminati_agent_4.png",
    "size": "512x512",
    "prompt": "portrait of Liselotte Stern, Austrian woman, 19th century covert handler, dark bonnet and shawl, candlelit, secret society, shadowy background, historical oil painting style, watchful cunning expression, bust portrait, highly detailed, realistic painting"
  },
  {
    "outputPath": "Assets/Textures/Characters/illuminati_agent_5.png",
    "size": "512x512",
    "prompt": "portrait of Remy Lacroix, French, 19th century shadow operative, lean face, dark travelling coat, candlelit, secret society, shadowy background, historical oil painting style, guarded secretive expression, bust portrait, highly detailed, realistic painting"
  },
  {
    "outputPath": "Assets/Textures/Characters/illuminati_agent_6.png",
    "size": "512x512",
    "prompt": "portrait of Otto Brandt, Prussian, 19th century intelligence officer, stern angular face, dark military coat without insignia, candlelit, secret society, shadowy background, historical oil painting style, impassive disciplined expression, bust portrait, highly detailed, realistic painting"
  }
]
```

Run:
```powershell
$env:PYTHONUTF8 = '1'; & ".venv\Scripts\python.exe" ".claude\generate_images_batch.py" ".tmp\images.json"
```

Then wire all nine new portraits into `CharacterVisualConfig` following the same pattern used for existing characters (Plan 25).

---

## Files to Create / Modify

| Action | Path |
|--------|------|
| Modify | `src/Game.Configs/CharacterConfig.cs` — add `OrgCharacterPool`, `OrgPools`, `MaxCount` on role |
| Modify | `src/Game.Configs/OrganizationConfig.cs` — add `InitialAgentSlots` on `OrganizationEntry` |
| Modify | `src/Game.Components/Character.cs` — add `OrgId` field |
| Create | `src/Game.Components/CharacterSlot.cs` — new `[Savable]` struct (generic: `OwnerId` holds countryId or orgId) |
| Modify | `src/Game.Main/InitSystem.cs` — add `CreateOrgCharacterEntities()` + `CreateOrgSlots()` |
| Modify | `src/Game.Main/VisualState.cs` — add `OrgCharacterSlotEntry`, `OrgCharactersState`, `PlayerOrgCharacters` |
| Modify | `src/Game.Main/VisualStateConverter.cs` — add `UpdateOrgCharacters()`, call in `Update()` |
| Create | `src/Game.Commands/DebugCycleCharacterCommand.cs` |
| Create | `src/Game.Commands/DebugDropCharacterCommand.cs` |
| Modify | `src/Game.Main/GameLogic.cs` — process debug commands, add cycle/drop helpers |
| Modify | `Assets/Configs/character_config.json` — add master/agent roles, `orgPools` section |
| Modify | `Assets/Configs/organizations.json` — add `initialAgentSlots: 3` |
| Modify | `Assets/Localization/en.asset` — add new locale keys |
| Modify | `Assets/Localization/ru.asset` — add new locale keys (Russian) |
| Create | `Assets/UI/Overlay/OrgInfo/OrgInfo.uxml` |
| Create | `Assets/UI/Overlay/OrgInfo/OrgInfo.uss` |
| Modify | `Assets/UI/Shared/SharedStyles.uss` — add `.character-card--empty`, `.character-slot-status*` |
| Create | `Assets/Scripts/Unity/UI/OrgInfoDocument.cs` |
| Create | `Assets/Scripts/Unity/UI/OrgCharactersView.cs` |
| Modify | `Assets/Scripts/Unity/UI/HUDDocument.cs` — org click toggle, debug character buttons |
| Modify | `Assets/UI/HUD/HUD.uxml` — add `character-debug-container`, `org-char-debug-container` |
| Modify | `Assets/Scenes/Map.unity` — add `OrgInfoUI` GameObject |
| Modify | `Assets/Scripts/Unity/DI/GameLifetimeScope.cs` — register `OrgInfoDocument` |

---

## Tests

Add a new file `src/Game.Tests/OrgCharacterTests.cs`. Use the same `StaticConfig<T>` + `BuildLogic` helper pattern as `CharacterInitTests.cs`.

### Helper: `BuildOrgCharacterLogic()`

Builds a `GameLogic` with:
- One country (`Great_Britain`)
- One org (`Illuminati`, `InitialAgentSlots = 3`, `InitialOrganizationId = "Illuminati"`)
- `CharacterConfig` with `master` (skill=charm, MaxCount=1) and `agent` (skill=intrigue, MaxCount=3) roles
- `OrgCharacterPool` for Illuminati with 1 master candidate and 3 agent candidates

### Test cases

| Test | Assertion |
|------|-----------|
| `org_master_slot_entity_created` | Exactly 1 `CharacterSlot` with `RoleId == "master"` and `OwnerId == "Illuminati"` exists after first `Update(0f)` |
| `org_agent_slot_entities_count_matches_config` | Exactly 3 `CharacterSlot` entities with `RoleId == "agent"` and `OwnerId == "Illuminati"` exist |
| `org_master_slot_index_zero_has_character` | The single master `CharacterSlot` has non-empty `CharacterId` |
| `org_agent_slot_index_zero_has_character` | The agent `CharacterSlot` with `SlotIndex == 0` has non-empty `CharacterId` |
| `org_agent_slots_1_and_2_are_empty` | `CharacterSlot` entities with `SlotIndex == 1` and `SlotIndex == 2` have empty `CharacterId` |
| `player_org_slots_are_available` | The two empty agent slots (SlotIndex 1, 2) have `IsAvailable == true` |
| `non_player_org_slots_are_not_available` | When a second non-player org is in config, its empty slots have `IsAvailable == false` |
| `update_org_characters_populates_player_org_characters` | After `Update(0f)`, `VisualState.PlayerOrgCharacters.Slots.Count == 4` (1 master + 3 agents) |
| `filled_slot_has_character_entry` | The slot with `SlotIndex == 0` for agent has `OrgCharacterSlotEntry.Character != null` |
| `empty_slot_has_null_character_entry` | The slot with `SlotIndex == 1` for agent has `OrgCharacterSlotEntry.Character == null` |
| `debug_cycle_org_character_changes_char_id` | After pushing `DebugCycleCharacterCommand` for `("Illuminati", "master", 0)` and calling `Update(0f)`, master slot `CharacterId` is still non-empty (cycling within 1 candidate wraps to same) |
| `debug_cycle_with_two_candidates_switches_character` | With 2 master candidates, cycle command switches to the other one |
| `debug_drop_org_character_empties_slot` | After `DebugDropCharacterCommand` for `("Illuminati", "master", 0)` + `Update(0f)`, master slot `CharacterId` is empty |
| `debug_drop_player_org_sets_available` | Dropped slot on player org has `IsAvailable == true` |
| `debug_cycle_country_character_switches` | `DebugCycleCharacterCommand` for `("Great_Britain", "ruler", 0)` with 2 ruler candidates switches to the other candidate |
| `debug_drop_country_character_removes_entity` | After drop command for country, `Character` entity for that role no longer exists in world |

---

Use /implement to start working on the plan or request changes.
