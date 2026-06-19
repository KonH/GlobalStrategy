# Plan: Country Action Cards

## Spec

Players can play action cards targeting a selected country to improve influence and build relations with its characters. Three card types exist:

- **Sphere of Pressure** (`sphere_of_pressure`): fixed 50% success, 200 gold, +10 influence on success, 1-month cooldown. Pre-dealt to hand slot 0 for every country. Unplayable when influence pool ≥ 100.
- **Letter of Commendation** (`letter_of_commendation_{roleId}`): per advisor role (diplomacy/economic/military/secret), 30%+influence/2 success, 50 gold, +opinion modifier (Value=50, ChangeValue=-1) on success, +1 influence if pool not full. 2-month cooldown. Requires ≥10 influence to draw.
- **Royal Audience** (`royal_audience`): targets ruler, 20%+influence/3 success, 100 gold, +opinion modifier (Value=25, ChangeValue=-1) on success, +2 influence if pool not full. 3-month cooldown. Requires ≥20 influence to draw.

All card decks hold 3 copies. Post-play, the system draws one eligible card from the deck using updated influence. Cooldown display: "N year(s)", "N month(s)", "N days", "1 day", "less than a day". Dynamic-rate cards show a tooltip breakdown on hover. Country panel gets an "Actions" button that opens the 3-slot hand + deck pile. OrgInfoDocument bug fix: Characters and Actions slides must be mutually exclusive.

Out of scope: AI using cards, undo.

---

## Goal

Add a country-targeted card system with three card types, all displayed in a new Country Actions slide in the country info panel, fully wired through ECS, VisualState, and UI Toolkit.

---

## Approach

New ECS components (`CountryActionCard`, `ActionCooldown`) track per-country card state independently of the existing `ActionCard`/`InHand` org system. A new `CountryActionSystem` processes `PlayCountryActionCommand` and manages cooldown expiry. The UI reuses the visual structure of `OrgActionsView` via a new `CountryActionsView` class wired into the existing `CountryInfoView` and `HUDDocument`.

---

## Section 1 — Agent Steps

### Phase 1 — ECS Components

- [x] **Create `src/Game.Components/CountryActionCard.cs`** — define `[Savable] public struct CountryActionCard` with fields: `public string OrgId`, `public string CountryId`, `public string ActionId`, `public string TargetCharacterId`. Mark with `[Savable]` following the same pattern as `ActionCard.cs`.

- [x] **Create `src/Game.Components/ActionCooldown.cs`** — define `[Savable] public struct ActionCooldown` with field `public DateTime CooldownEndTime`. Import `System` at the top. This component is added to all `CountryActionCard` entities sharing the same (OrgId, CountryId, ActionId, TargetCharacterId) when any copy is played. Marking it `[Savable]` ensures cooldown timers survive save/load, consistent with how `InHand` persists org card hand state.

### Phase 2 — Config

- [x] **Create `src/Game.Configs/CountryActionConfig.cs`** — define two classes:
  - `CountryActionDefinition` with properties: `string ActionId`, `string NameKey`, `string DescKey`, `string TargetRole`, `int DeckCopies`, `bool PreDealtToHand`, `int CooldownMonths`, `int InfluenceThreshold`, `float SuccessRateBase`, `int SuccessRateInfluenceDivisor` (0 = fixed rate), `double GoldCost`, `int InfluenceOnSuccess`, `string OpinionModifierSourceId`, `int OpinionModifierValue`, `int OpinionModifierChangeValue`.
  - `CountryActionConfig` with property `List<CountryActionDefinition> Actions` (init to `new()`) and method `public CountryActionDefinition? Find(string actionId)` that iterates and returns the matching entry or null.

- [x] **Create `Assets/Configs/country_action_config.json`** — write the JSON with the `"actions"` array containing 6 entries (one per card type listed below). Use camelCase field names to match Newtonsoft.Json default. Entries:
  1. `sphere_of_pressure`: targetRole="" , preDealtToHand=true, cooldownMonths=1, influenceThreshold=0, successRateBase=0.5, successRateInfluenceDivisor=0, goldCost=200.0, influenceOnSuccess=10, opinionModifierSourceId="", opinionModifierValue=0, opinionModifierChangeValue=0, deckCopies=3, nameKey="action.sphere_of_pressure.name", descKey="action.sphere_of_pressure.desc"
  2. `letter_of_commendation_diplomacy_advisor`: targetRole="diplomacy_advisor", preDealtToHand=false, cooldownMonths=2, influenceThreshold=10, successRateBase=0.3, successRateInfluenceDivisor=2, goldCost=50.0, influenceOnSuccess=1, opinionModifierSourceId="letter_of_commendation", opinionModifierValue=50, opinionModifierChangeValue=-1, deckCopies=3, nameKey="action.letter_of_commendation_diplomacy_advisor.name", descKey="action.letter_of_commendation_diplomacy_advisor.desc"
  3. `letter_of_commendation_economic_advisor`: same as above but targetRole="economic_advisor", actionId and keys adjusted accordingly
  4. `letter_of_commendation_military_advisor`: targetRole="military_advisor"
  5. `letter_of_commendation_secret_advisor`: targetRole="secret_advisor"
  6. `royal_audience`: targetRole="ruler", preDealtToHand=false, cooldownMonths=3, influenceThreshold=20, successRateBase=0.2, successRateInfluenceDivisor=3, goldCost=100.0, influenceOnSuccess=2, opinionModifierSourceId="royal_audience", opinionModifierValue=25, opinionModifierChangeValue=-1, deckCopies=3, nameKey="action.royal_audience.name", descKey="action.royal_audience.desc"

### Phase 3 — Command

- [x] **Create `src/Game.Commands/PlayCountryActionCommand.cs`** — define `public struct PlayCountryActionCommand : ICommand` with fields: `public string OrgId`, `public string CountryId`, `public string ActionId`, `public string TargetCharacterId`. The source generator in `src/Game.SourceGenerators/CommandGenerator.cs` will auto-generate the `ReadPlayCountryActionCommand()` accessor and buffer when the solution is rebuilt — no manual changes to `CommandAccessor.cs` are needed.

### Phase 4 — CountryActionSystem

- [x] **Create `src/Game.Systems/CountryActionSystem.cs`** — `public static class CountryActionSystem` in namespace `GS.Game.Systems`. Add using directives: `System`, `System.Collections.Generic`, `ECS`, `GS.Game.Commands`, `GS.Game.Components`, `GS.Game.Configs`.

  Implement `public static void TickCooldowns(World world, DateTime currentTime)`:
  - Build include mask for `TypeId<CountryActionCard>.Value` and `TypeId<ActionCooldown>.Value`.
  - Collect all entity IDs on cooldown where `CooldownEndTime <= currentTime` into a list.
  - After the collection loop, call `world.Remove<ActionCooldown>(entity)` for each entity in the list (never mutate archetypes inside iteration).

  Implement `public struct ActionResult { public bool Executed; public bool Success; }`.

  Implement `public static ActionResult ProcessPlayCountryAction(World world, PlayCountryActionCommand cmd, CountryActionConfig config, DateTime currentTime, Random rng)`:
  1. Look up `CountryActionDefinition def = config.Find(cmd.ActionId)`. If null, return default.
  2. Find the org's gold entity: iterate `ResourceOwner` + `Resource` where `OwnerId == cmd.OrgId && ResourceId == "gold"`. If gold < def.GoldCost, return default (not Executed).
  3. Deduct gold: `resources[i].Value -= def.GoldCost`.
  4. Compute player org influence in country: iterate `InfluenceEffect` entities where `OrgId == cmd.OrgId && CountryId == cmd.CountryId`, sum `Value` → `int orgInfluence`.
  5. Check eligibility: if `orgInfluence < def.InfluenceThreshold`, return `new ActionResult { Executed = true, Success = false }` (gold was already deducted — this shouldn't happen if UI guards correctly, but be safe; alternatively, deduct gold only after eligibility check. Opt for eligibility check BEFORE gold deduction: move step 2–3 after step 4–5).
  
     **Revised order:**
     1. Find def (return default if null).
     2. Compute `orgInfluence` (sum InfluenceEffect for orgId+countryId).
     3. Check eligibility (`orgInfluence < def.InfluenceThreshold`) → return default if ineligible.
     4. Check and deduct gold → return default if cannot afford.
     5. Mark `result.Executed = true`.
     6. Remove `InHand` from the played entity: iterate `CountryActionCard + InHand` where card matches (OrgId, CountryId, ActionId, TargetCharacterId), call `world.Remove<InHand>(entityId)`. Store the vacated `SlotIndex`.
     7. Add `ActionCooldown { CooldownEndTime = currentTime.AddMonths(def.CooldownMonths) }` to ALL `CountryActionCard` entities where (OrgId, CountryId, ActionId, TargetCharacterId) match — collect into a list first, then add.
     8. Roll RNG: `float successRate = def.SuccessRateBase + (def.SuccessRateInfluenceDivisor > 0 ? orgInfluence / (float)def.SuccessRateInfluenceDivisor : 0f)`. Clamp to 1.0f. `result.Success = (float)rng.NextDouble() < successRate`.
     9. On success:
        - If `def.InfluenceOnSuccess > 0`: compute total influence pool used (iterate ALL `InfluenceEffect` for `CountryId == cmd.CountryId`, sum all org values → `int usedTotal`). If `usedTotal < 100`: create a new `InfluenceEffect` entity with `OrgId = cmd.OrgId`, `CountryId = cmd.CountryId`, `Value = Math.Min(def.InfluenceOnSuccess, 100 - usedTotal)`, `EffectId = $"country_action_{cmd.OrgId}_{cmd.ActionId}_{currentTime.Ticks}"`.
        - If `def.OpinionModifierSourceId != ""` and `!string.IsNullOrEmpty(cmd.TargetCharacterId)`: find entity with `Character.CharacterId == cmd.TargetCharacterId` and `CharacterOpinion` component. Add to `opinion.ModifiersPerOrg[cmd.OrgId]` a new `OpinionModifier { SourceId = def.OpinionModifierSourceId, Value = def.OpinionModifierValue, ChangeValue = def.OpinionModifierChangeValue }`. Initialise the dictionary and list if null.
     10. Draw next card:
        - Recompute `orgInfluence` (now post-effect) by iterating InfluenceEffect again.
        - Find all `CountryActionCard` entities for (OrgId, CountryId) that have neither `InHand` nor `ActionCooldown` components. Filter: `config.Find(card.ActionId)?.InfluenceThreshold <= orgInfluence`. Collect into a `List<int>` of entity IDs.
        - Fisher-Yates shuffle the list with `rng`.
        - If the list is non-empty, add `InHand { SlotIndex = vacatedSlot }` to the first entity.
     11. Return result.

### Phase 5 — GameLogicContext

- [x] **Edit `src/Game.Main/GameLogicContext.cs`** — add `public IConfigSource<CountryActionConfig> CountryAction { get; }` property. Add parameter `IConfigSource<CountryActionConfig>? countryAction = null` at the end of the constructor parameter list. In the constructor body: `CountryAction = countryAction ?? new EmptyCountryActionConfig();`. Add inner sealed class `EmptyCountryActionConfig : IConfigSource<CountryActionConfig>` with `public CountryActionConfig Load() => new CountryActionConfig();`. Add `using GS.Game.Configs;` if not already present at the top.

### Phase 6 — InitSystem

- [x] **Edit `src/Game.Main/InitSystem.cs`** — in the `Run` method, after the `CreateCharacterEntities(...)` call, add: `CreateCountryActionEntities(world, context, rng);`.

  Add `static void CreateCountryActionEntities(World world, GameLogicContext context, Random rng)`:
  - Load: `var countryActionConfig = context.CountryAction.Load();` — if `countryActionConfig.Actions.Count == 0`, return early.
  - Load: `var countryConfig = context.Country.Load();`
  - Load: `var characterConfig = context.Character.Load();`
  - Get orgId: `string orgId = context.InitialOrganizationId;` — if empty, return.
  - For each `CountryEntry entry` in `countryConfig.Countries` where `entry.IsAvailable`:
    - For each `CountryActionDefinition def` in `countryActionConfig.Actions`:
      - Determine targets. For `def.TargetRole == ""` (sphere_of_pressure): one target with `targetCharacterId = ""`.
      - For `def.TargetRole` in `{ "diplomacy_advisor", "economic_advisor", "military_advisor", "secret_advisor" }`:
        - Find characters with `Character.CountryId == entry.CountryId && Character.RoleId == def.TargetRole` via iteration over `Character` archetype. Collect `List<string> charIds`.
        - For each `charId` in `charIds`: that character is one target. If no characters found for that role, skip.
      - For `def.TargetRole == "ruler"`:
        - Find character with `Character.CountryId == entry.CountryId && Character.RoleId == "ruler"`. Collect charIds.
      - For each target (charId), create `def.DeckCopies` entities (e.g. 3):
        - `int e = world.Create();`
        - `world.Add(e, new CountryActionCard { OrgId = orgId, CountryId = entry.CountryId, ActionId = def.ActionId, TargetCharacterId = charId });`
        - If `def.PreDealtToHand && copyIndex == 0`: `world.Add(e, new InHand { SlotIndex = 0 });` (only the first copy of sphere_of_pressure gets pre-dealt).

  Note: `Character` entities are created in `CreateCharacterEntities` which runs before this new method, so characters are guaranteed to exist.

### Phase 7 — VisualState Extension

- [x] **Edit `src/Game.Main/VisualState.cs`** — add the following new classes before the `VisualState` class definition:

  ```
  CountryActionCardEntry — plain class, constructor-init:
    string ActionId
    int SlotIndex
    bool IsInHand
    string TargetCharacterId
    string TargetCharacterName   // resolved display name from locale keys
    float SuccessRate             // computed: base + influence/divisor
    bool IsRateDynamic            // true when successRateInfluenceDivisor > 0
    int InfluenceBase             // for tooltip: successRateBase as int %
    int InfluenceBonus            // for tooltip: contribution from current influence
    bool IsUnplayable
    string UnplayableReason
    bool IsOnCooldown
    DateTime CooldownEnd
  ```

  ```
  CountryActionsState : INotifyPropertyChanged:
    event PropertyChangedEventHandler? PropertyChanged
    IReadOnlyList<CountryActionCardEntry> Hand  (private set, init Array.Empty)
    IReadOnlyList<CountryActionCardEntry> Deck  (private set, init Array.Empty)
    int HandSize (private set)
    void Set(List<CountryActionCardEntry> hand, List<CountryActionCardEntry> deck, int handSize)
      → assigns, fires PropertyChanged
  ```

  In the `VisualState` class, add: `public CountryActionsState SelectedCountryActions { get; } = new CountryActionsState();`

### Phase 8 — VisualStateConverter

- [x] **Edit `src/Game.Main/VisualStateConverter.cs`** — add field `CountryActionConfig? _countryActionConfig;`.

  In the constructor `VisualStateConverter(VisualState state)`, add parameter `CountryActionConfig? countryActionConfig = null` and assign `_countryActionConfig = countryActionConfig;`.

  In `Update(...)`, after the `UpdateOrgActions(world)` call, add: `UpdateCountryActions(world);`.

  Add `void UpdateCountryActions(IReadOnlyWorld world)`:
  - If `!_state.SelectedCountry.IsValid || !_state.PlayerOrganization.IsValid || _countryActionConfig == null`:
    - `_state.SelectedCountryActions.Set(new List<CountryActionCardEntry>(), new List<CountryActionCardEntry>(), 0);` return.
  - `string orgId = _state.PlayerOrganization.OrgId;`
  - `string countryId = _state.SelectedCountry.CountryId;`
  - Compute `int orgInfluence`: iterate `InfluenceEffect` entities where `OrgId == orgId && CountryId == countryId`, sum Value.
  - Compute `int usedTotal`: iterate ALL `InfluenceEffect` entities where `CountryId == countryId`, sum Value.
  - Build char name lookup: iterate `Character` entities where `CountryId == countryId`, build `Dictionary<string, string[]> charNameKeys` keyed by CharacterId.
  - Build cooldown lookup: iterate `CountryActionCard + ActionCooldown` entities where `OrgId == orgId && CountryId == countryId`, build `Dictionary<(string actionId, string targetCharId), DateTime> cooldownMap`.
  - Iterate `CountryActionCard` entities (without `InHand` exclude) for (OrgId == orgId, CountryId == countryId):
    - Also iterate `CountryActionCard + InHand` for same (orgId, countryId) → hand entries.
    - To do this cleanly: two separate archetype queries:
      1. `int[] handReq = { TypeId<CountryActionCard>.Value, TypeId<InHand>.Value };` → hand list.
      2. `int[] deckReq = { TypeId<CountryActionCard>.Value };`, exclude `{ TypeId<InHand>.Value }` → deck list.
  - For each card (hand + deck), build a `CountryActionCardEntry`:
    - `def = _countryActionConfig.Find(card.ActionId)` — skip if def is null.
    - `float successRate = def.SuccessRateBase + (def.SuccessRateInfluenceDivisor > 0 ? orgInfluence / (float)def.SuccessRateInfluenceDivisor : 0f)`. Clamp to 1.0f.
    - `bool isDynamic = def.SuccessRateInfluenceDivisor > 0`.
    - `int influenceBase = (int)(def.SuccessRateBase * 100)`.
    - `int influenceBonus = isDynamic ? (int)(orgInfluence / (float)def.SuccessRateInfluenceDivisor * 100) : 0`.
    - Cooldown: check cooldownMap with key `(card.ActionId, card.TargetCharacterId)`.
    - `bool isUnplayable`: `orgInfluence < def.InfluenceThreshold` OR (`def.ActionId == "sphere_of_pressure" && usedTotal >= 100`) OR `isOnCooldown`.
    - `string unplayableReason`: if influence below threshold → use locale key `"action.country.unplayable.insufficient_influence"` format with threshold value (pass the threshold int, the view formats it). Store raw threshold in a separate field OR pass the reason string computed here. Since `VisualStateConverter` doesn't have locale access, store `UnplayableReason` as a key+arg composite or just store a code. Simplest: store the threshold value as part of the reason string like `"insufficient_influence:10"` and let the view parse it, OR store the numeric threshold in `CountryActionCardEntry` and let the view format. **Decision: add `int InfluenceThreshold` property to `CountryActionCardEntry` and set `UnplayableReason` to either `"pool_full"` or `"insufficient_influence"` (a code string). The view resolves the locale key from the code.** Add `int InfluenceThreshold` property to the entry.
    - `string targetCharacterName`: if `charNameKeys.TryGetValue(card.TargetCharacterId, out var keys)` → store the keys array. The view will call `loc.Get()` on them to build the name. Since VisualStateConverter doesn't know locale, store `string[] TargetCharacterNameKeys` instead of `string TargetCharacterName` on the entry. Rename the field in `CountryActionCardEntry` to `string[] TargetCharacterNameKeys`.
  - Sort hand by SlotIndex. Call `_state.SelectedCountryActions.Set(hand, deck, 3)`.

  **Correction to `CountryActionCardEntry` fields after the above:**
  - Replace `string TargetCharacterName` with `string[] TargetCharacterNameKeys`.
  - Add `int InfluenceThreshold`.
  - Keep `string UnplayableReason` as a code: `""`, `"pool_full"`, or `"insufficient_influence"`.

### Phase 9 — GameLogic

- [x] **Edit `src/Game.Main/GameLogic.cs`** — load CountryActionConfig: add field `CountryActionConfig _countryActionConfig = null!;`.

  In the constructor, after the line `ActionConfig = context.Action.Load();`, add:
  ```
  _countryActionConfig = context.CountryAction.Load();
  ```

  Expose as a public property: `public CountryActionConfig CountryActionConfig { get; private set; } = null!;` and assign it in the constructor: `CountryActionConfig = _countryActionConfig;`.

  Pass it to `VisualStateConverter`: change the constructor call from `new VisualStateConverter(VisualState)` to `new VisualStateConverter(VisualState, _countryActionConfig)`.

  In `Update(float deltaTime)`, after `OpinionSystem.Update(...)`, add:
  ```
  CountryActionSystem.TickCooldowns(_world, currentTime);
  ```

  After the existing `foreach (var cmd in _commandAccessor.ReadPlayActionCommand().AsSpan()) { ... }` block, add:
  ```
  foreach (var cmd in _commandAccessor.ReadPlayCountryActionCommand().AsSpan()) {
      lastActionId = cmd.ActionId;
      lastActionResult = CountryActionSystem.ProcessPlayCountryAction(
          _world, cmd, _countryActionConfig, currentTime, _rng);
  }
  ```
  Note: this reuses the same `lastActionResult`/`lastActionId` variables so the same `VisualState.LastAction.Set(...)` call picks up the result. Both org-action and country-action results flow through the same signal — this is fine for v1 since only one can be played per frame.

  Add `using GS.Game.Systems;` at the top if not already present (it is present).

### Phase 10 — Build DLLs

- [x] **Run `dotnet build src/GlobalStrategy.Core.sln -c Release`** — this rebuilds all DLLs into `Assets/Plugins/Core/`. The source generator will auto-generate `ReadPlayCountryActionCommand()` on `CommandAccessor`. After the build, verify the output includes `Game.Components.dll`, `Game.Configs.dll`, `Game.Commands.dll`, `Game.Systems.dll`, `Game.Main.dll`.

### Phase 11 — Config JSON TextAsset Registration

- [x] **Edit `Assets/Scripts/Unity/DI/GameLifetimeScope.cs`** — add a serialized field: `[SerializeField] TextAsset _countryActionConfigAsset;`.

  In `Configure(...)`, in the `var ctx = new GameLogicContext(...)` call, add a new named argument: `countryAction: _countryActionConfigAsset != null ? new TextAssetConfig<GS.Game.Configs.CountryActionConfig>(_countryActionConfigAsset) : null`.

  After the existing `builder.Register(c => c.Resolve<GameLogic>().ActionConfig, ...)` line, add:
  ```
  builder.Register(c => c.Resolve<GameLogic>().CountryActionConfig, Lifetime.Singleton);
  ```

### Phase 12 — Locale Keys

- [x] **Edit `Assets/Localization/en.asset`** — add the following locale keys (using the existing `.asset` text format). Locate the existing key block and append:

  ```
  action.sphere_of_pressure.name → "Sphere of Pressure"
  action.sphere_of_pressure.desc → "Dispatch your agents to consolidate the organisation's foothold. A show of coordinated strength may tip the balance of local loyalties."
  action.letter_of_commendation_diplomacy_advisor.name → "Diplomatic Dispatch"
  action.letter_of_commendation_diplomacy_advisor.desc → "A private communiqué delivered to the foreign minister's desk — cordial, well-argued, and impossible to ignore. Goodwill at the ministry opens corridors that force alone cannot."
  action.letter_of_commendation_economic_advisor.name → "Treasury Commission"
  action.letter_of_commendation_economic_advisor.desc → "A formal letter of recognition from the organisation, acknowledging the minister's fiscal acumen and proposing closer financial cooperation. Self-interest dressed as flattery."
  action.letter_of_commendation_military_advisor.name → "Campaign Commendation"
  action.letter_of_commendation_military_advisor.desc → "A letter extolling the general's past campaigns, delivered through discreet channels. Professional admiration is a currency soldiers understand."
  action.letter_of_commendation_secret_advisor.name → "Shadow Accord"
  action.letter_of_commendation_secret_advisor.desc → "A whispered arrangement, conveyed through cut-outs and sealed with mutual benefit. Some alliances are better left unwritten."
  action.royal_audience.name → "Royal Audience"
  action.royal_audience.desc → "Securing a private audience with the sovereign is a rare honour. The right words, delivered face to face, may sow the seeds of a lasting alliance."
  action.country.unplayable.pool_full → "No influence pool space remaining"
  action.country.unplayable.insufficient_influence → "Requires {0} influence"
  ```

- [x] **Edit `Assets/Localization/ru.asset`** — add the same keys with the same English strings as placeholder Russian translations (the same English text for now).

### Phase 12b — Card Artwork Generation

- [x] **Write `.tmp/images.json`** — create the batch input file with 6 entries, one per card action. Each entry has `"outputPath"`, `"size": "256x384"`, and `"prompt"`. Use 19th-century historical oil painting style consistent with existing card art (`discover_country.png`). Prompts:

  ```json
  [
    {
      "outputPath": "Assets/Textures/Actions/sphere_of_pressure.png",
      "size": "256x384",
      "prompt": "covert agents exchanging sealed documents in a candlelit back room, shadowy figures, political intrigue, 19th century, historical oil painting style, dark atmospheric background, highly detailed, dramatic lighting"
    },
    {
      "outputPath": "Assets/Textures/Actions/letter_of_commendation_diplomacy_advisor.png",
      "size": "256x384",
      "prompt": "ornate wax-sealed diplomatic letter resting on a mahogany desk, quill pen and inkwell, foreign ministry window in background, 19th century, historical oil painting style, formal composition, highly detailed"
    },
    {
      "outputPath": "Assets/Textures/Actions/letter_of_commendation_economic_advisor.png",
      "size": "256x384",
      "prompt": "formal letter of recognition on a treasury desk, gold coins and financial ledgers, classical columns in background, 19th century, historical oil painting style, warm golden tones, highly detailed"
    },
    {
      "outputPath": "Assets/Textures/Actions/letter_of_commendation_military_advisor.png",
      "size": "256x384",
      "prompt": "military commendation letter adorned with medals and campaign ribbons, battlefield map background, 19th century, historical oil painting style, patriotic composition, highly detailed"
    },
    {
      "outputPath": "Assets/Textures/Actions/letter_of_commendation_secret_advisor.png",
      "size": "256x384",
      "prompt": "shadowy figure passing a sealed envelope in a dimly lit corridor, clandestine meeting, hooded silhouette, 19th century, historical oil painting style, mysterious dark atmosphere, highly detailed"
    },
    {
      "outputPath": "Assets/Textures/Actions/royal_audience.png",
      "size": "256x384",
      "prompt": "diplomat bowing before a sovereign in an ornate throne room, gilded columns and red drapery, formal royal audience, 19th century, historical oil painting style, grand ceremonial composition, highly detailed"
    }
  ]
  ```

- [x] **Run image generation** — ensure ComfyUI is running at `http://127.0.0.1:8188`, then execute:
  ```powershell
  $env:PYTHONUTF8 = '1'; & ".venv\Scripts\python.exe" ".claude\generate_images_batch.py" ".tmp\images.json"
  ```
  Verify all 6 files appear under `Assets/Textures/Actions/`.

- [x] **Delete `.tmp/images.json`** — run `Remove-Item .tmp\images.json` as a separate PowerShell call after confirming all images were generated.

- [x] **Create `.meta` files for each generated PNG** — write 6 `.meta` files in `Assets/Textures/Actions/`, one per card image. Each file must be named `<actionId>.png.meta`. Use `spriteMode: 2` (Multiple) with an explicit sprite defined in `spriteSheet.sprites` — same format as `discover_country.png.meta`. Generate a unique 32-char lowercase hex GUID (texture GUID) and a unique negative int64 internalID (sprite sub-asset ID) for each entry; record them for use in the next step. Template (substitute the four placeholders per file):

  ```yaml
  fileFormatVersion: 2
  guid: <TEXTURE_GUID>
  TextureImporter:
    internalIDToNameTable:
    - first:
        213: <INTERNAL_ID>
      second: <actionId>_0
    externalObjects: {}
    serializedVersion: 13
    mipmaps:
      mipMapMode: 0
      enableMipMap: 0
      sRGBTexture: 1
      linearTexture: 0
      fadeOut: 0
      borderMipMap: 0
      mipMapsPreserveCoverage: 0
      alphaTestReferenceValue: 0.5
      mipMapFadeDistanceStart: 1
      mipMapFadeDistanceEnd: 3
    isReadable: 0
    streamingMipmaps: 0
    streamingMipmapsPriority: 0
    generateCubemap: 6
    cubemapConvolution: 0
    seamlessCubemap: 0
    textureFormat: 1
    maxTextureSize: 2048
    textureSettings:
      serializedVersion: 2
      filterMode: 1
      aniso: 1
      mipBias: 0
      wrapU: 1
      wrapV: 1
      wrapW: 1
    nPOTScale: 0
    lightmap: 0
    compressionQuality: 50
    spriteMode: 2
    spriteExtrude: 1
    spriteMeshType: 1
    alignment: 0
    spritePivot: {x: 0.5, y: 0.5}
    spritePixelsToUnits: 100
    spriteBorder: {x: 0, y: 0, z: 0, w: 0}
    spriteGenerateFallbackPhysicsShape: 1
    alphaUsage: 1
    alphaIsTransparency: 1
    spriteTessellationDetail: -1
    textureType: 8
    textureShape: 1
    singleChannelComponent: 0
    flipbookRows: 1
    flipbookColumns: 1
    maxTextureSizeSet: 0
    compressionQualitySet: 0
    textureFormatSet: 0
    ignorePngGamma: 0
    applyGammaDecoding: 0
    cookieLightType: 0
    platformSettings:
    - serializedVersion: 4
      buildTarget: DefaultTexturePlatform
      maxTextureSize: 2048
      resizeAlgorithm: 0
      textureFormat: -1
      textureCompression: 1
      compressionQuality: 50
      crunchedCompression: 0
      allowsAlphaSplitting: 0
      overridden: 0
      ignorePlatformSupport: 0
      androidETC2FallbackOverride: 0
      forceMaximumCompressionQuality_BC6H_BC7: 0
    spriteSheet:
      serializedVersion: 2
      sprites:
      - serializedVersion: 2
        name: <actionId>_0
        rect:
          serializedVersion: 2
          x: 0
          y: 0
          width: 256
          height: 384
        alignment: 0
        pivot: {x: 0.5, y: 0.5}
        border: {x: 0, y: 0, z: 0, w: 0}
        customData: 
        outline: []
        physicsShape: []
        tessellationDetail: -1
        bones: []
        spriteID: 00000000000000000000000000000000
        internalID: <INTERNAL_ID>
        vertices: []
        indices: 
        edges: []
        weights: []
      outline: []
      customData: 
      physicsShape: []
      bones: []
      spriteID: 
      internalID: 0
      vertices: []
      indices: 
      edges: []
      weights: []
      secondaryTextures: []
      spriteCustomMetadata:
        entries: []
      nameFileIdTable:
        <actionId>_0: <INTERNAL_ID>
    userData: 
    assetBundleName: 
    assetBundleVariant: 
  ```

- [x] **Edit `Assets/Configs/ActionVisualConfig.asset`** — append 6 new entries to the `entries:` list. Use the `<TEXTURE_GUID>` and `<INTERNAL_ID>` from each `.meta` file written above. Leave `backImage` referencing `{fileID: 0}` so the `defaultBackImage` applies for deck piles. Pattern (one entry per card):

  ```yaml
  - actionId: sphere_of_pressure
    frontImage: {fileID: <INTERNAL_ID>, guid: <TEXTURE_GUID>, type: 3}
    backImage: {fileID: 0}
  ```

  Add all 6 entries (`sphere_of_pressure`, `letter_of_commendation_diplomacy_advisor`, `letter_of_commendation_economic_advisor`, `letter_of_commendation_military_advisor`, `letter_of_commendation_secret_advisor`, `royal_audience`) in the same order as the `entries:` list.

### Phase 13 — CountryActionsView

- [x] **Create `Assets/Scripts/Unity/UI/CountryActionsView.cs`** — plain C# class (not MonoBehaviour) in namespace `GS.Unity.UI`. Model it on `OrgActionsView.cs`. Using: `System`, `System.Collections.Generic`, `UnityEngine`, `UnityEngine.UIElements`, `GS.Main`, `GS.Game.Configs`, `GS.Unity.Common`.

  Fields: `readonly VisualElement _handContainer`, `readonly ILocalization _loc`, `readonly CountryActionConfig _config`, `readonly ActionVisualConfig _visualConfig`, `readonly TooltipSystem _tooltip`.

  Properties: `public Action<string, string, VisualElement> OnCardClicked` (actionId, targetCharId, element), `public VisualElement DeckPileElement { get; private set; }`, `public bool SuppressRefresh { get; set; }`.

  Constructor: `CountryActionsView(VisualElement handContainer, ILocalization loc, CountryActionConfig config, ActionVisualConfig visualConfig, TooltipSystem tooltip)` — assign all fields. Add `using GS.Unity.Common;` for `ActionVisualConfig`.

  `public void Refresh(CountryActionsState state, CountryResourcesState resources)`:
  - If `SuppressRefresh`, return.
  - `_handContainer.Clear()`.
  - Add `BuildDeckPile(state.Deck.Count)`.
  - For each card in `state.Hand`, call `BuildHandCard(card, resources)` and add result.

  `VisualElement BuildHandCard(CountryActionCardEntry card, CountryResourcesState resources)`:
  - Create wrapper with class `"card-lift-wrapper"`.
  - Create `cardEl` with class `"action-card"`.
  - Add class `"action-card--unavailable"` if `card.IsUnplayable || card.IsOnCooldown`, else `"action-card--available"`.
  - Header `Label`: `_loc.Get(def.NameKey)` where `def = _config.Find(card.ActionId)`. Fallback to `card.ActionId`.
  - Art `VisualElement` with class `"action-card-art"`. Resolve sprite: `var sprite = _visualConfig?.FindFront(card.ActionId);` and if non-null set `art.style.backgroundImage = new StyleBackground(sprite);` — same pattern as `OrgActionsView.BuildHandCard`.
  - Body: description label, footer with success% label and cost label.
    - Success% label: `$"{(int)(card.SuccessRate * 100)}%"`. Add class `"action-card-success-pct"`.
    - If `card.IsRateDynamic`, register a tooltip on the success% label showing `"X% = Ybase% + Zbonus% from W influence"` using `card.InfluenceBase`, `card.InfluenceBonus`, and current influence value. To get current influence value: `card.InfluenceBase + card.InfluenceBonus` already represents the split; derive `int influenceUsed = card.SuccessRateInfluenceDivisor * card.InfluenceBonus / 100`. Actually, store the raw influence value separately: add `int CurrentOrgInfluence` to `CountryActionCardEntry` and set it in `VisualStateConverter`. Then the tooltip can say e.g. `$"{(int)(card.SuccessRate*100)}% = {card.InfluenceBase}% base + {card.InfluenceBonus}% from {card.CurrentOrgInfluence} influence"`.
    - Cost label: `$"{(int)def.GoldCost}"` + gold icon class. Check affordability: `resources?.Gold >= def.GoldCost` (or just compare against the resource list). Use same helper as `OrgActionsView.GetResourceValue`.
    - If `card.TargetCharacterNameKeys?.Length > 0`: add a secondary label for the target name. Build name string: `string.Join(" ", card.TargetCharacterNameKeys.Select(k => _loc.Get(k)))`.
  - Cooldown overlay: if `card.IsOnCooldown`: add a `VisualElement` overlay with class `"action-card-cooldown-overlay"` containing a `Label` with the formatted duration. Format duration via helper `FormatCooldown(DateTime end, DateTime now)`:
    - `TimeSpan span = end - now`. Use `DateTime.UtcNow` — but we don't have current time in the view. **Decision: add `DateTime CurrentTime` to `CountryActionsState` (set in `VisualStateConverter` from `gameTimeEntity`)**. Pass it through `Refresh`. Actually, add `public DateTime CurrentTime { get; private set; }` to `CountryActionsState` and set it in `VisualStateConverter.UpdateCountryActions` by passing `world.Get<GameTime>(gameTimeEntity).CurrentTime`. The `VisualStateConverter.UpdateCountryActions` call needs `gameTimeEntity` — pass it as a parameter from `Update(world, gameTimeEntity, ...)` which already has it.
    - `TimeSpan remaining = card.CooldownEnd - state.CurrentTime`. Format: days = `(int)remaining.TotalDays`. If `days >= 365`: `$"{days/365} year(s)"`. Else if `days >= 30`: `$"{days/30} month(s)"`. Else if `days >= 2`: `$"{days} days"`. Else if `days == 1`: `"1 day"`. Else: `"less than a day"`.
  - Unplayable reason label: if `card.IsUnplayable && !card.IsOnCooldown`: add a `Label` with class `"action-card-unplayable-reason"`. Resolve text: if `card.UnplayableReason == "pool_full"` → `_loc.Get("action.country.unplayable.pool_full")`; if `"insufficient_influence"` → `string.Format(_loc.Get("action.country.unplayable.insufficient_influence"), card.InfluenceThreshold)`.
  - Click handler: if `!card.IsUnplayable && !card.IsOnCooldown`:
    ```
    string capturedAction = card.ActionId;
    string capturedTarget = card.TargetCharacterId;
    cardEl.RegisterCallback<PointerUpEvent>(e => {
        if (e.button == 0 && cardEl.ContainsPoint(e.localPosition)) {
            OnCardClicked?.Invoke(capturedAction, capturedTarget, cardEl);
        }
    });
    ```
  - Register tooltip on wrapper for card description + success info. Use `_tooltip.RegisterTrigger(...)`.
  - Return wrapper.

  `VisualElement BuildDeckPile(int deckCount)` — copy from `OrgActionsView.BuildDeckPile`. Use `_visualConfig?.defaultBackImage` for the back sprite, same as `OrgActionsView`. Class names are the same.

  **Amend `CountryActionCardEntry`** to add `int CurrentOrgInfluence` field (set to orgInfluence in VisualStateConverter for every card in the country). Also **amend `CountryActionsState`** to add `DateTime CurrentTime` property.

  **Amend `VisualStateConverter.UpdateCountryActions`** signature to receive `int gameTimeEntity` (already available in `Update`). Update the `Update` method call: pass `gameTimeEntity` as a parameter to `UpdateCountryActions(world, gameTimeEntity)`. In `UpdateCountryActions`, read `DateTime currentTime = world.Get<GameTime>(gameTimeEntity).CurrentTime;` and set `_state.SelectedCountryActions.CurrentTime = currentTime` inside the `Set` call (update the `Set` method signature to `Set(List<...> hand, List<...> deck, int handSize, DateTime currentTime)`).

  `BuildDeckPile` should also set `DeckPileElement` as in `OrgActionsView`.

### Phase 14 — CountryInfo UXML

- [x] **Edit `Assets/UI/HUD/CountryInfo/CountryInfo.uxml`** — add an "actions-slide" element and an "actions-toggle-btn" button, mirroring the existing characters-slide and chars-toggle-btn. Insert after the `<ui:VisualElement name="characters-slide" ...>` block:

  ```xml
  <ui:VisualElement name="actions-slide" class="actions-slide">
      <ui:VisualElement name="actions-instance">
          <ui:VisualElement name="hand-container" class="hand-container" />
      </ui:VisualElement>
  </ui:VisualElement>
  ```

  Inside the `<ui:VisualElement name="country-bar" ...>` block, after the `chars-toggle-btn` Button, add:
  ```xml
  <ui:Button name="actions-toggle-btn" class="actions-toggle-btn gs-btn gs-btn--small">
      <ui:Label text="Actions &#x25B2;" picking-mode="Ignore" />
  </ui:Button>
  ```

### Phase 15 — CountryInfo USS

- [x] **Edit `Assets/UI/HUD/CountryInfo/CountryInfo.uss`** — add CSS rules for `actions-slide` following the same pattern as `characters-slide`. The slide should be hidden by default (`display: none` or use a class toggle, mirroring `characters-slide--open` pattern). Add:

  ```css
  .actions-slide {
      display: none;
      /* Same layout rules as characters-slide */
  }
  .actions-slide--open {
      display: flex;
  }
  .action-card-cooldown-overlay {
      position: absolute;
      bottom: 0;
      left: 0;
      right: 0;
      background-color: rgba(0, 0, 0, 0.6);
      padding: 4px;
  }
  .action-card-unplayable-reason {
      font-size: 12px;
      color: rgb(200, 80, 80);
      white-space: normal;
  }
  ```

  Check the existing `CountryInfo.uss` content and mirror the exact class names and pattern used by `characters-slide` and `characters-slide--open`.

### Phase 16 — CountryInfoView

- [x] **Edit `Assets/Scripts/Unity/UI/CountryInfoView.cs`** — add fields:
  - `readonly VisualElement _actionsSlide;`
  - `readonly Button _actionsToggleBtn;`
  - `readonly CountryActionsView _actionsView;`
  - `bool _actionsOpen;`
  - `public event Action<string, string, VisualElement> OnCountryActionCardClicked;`

  In the constructor, after the existing `_charsToggleBtn` assignment, add:
  - `_actionsSlide = root.Q("actions-slide");`
  - `_actionsToggleBtn = root.Q<Button>("actions-toggle-btn");`
  - If `_actionsSlide != null`: `_actionsSlide.pickingMode = PickingMode.Ignore;`
  - `var actionsInstance = root.Q("actions-instance");`
  - `if (actionsInstance != null) { _actionsView = new CountryActionsView(actionsInstance.Q("hand-container"), loc, countryActionConfig, actionVisualConfig, tooltip); _actionsView.OnCardClicked = (actionId, targetCharId, el) => OnCountryActionCardClicked?.Invoke(actionId, targetCharId, el); }`
  - Register `_actionsToggleBtn` with `PointerUpEvent` using `ContainsPoint` check (NOT `.clicked`): `_actionsToggleBtn?.RegisterCallback<PointerUpEvent>(e => { if (e.button == 0 && _actionsToggleBtn.ContainsPoint(e.localPosition)) { ToggleActions(); } });`

  Update the constructor signature to accept `CountryActionConfig countryActionConfig` and `ActionVisualConfig actionVisualConfig`. The caller (`HUDDocument.Awake`) creates `CountryInfoView`; update that call too.

  Add `SetActionsOpen(false)` in the `if (selected.CountryId != _lastCountryId)` block inside `Refresh`.

  In `Refresh(...)` signature, add `CountryActionsState countryActions` parameter. Call `_actionsView?.Refresh(countryActions, resources)`. Show/hide `_actionsToggleBtn` based on `countryActions.Hand.Count > 0 || countryActions.Deck.Count > 0`.

  Add `void ToggleActions()`:
  ```
  void ToggleActions() { SetActionsOpen(!_actionsOpen); }
  ```

  Add `void SetActionsOpen(bool open)`:
  - `_actionsOpen = open;`
  - If `_actionsSlide != null`: add/remove class `"actions-slide--open"`, set `pickingMode` to `Position`/`Ignore`.
  - Update button label: `open ? "Actions ▼" : "Actions ▲"`.
  - When opening actions, close chars: `if (open) { SetCharsOpen(false); }`.

  **Also fix existing `SetCharsOpen`** — when opening chars, close actions:
  - In `SetCharsOpen(bool open)`, at the top: `if (open) { SetActionsOpen(false); }`. Note: this would recurse (`SetActionsOpen(false)` calls... actually `SetActionsOpen(false)` does NOT call `SetCharsOpen` when `open == false`, so no recursion). Actually, `SetActionsOpen(false)` calls `if (open) { SetCharsOpen(false); }` which is `if (false) {...}` — safe. No infinite recursion.

  Change existing `_charsToggleBtn.clicked += ToggleChars;` to use `PointerUpEvent` pattern (the existing code uses `.clicked` — this must be changed to match the project's known `Button.clicked` bug workaround):
  - Remove the `_charsToggleBtn.clicked += ToggleChars;` line.
  - Replace with: `_charsToggleBtn?.RegisterCallback<PointerUpEvent>(e => { if (e.button == 0 && _charsToggleBtn.ContainsPoint(e.localPosition)) { ToggleChars(); } });`

### Phase 17 — HUDDocument

- [x] **Edit `Assets/Scripts/Unity/UI/HUDDocument.cs`** — inject `CountryActionConfig`:
  - Add field `CountryActionConfig _countryActionConfig;`.
  - In `[Inject] void Construct(...)`, add `CountryActionConfig countryActionConfig` parameter: `_countryActionConfig = countryActionConfig;`.
  - In `Awake()`, the `CountryInfoView` construction now needs `countryActionConfig`: pass `_countryActionConfig` as the new parameter.

  Subscribe to `_state.SelectedCountryActions.PropertyChanged`:
  - In `OnEnable()`, add: `_state.SelectedCountryActions.PropertyChanged += HandleCountryActionsChanged;`
  - In `OnDisable()`, add: `_state.SelectedCountryActions.PropertyChanged -= HandleCountryActionsChanged;`
  - Add handler: `void HandleCountryActionsChanged(object sender, PropertyChangedEventArgs e) => RefreshCountryViews();`

  In `RefreshCountryViews()`, update the `_countryInfo.Refresh(...)` call to add `_state.SelectedCountryActions` as an argument (matching the new signature).

  Wire up card click: in `Awake()` after creating `_countryInfo`, subscribe:
  ```
  _countryInfo.OnCountryActionCardClicked += OnCountryActionCardClicked;
  ```
  Unsubscribe in `OnDisable()`:
  ```
  _countryInfo.OnCountryActionCardClicked -= OnCountryActionCardClicked;
  ```
  Add handler:
  ```
  void OnCountryActionCardClicked(string actionId, string targetCharId, VisualElement el) {
      if (_state == null || !_state.PlayerOrganization.IsValid || !_state.SelectedCountry.IsValid) { return; }
      _commands.Push(new PlayCountryActionCommand {
          OrgId = _state.PlayerOrganization.OrgId,
          CountryId = _state.SelectedCountry.CountryId,
          ActionId = actionId,
          TargetCharacterId = targetCharId
      });
  }
  ```

### Phase 18 — OrgInfoDocument Bug Fix

- [x] **Edit `Assets/Scripts/Unity/UI/OrgInfoDocument.cs`** — apply mutual exclusion:
  - In `SetCharsOpen(bool open)`: at the very beginning of the method, add `if (open && _actionsOpen) { SetActionsOpen(false); }`.
  - In `SetActionsOpen(bool open)`: at the very beginning, add `if (open && _charsOpen) { SetCharsOpen(false); }`.
  - These guards prevent recursion (calling `SetActionsOpen(false)` when `_actionsOpen` is already false is a no-op).

### Phase 19 — Unity MCP Refresh

- [x] **Refresh Unity and check console** — after all file changes, call `refresh_unity` via MCP, then `read_console(types=["error"])`. Fix any compilation errors before proceeding.

### Phase 20 — Scene Wiring

- [x] **Assign `_countryActionConfigAsset` in the Inspector** — in the Unity Editor, select the `GameLifetimeScope` GameObject in the Game scene. In the Inspector, find the new `Country Action Config Asset` field and assign `Assets/Configs/country_action_config.json`.

---

## Section 2 — User Steps

1. **Verify Inspector wiring**: In Play mode, open the country info panel for any country. Confirm the "Actions" button appears and clicking it opens the 3-slot hand containing "Sphere of Pressure" at slot 0 with its generated artwork. Clicking the button again should close it.

2. **Test mutual exclusion in OrgInfoDocument**: Open the Org panel (player org info). Open the Characters slide. Click Actions — verify Characters closes. Open Characters again — verify Actions closes.

3. **Test Sphere of Pressure**: With a country selected showing ≥1 free influence pool point, click the "Sphere of Pressure" card. Confirm gold decreases by 200 and influence increases by up to 10 on success.

4. **Test influence pool full**: Use debug influence buttons to push a country's pool to 100. Verify Sphere of Pressure shows as greyed out with "No influence pool space remaining".

5. **Test Letter of Commendation draw**: Use the debug ChangeInfluence to set org influence in a country to ≥10. Advance game time. Verify an advisor card draws into the hand.

6. **Test cooldown display**: Play a card. Verify the card shows a cooldown label in the appropriate format (N months) and cannot be clicked.

7. **Test dynamic rate tooltip**: Hover over the success% on a Letter of Commendation or Royal Audience card. Verify the tooltip shows the breakdown formula.

8. **Test opinion effect**: Play a Letter of Commendation successfully. Open the Characters slide for the same country. Verify the targeted advisor's opinion score increased.

---

## Tests

Add `src/Game.Tests/CountryActionSystemTests.cs` with the following test cases:

- `sphere_of_pressure_pre_dealt_in_hand_on_init` — call `InitSystem.Update` with a test `GameLogicContext` that includes a `CountryActionConfig` with `sphere_of_pressure` (preDealtToHand=true). Assert exactly one `CountryActionCard` entity with `ActionId == "sphere_of_pressure"` has an `InHand` component for the player org and the test country. Assert the other 2 copies do NOT have `InHand`.

- `play_sphere_of_pressure_success_adds_influence` — set up world with org gold ≥ 200, no existing influence. Force success by seeding `Random` deterministically (or mock — use seed 0 if `rng.NextDouble() < 0.5` succeeds on first roll, otherwise use a known seed). Push `PlayCountryActionCommand` and call `CountryActionSystem.ProcessPlayCountryAction`. Assert `result.Executed == true`, `result.Success == true`, assert one `InfluenceEffect` entity exists for (orgId, countryId) with `Value == 10`.

- `play_sphere_of_pressure_does_not_exceed_pool` — pre-populate a 95-value `InfluenceEffect` for the country. Play the card with forced success. Assert the new influence added is ≤ 5 (capped at 100 total pool). Assert total influence = 100.

- `play_sphere_of_pressure_failure_no_influence` — force failure (seed rng to always return `> 0.5`). Assert no `InfluenceEffect` entity is created.

- `played_card_goes_on_cooldown` — after processing a play command, assert ALL 3 `CountryActionCard` entities for `sphere_of_pressure` (same orgId, countryId) have `ActionCooldown` component. Assert `CooldownEndTime == currentTime.AddMonths(1)`.

- `cooldown_card_not_eligible_for_draw` — add `ActionCooldown` to all copies of `sphere_of_pressure`. Play a card that vacates a slot. Assert no card is drawn (hand remains at 0 after vacating).

- `cooldown_expires_after_months` — add `ActionCooldown { CooldownEndTime = T }`. Call `CountryActionSystem.TickCooldowns(world, T)`. Assert `ActionCooldown` component is removed. Call with `T - 1 second` — assert component remains.

- `advisor_card_not_eligible_below_threshold` — set orgInfluence = 5, place advisor card in deck. Vacate a hand slot (play sphere_of_pressure). Assert advisor card is NOT drawn (hand slot remains empty after sphere play).

- `advisor_card_eligible_at_threshold` — set orgInfluence ≥ 10, place advisor card in deck. Vacate a slot. Assert the advisor card IS drawn into the vacated slot.

- `play_advisor_card_adds_opinion_on_success` — set up character entity with `CharacterOpinion`. Process `PlayCountryActionCommand` with `ActionId = "letter_of_commendation_diplomacy_advisor"` and `TargetCharacterId`. Force success. Assert `CharacterOpinion.ModifiersPerOrg[orgId]` contains one modifier with `SourceId == "letter_of_commendation"` and `Value == 50`.

- `royal_audience_requires_20_influence` — set orgInfluence = 15. Attempt to process `royal_audience`. Assert `result.Executed == false` (eligibility check blocks it, gold not deducted).

---

## Constitution Check

- **ECS for all game logic**: `CountryActionSystem` is a static class in `src/Game.Systems/`. All card state is in ECS components (`CountryActionCard`, `ActionCooldown`, `InHand`). No game state in MonoBehaviours. ✓
- **VContainer is the sole DI mechanism**: `CountryActionConfig` is injected via `GameLifetimeScope` into `HUDDocument` through VContainer. No `FindObjectOfType` or `new` for services. ✓
- **UI Toolkit only**: `CountryActionsView` is a plain C# class building `VisualElement` trees. No Canvas or UGUI. ✓
- **One `.asmdef` per feature folder**: No new feature folders are added (new C# files go into existing assemblies). No `.asmdef` changes needed. ✓
- **Code style**: Tabs, `_` prefix, braces on same line, braces always, `[Inject] void Construct`, `PointerUpEvent` + `ContainsPoint`, `PickingMode.Ignore` set explicitly. ✓
- **`Button.clicked` not used**: All click handling uses `PointerUpEvent` + `ContainsPoint`. The existing `_charsToggleBtn.clicked += ToggleChars` in `CountryInfoView` is explicitly replaced in Phase 16. ✓
- **`[Savable]` on `CountryActionCard` and `ActionCooldown`**: Both components are marked `[Savable]`, consistent with `ActionCard` and `InHand` which persist org card state. This ensures hand contents and cooldown timers survive save/load without extra work. ✓
- **Command discovery is automatic**: Adding `PlayCountryActionCommand : ICommand` to `src/Game.Commands/` is sufficient. The source generator scans all `ICommand` implementations on rebuild. ✓

---

Use /implement to start working on the plan or request changes.
