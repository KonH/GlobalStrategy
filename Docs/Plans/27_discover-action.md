# Plan 27: Discover Action

## Goal

Introduce an action card system with a single "Discover Country" action for the player org. Each action owner (org only for now) has a hand and a deck. The Illuminati starts with one Discover Country card in hand. Playing the card costs 100 gold, rolls against a 75% success rate, then on success marks a weighted-by-proximity country as discovered and reveals it on the map. Undiscovered countries are hidden from the map renderer. The full play sequence is animated with a card test view, random-number roll, fly text on success, and camera pan to the discovered country.

---

## Approach

1. Add `ActionConfig` and `EffectConfig` POCOs to `src/Game.Configs`.
2. Add `ActionCard` (hand + deck ECS components), `ActionOwner`, `IsDiscovered` ECS components to `src/Game.Components`.
3. Build the proximity map in `InitSystem` (stored on an ECS singleton entity). Expose `RebuildProximityMap()` on `GameLogic`.
4. Add `PlayActionCommand` to `src/Game.Commands`; process it in `GameLogic.Update` via a new `ActionSystem`.
5. Extend `VisualState` with `DiscoveredCountriesState`, `OrgActionsState`, and `LastActionResult`.
6. Extend `VisualStateConverter` to populate new states.
7. Extend `MapRenderer` / `MapLensApplier` to hide undiscovered countries.
8. Add "Actions" button to the OrgInfo panel; build `OrgActionsView` (hand + deck).
9. Create `ActionVisualConfig` ScriptableObject for card art.
10. Build the `CardPlayAnimator` MonoBehaviour for the full animated sequence.
11. Generate art: standard card back, Discover card front.
12. Wire everything in `GameLifetimeScope` and scene YAML.

---

## Numbered Steps

### Step 1 — Create `src/Game.Configs/EffectConfig.cs`

New file. Contains effect descriptors. For now only `DiscoverCountryEffect` exists but the structure is extensible.

```csharp
using System.Collections.Generic;

namespace GS.Game.Configs {
    public class EffectDefinition {
        public string EffectId    { get; set; } = "";
        public string EffectType  { get; set; } = ""; // e.g. "DiscoverCountry"
        public string NameKey     { get; set; } = "";
        public string DescKey     { get; set; } = "";
    }

    public class EffectConfig {
        public List<EffectDefinition> Effects { get; set; } = new();

        public EffectDefinition? Find(string effectId) {
            foreach (var e in Effects) {
                if (e.EffectId == effectId) return e;
            }
            return null;
        }
    }
}
```

---

### Step 2 — Create `src/Game.Configs/ActionConfig.cs`

New file. Describes actions and their hand-size defaults per owner type.

```csharp
using System.Collections.Generic;

namespace GS.Game.Configs {
    public class ActionCondition {
        public string ConditionType { get; set; } = "";
    }

    public class ActionPrice {
        public string ResourceId { get; set; } = "gold";
        public double Amount     { get; set; } = 0;
    }

    public class ActionDefinition {
        public string ActionId       { get; set; } = "";
        public string Rarity         { get; set; } = "Standard";
        public string NameKey        { get; set; } = "";
        public string DescKey        { get; set; } = "";
        public List<ActionCondition> Conditions { get; set; } = new();
        public List<ActionPrice> Prices { get; set; } = new();
        public List<string> EffectIds { get; set; } = new();
        public float SuccessRate     { get; set; } = 1.0f;  // 0..1
        // DiscoverCountryAction-specific:
        public float MinCountryChance { get; set; } = 0.01f; // floor weight fraction
    }

    public class ActionOwnerDefaults {
        public string OwnerType { get; set; } = ""; // "org", "country", "character"
        public int HandSize     { get; set; } = 0;
    }

    public class OrgActionPool {
        public string OrgId            { get; set; } = "";
        public List<string> ActionIds  { get; set; } = new();
    }

    public class ActionConfig {
        public List<ActionOwnerDefaults> Defaults  { get; set; } = new();
        public List<ActionDefinition>    Actions   { get; set; } = new();
        public List<OrgActionPool>       OrgPools  { get; set; } = new();

        public ActionDefinition? Find(string actionId) {
            foreach (var a in Actions) {
                if (a.ActionId == actionId) return a;
            }
            return null;
        }

        public int GetHandSize(string ownerType) {
            foreach (var d in Defaults) {
                if (d.OwnerType == ownerType) return d.HandSize;
            }
            return 0;
        }

        public List<string>? GetOrgPool(string orgId) {
            foreach (var p in OrgPools) {
                if (p.OrgId == orgId) return p.ActionIds;
            }
            return null;
        }
    }
}
```

---

### Step 3 — Create `Assets/Configs/effect_config.json`

```json
{
  "effects": [
    {
      "effectId": "discover_country",
      "effectType": "DiscoverCountry",
      "nameKey": "effect.discover_country.name",
      "descKey": "effect.discover_country.desc"
    }
  ]
}
```

---

### Step 4 — Create `Assets/Configs/action_config.json`

```json
{
  "defaults": [
    { "ownerType": "org",       "handSize": 1 },
    { "ownerType": "country",   "handSize": 0 },
    { "ownerType": "character", "handSize": 0 }
  ],
  "actions": [
    {
      "actionId": "discover_country",
      "rarity": "Standard",
      "nameKey": "action.discover_country.name",
      "descKey": "action.discover_country.desc",
      "conditions": [],
      "prices": [{ "resourceId": "gold", "amount": 100.0 }],
      "effectIds": ["discover_country"],
      "successRate": 0.75,
      "minCountryChance": 0.01
    }
  ],
  "orgPools": [
    {
      "orgId": "Illuminati",
      "actionIds": ["discover_country"]
    }
  ]
}
```

---

### Step 5 — Create `src/Game.Components/ActionCard.cs`

One entity per card slot (filled = in deck or hand).

```csharp
namespace GS.Game.Components {
    [Savable]
    public struct ActionCard {
        public string ActionId;
        public string OwnerId;
    }
}
```

---

### Step 6 — Create `src/Game.Components/InHand.cs`

Tag component added to `ActionCard` entities that are currently in the owner's hand.

```csharp
namespace GS.Game.Components {
    [Savable]
    public struct InHand {
        public int SlotIndex;
    }
}
```

---

### Step 7 — Create `src/Game.Components/ActionOwner.cs`

One entity per owner. Tracks current hand size limit.

```csharp
namespace GS.Game.Components {
    [Savable]
    public struct ActionOwner {
        public string OwnerId;
        public string OwnerType; // "org"
        public int    HandSize;
    }
}
```

---

### Step 8 — Create `src/Game.Components/IsDiscovered.cs`

Tag component on country entities to mark them as discovered by the player.

```csharp
namespace GS.Game.Components {
    [Savable]
    public struct IsDiscovered { }
}
```

---

### Step 9 — Create `src/Game.Components/ProximityMapData.cs`

Singleton component holding the precomputed proximity distances.

```csharp
using System.Collections.Generic;

namespace GS.Game.Components {
    // Not [Savable] — rebuilt at startup from config; no need to persist.
    public struct ProximityMapData {
        // Key: (countryIdA, countryIdB) where A < B lexicographically for deduplication.
        // Value: minimum distance between edge-point samples of any feature polygon rings.
        public Dictionary<(string, string), float> Distances;
    }
}
```

---

### Step 10 — `src/Game.Configs/GameLogicContext.cs`: add `Action` and `Effect` config sources

Add two new `IConfigSource<T>` properties and constructor parameters. Use null-safe defaults (empty configs) to keep backward compatibility.

Add to the constructor parameter list (after `character`):
```
IConfigSource<ActionConfig>? action = null,
IConfigSource<EffectConfig>? effect = null
```

Add properties:
```csharp
public IConfigSource<ActionConfig> Action { get; }
public IConfigSource<EffectConfig> Effect { get; }
```

Wire in constructor body:
```csharp
Action = action ?? new EmptyActionConfig();
Effect = effect ?? new EmptyEffectConfig();
```

Add inner sealed classes:
```csharp
sealed class EmptyActionConfig : IConfigSource<ActionConfig> {
    public ActionConfig Load() => new ActionConfig();
}
sealed class EmptyEffectConfig : IConfigSource<EffectConfig> {
    public EffectConfig Load() => new EffectConfig();
}
```

---

### Step 11 — `src/Game.Main/InitSystem.cs`: add `CreateActionEntities()` and proximity map

**11a — Proximity map entity**

After the `orgEntry != null` block (line 79) and before `CreateOrgCharacterEntities`, call:
```csharp
BuildProximityMap(world, context);
```

Add static method `BuildProximityMap`:

```csharp
internal static void BuildProximityMap(World world, GameLogicContext context) {
    // Destroy existing ProximityMapData entity if rebuilding
    int[] pmReq = { TypeId<ProximityMapData>.Value };
    var toDestroy = new System.Collections.Generic.List<int>();
    foreach (var arch in world.GetMatchingArchetypes(pmReq, null)) {
        for (int i = 0; i < arch.Count; i++) {
            toDestroy.Add(arch.Entities[i]);
        }
    }
    foreach (int e in toDestroy) { world.Destroy(e); }

    var countryConfig = context.Country.Load();
    var geoJson = context.GeoJson.Load();

    // Build featureId → list of ring points lookup from GeoJson.
    // GeoJsonConfig only stores Name/PartOf; actual geometry comes from MapFeature objects
    // parsed via Core.Map.GeoJsonParser. However GameLogicContext uses IConfigSource<GeoJsonConfig>
    // which only gives feature names, not geometry. The raw geometry is available through
    // a dedicated IConfigSource<List<MapFeature>> that must be added in Step 12.
    var featureGeometry = context.MapGeometry?.Load();
    var distances = new System.Collections.Generic.Dictionary<(string, string), float>();

    if (featureGeometry != null) {
        // Build lookup: mapFeatureId → sampled outer-ring points
        var featurePoints = BuildFeaturePointsLookup(featureGeometry);

        var entries = new System.Collections.Generic.List<CountryEntry>();
        foreach (var e in countryConfig.Countries) {
            if (e.IsAvailable) { entries.Add(e); }
        }

        for (int i = 0; i < entries.Count; i++) {
            for (int j = i + 1; j < entries.Count; j++) {
                float dist = ComputeMinDistance(entries[i], entries[j], featurePoints);
                string a = entries[i].CountryId;
                string b = entries[j].CountryId;
                if (string.CompareOrdinal(a, b) > 0) { var tmp = a; a = b; b = tmp; }
                distances[(a, b)] = dist;
            }
        }
    }

    int pmEntity = world.Create();
    world.Add(pmEntity, new ProximityMapData { Distances = distances });
}

static System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<GS.Core.Map.Vector2d>>
    BuildFeaturePointsLookup(System.Collections.Generic.List<GS.Core.Map.MapFeature> features) {
    var lookup = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<GS.Core.Map.Vector2d>>();
    foreach (var f in features) {
        var pts = new System.Collections.Generic.List<GS.Core.Map.Vector2d>();
        foreach (var poly in f.Polygons) {
            if (poly.Rings.Count == 0) { continue; }
            var ring = poly.Rings[0]; // outer ring only
            // Sample every Nth point for performance (N=4)
            for (int k = 0; k < ring.Points.Count; k += 4) {
                pts.Add(ring.Points[k]);
            }
        }
        lookup[f.Id] = pts;
    }
    return lookup;
}

static float ComputeMinDistance(
    CountryEntry a, CountryEntry b,
    System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<GS.Core.Map.Vector2d>> featurePoints) {
    float minDist = float.MaxValue;
    var aIds = new System.Collections.Generic.List<string>(a.MainMapFeatureIds);
    foreach (var s in a.SecondaryMapFeatureIds) { aIds.Add(s); }
    var bIds = new System.Collections.Generic.List<string>(b.MainMapFeatureIds);
    foreach (var s in b.SecondaryMapFeatureIds) { bIds.Add(s); }

    foreach (var aId in aIds) {
        if (!featurePoints.TryGetValue(aId, out var aPts)) { continue; }
        foreach (var bId in bIds) {
            if (!featurePoints.TryGetValue(bId, out var bPts)) { continue; }
            foreach (var ap in aPts) {
                foreach (var bp in bPts) {
                    float dx = (float)(ap.Lon - bp.Lon);
                    float dy = (float)(ap.Lat - bp.Lat);
                    float d = dx * dx + dy * dy;
                    if (d < minDist) { minDist = d; }
                }
            }
        }
    }
    return minDist == float.MaxValue ? 1e9f : (float)System.Math.Sqrt(minDist);
}
```

Note: `Vector2d` fields are `Lon` and `Lat` (double), confirmed from `src/Core.Map/Map/Vector2d.cs`.

**11b — Action card entities**

After `CreateOrgCharacterEntities`, call:
```csharp
CreateActionEntities(world, context, rng);
```

Add static method:

```csharp
static void CreateActionEntities(World world, GameLogicContext context, Random rng) {
    var actionConfig = context.Action.Load();
    string orgId = context.InitialOrganizationId;
    if (string.IsNullOrEmpty(orgId)) { return; }

    int handSize = actionConfig.GetHandSize("org");
    if (handSize <= 0) { return; }

    var pool = actionConfig.GetOrgPool(orgId);
    if (pool == null || pool.Count == 0) { return; }

    // Create ActionOwner entity
    int ownerEntity = world.Create();
    world.Add(ownerEntity, new ActionOwner {
        OwnerId   = orgId,
        OwnerType = "org",
        HandSize  = handSize
    });

    // Create one ActionCard per action in pool, placed into deck (no InHand initially)
    for (int i = 0; i < pool.Count; i++) {
        int cardEntity = world.Create();
        world.Add(cardEntity, new ActionCard {
            ActionId = pool[i],
            OwnerId  = orgId
        });
    }

    // Draw up to handSize cards into hand by random selection from the deck bucket
    var deckEntities = new System.Collections.Generic.List<int>();
    int[] cardReq = { TypeId<ActionCard>.Value };
    foreach (var arch in world.GetMatchingArchetypes(cardReq, null)) {
        ActionCard[] cards = arch.GetColumn<ActionCard>();
        int count = arch.Count;
        for (int i = 0; i < count; i++) {
            if (cards[i].OwnerId == orgId) { deckEntities.Add(arch.Entities[i]); }
        }
    }
    // Shuffle and draw
    for (int i = deckEntities.Count - 1; i > 0; i--) {
        int j = rng.Next(i + 1);
        var tmp = deckEntities[i]; deckEntities[i] = deckEntities[j]; deckEntities[j] = tmp;
    }
    for (int slot = 0; slot < handSize && slot < deckEntities.Count; slot++) {
        world.Add(deckEntities[slot], new InHand { SlotIndex = slot });
    }

    // Mark initially discovered countries
    DiscoverInitialCountries(world, context);
}

static void DiscoverInitialCountries(World world, GameLogicContext context) {
    var toDiscover = new System.Collections.Generic.HashSet<string>();

    if (!string.IsNullOrEmpty(context.InitialPlayerCountryId)) {
        toDiscover.Add(context.InitialPlayerCountryId);
    }

    // Org HQ country is also discovered at start
    if (!string.IsNullOrEmpty(context.InitialOrganizationId)) {
        var orgConfig = context.Organization.Load();
        var orgEntry = orgConfig.FindById(context.InitialOrganizationId);
        if (orgEntry != null && !string.IsNullOrEmpty(orgEntry.HqCountryId)) {
            toDiscover.Add(orgEntry.HqCountryId);
        }
    }

    int[] countryReq = { TypeId<Country>.Value };
    foreach (var arch in world.GetMatchingArchetypes(countryReq, null)) {
        Country[] countries = arch.GetColumn<Country>();
        int count = arch.Count;
        for (int i = 0; i < count; i++) {
            if (toDiscover.Contains(countries[i].CountryId)) {
                world.Add(arch.Entities[i], new IsDiscovered());
            }
        }
    }
}
```

---

### Step 12 — `src/Game.Configs/GameLogicContext.cs`: add `MapGeometry` config source

The proximity builder needs parsed `MapFeature` objects (from `Core.Map`), not just the raw JSON feature-name list that `GeoJsonConfig` provides. Add a new optional source:

```csharp
public IConfigSource<System.Collections.Generic.List<GS.Core.Map.MapFeature>>? MapGeometry { get; }
```

Add constructor parameter:
```
IConfigSource<System.Collections.Generic.List<GS.Core.Map.MapFeature>>? mapGeometry = null
```

Wire:
```csharp
MapGeometry = mapGeometry;
```

This stays null in tests that don't need proximity. The Unity `GameLifetimeScope` will wire it by loading the GeoJSON text asset through `Core.Map.GeoJsonParser`.

---

### Step 13 — Create `src/Game.Commands/PlayActionCommand.cs`

```csharp
namespace GS.Game.Commands {
    public struct PlayActionCommand : ICommand {
        public string OwnerId;
        public string ActionId;
    }
}
```

---

### Step 14 — Create `src/Game.Systems/ActionSystem.cs`

Pure C# system. Processes `PlayActionCommand`, checks affordability, rolls, applies effect, rotates hand.

```csharp
using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
    public static class ActionSystem {
        public struct ActionResult {
            public bool Executed;
            public bool Success;
        }

        public static ActionResult ProcessPlayAction(
            World world,
            PlayActionCommand cmd,
            ActionConfig actionConfig,
            int proximityEntity,
            Random rng) {
            var result = new ActionResult();
            if (string.IsNullOrEmpty(cmd.OwnerId)) { return result; }

            var actionDef = actionConfig.Find(cmd.ActionId);
            if (actionDef == null) { return result; }

            // Check and deduct all prices atomically
            if (!CanAfford(world, cmd.OwnerId, actionDef.Prices)) { return result; }
            DeductPrices(world, cmd.OwnerId, actionDef.Prices);

            result.Executed = true;

            // Roll success
            float roll = (float)rng.NextDouble();
            result.Success = roll < actionDef.SuccessRate;

            // Move card from hand back to deck
            ReturnCardToDeck(world, cmd.OwnerId, cmd.ActionId);

            // Draw new card (random pick from deck bucket)
            DrawCard(world, cmd.OwnerId, rng);

            if (result.Success) {
                ApplyDiscoverCountry(world, cmd.OwnerId, actionDef, proximityEntity, rng);
            }

            return result;
        }

        static bool CanAfford(World world, string ownerId, List<ActionPrice> prices) {
            foreach (var price in prices) {
                if (!HasResource(world, ownerId, price.ResourceId, price.Amount)) { return false; }
            }
            return true;
        }

        static bool HasResource(World world, string ownerId, string resourceId, double amount) {
            int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
            foreach (var arch in world.GetMatchingArchetypes(req, null)) {
                ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
                Resource[] resources = arch.GetColumn<Resource>();
                int count = arch.Count;
                for (int i = 0; i < count; i++) {
                    if (owners[i].OwnerId != ownerId || resources[i].ResourceId != resourceId) { continue; }
                    return resources[i].Value >= amount;
                }
            }
            return false;
        }

        static void DeductPrices(World world, string ownerId, List<ActionPrice> prices) {
            foreach (var price in prices) {
                int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
                foreach (var arch in world.GetMatchingArchetypes(req, null)) {
                    ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
                    Resource[] resources = arch.GetColumn<Resource>();
                    int count = arch.Count;
                    for (int i = 0; i < count; i++) {
                        if (owners[i].OwnerId != ownerId || resources[i].ResourceId != price.ResourceId) { continue; }
                        resources[i].Value -= price.Amount;
                        break;
                    }
                }
            }
        }

        static void ReturnCardToDeck(World world, string ownerId, string actionId) {
            int[] req = { TypeId<ActionCard>.Value, TypeId<InHand>.Value };
            foreach (var arch in world.GetMatchingArchetypes(req, null)) {
                ActionCard[] cards = arch.GetColumn<ActionCard>();
                int count = arch.Count;
                for (int i = 0; i < count; i++) {
                    if (cards[i].OwnerId != ownerId || cards[i].ActionId != actionId) { continue; }
                    world.Remove<InHand>(arch.Entities[i]);
                    return;
                }
            }
        }

        static void DrawCard(World world, string ownerId, Random rng) {
            // Find ActionOwner handSize
            int handSize = 1;
            int[] ownerReq = { TypeId<ActionOwner>.Value };
            foreach (var arch in world.GetMatchingArchetypes(ownerReq, null)) {
                ActionOwner[] owners = arch.GetColumn<ActionOwner>();
                int count = arch.Count;
                for (int i = 0; i < count; i++) {
                    if (owners[i].OwnerId == ownerId) { handSize = owners[i].HandSize; break; }
                }
            }

            // Count current hand slots occupied
            int currentHand = 0;
            int[] handReq = { TypeId<ActionCard>.Value, TypeId<InHand>.Value };
            foreach (var arch in world.GetMatchingArchetypes(handReq, null)) {
                ActionCard[] cards = arch.GetColumn<ActionCard>();
                int count = arch.Count;
                for (int i = 0; i < count; i++) {
                    if (cards[i].OwnerId == ownerId) { currentHand++; }
                }
            }

            int toDraw = handSize - currentHand;
            if (toDraw <= 0) { return; }

            // Collect deck cards (not in hand) — deck is a random bucket
            int[] deckReq = { TypeId<ActionCard>.Value };
            int[] excludeHand = { TypeId<InHand>.Value };
            var deckEntities = new List<int>();
            foreach (var arch in world.GetMatchingArchetypes(deckReq, excludeHand)) {
                ActionCard[] cards = arch.GetColumn<ActionCard>();
                int count = arch.Count;
                for (int i = 0; i < count; i++) {
                    if (cards[i].OwnerId == ownerId) { deckEntities.Add(arch.Entities[i]); }
                }
            }

            // Shuffle the bucket and draw
            for (int i = deckEntities.Count - 1; i > 0; i--) {
                int j = rng.Next(i + 1);
                var tmp = deckEntities[i]; deckEntities[i] = deckEntities[j]; deckEntities[j] = tmp;
            }

            int slot = currentHand;
            for (int k = 0; k < toDraw && k < deckEntities.Count; k++) {
                world.Add(deckEntities[k], new InHand { SlotIndex = slot++ });
            }
        }

        static void ApplyDiscoverCountry(
            World world,
            string ownerId,
            ActionDefinition actionDef,
            int proximityEntity,
            Random rng) {

            // Find player country (used as origin for proximity)
            string playerCountryId = FindPlayerCountryId(world);

            // Collect undiscovered countries
            int[] countryReq = { TypeId<Country>.Value };
            int[] hasDiscovered = { TypeId<IsDiscovered>.Value };
            var undiscovered = new List<string>();
            foreach (var arch in world.GetMatchingArchetypes(countryReq, hasDiscovered)) {
                // has IsDiscovered — skip
            }
            // Need countries WITHOUT IsDiscovered
            // Use exclude pattern: query Country without IsDiscovered
            foreach (var arch in world.GetMatchingArchetypes(countryReq, hasDiscovered)) {
                // skip discovered
            }
            // Iterate all Country archetypes, build discovered set first
            var discoveredSet = new HashSet<string>();
            int[] discReq = { TypeId<Country>.Value, TypeId<IsDiscovered>.Value };
            foreach (var arch in world.GetMatchingArchetypes(discReq, null)) {
                Country[] cs = arch.GetColumn<Country>();
                for (int i = 0; i < arch.Count; i++) { discoveredSet.Add(cs[i].CountryId); }
            }
            var allCountries = new List<(int entity, string countryId)>();
            foreach (var arch in world.GetMatchingArchetypes(countryReq, null)) {
                Country[] cs = arch.GetColumn<Country>();
                for (int i = 0; i < arch.Count; i++) {
                    if (!discoveredSet.Contains(cs[i].CountryId)) {
                        allCountries.Add((arch.Entities[i], cs[i].CountryId));
                    }
                }
            }
            if (allCountries.Count == 0) { return; }

            // Build weights using proximity
            ProximityMapData pm = default;
            bool hasPm = false;
            if (proximityEntity >= 0) {
                pm = world.Get<ProximityMapData>(proximityEntity);
                hasPm = true;
            }

            float totalWeight = 0f;
            var weights = new float[allCountries.Count];
            float minChance = actionDef.MinCountryChance;

            for (int i = 0; i < allCountries.Count; i++) {
                string b = allCountries[i].countryId;
                float w = 0f;
                if (hasPm && pm.Distances != null && !string.IsNullOrEmpty(playerCountryId)) {
                    string a = playerCountryId;
                    string ka = string.CompareOrdinal(a, b) <= 0 ? a : b;
                    string kb = string.CompareOrdinal(a, b) <= 0 ? b : a;
                    if (pm.Distances.TryGetValue((ka, kb), out float d)) {
                        w = d < 0.0001f ? 1e6f : 1f / d;
                    } else {
                        w = 1f;
                    }
                } else {
                    w = 1f;
                }
                weights[i] = w;
                totalWeight += w;
            }

            // Apply minChance floor
            float minWeight = minChance * totalWeight;
            bool changed = true;
            while (changed) {
                changed = false;
                float newTotal = 0f;
                for (int i = 0; i < weights.Length; i++) { newTotal += weights[i]; }
                for (int i = 0; i < weights.Length; i++) {
                    if (weights[i] < minChance * newTotal) {
                        weights[i] = minChance * newTotal;
                        changed = true;
                    }
                }
            }

            // Re-normalise and pick
            float wTotal = 0f;
            for (int i = 0; i < weights.Length; i++) { wTotal += weights[i]; }
            float pick = (float)rng.NextDouble() * wTotal;
            float acc = 0f;
            int chosen = 0;
            for (int i = 0; i < weights.Length; i++) {
                acc += weights[i];
                if (pick <= acc) { chosen = i; break; }
            }

            // Apply IsDiscovered; VisualStateConverter will detect the diff
            world.Add(allCountries[chosen].entity, new IsDiscovered());
        }

        static string FindPlayerCountryId(World world) {
            int[] req = { TypeId<Country>.Value, TypeId<Player>.Value };
            foreach (var arch in world.GetMatchingArchetypes(req, null)) {
                if (arch.Count > 0) {
                    return arch.GetColumn<Country>()[0].CountryId;
                }
            }
            return "";
        }
    }
}
```

Note: `World.GetMatchingArchetypes` signature takes `(int[] required, int[]? excluded)`. Use `null` for no exclusion. For "cards NOT in hand", pass `new[] { TypeId<InHand>.Value }` as the excluded parameter.

---

### Step 15 — `src/Game.Main/GameLogic.cs`: add proximity entity field, expose `RebuildProximityMap()`, process `PlayActionCommand`

**15a — New field:**
```csharp
int _proximityEntity = -1;
ActionConfig _actionConfig = null!;
```

**15b — In constructor, load `ActionConfig`:**
```csharp
_actionConfig = context.Action.Load();
```

**15c — Expose as public property** (for DI registration in `GameLifetimeScope`):
```csharp
public ActionConfig ActionConfig { get; private set; } = null!;
```

Wire in constructor:
```csharp
ActionConfig = context.Action.Load();
```

**15d — `RefreshSingletonEntities()`:** add:
```csharp
_proximityEntity = FindEntityWith<ProximityMapData>();
```

But `ProximityMapData` has a `Dictionary` field (reference type) so `FindEntityWith<T>()` using column access is fine.

**15e — `RebuildProximityMap()` public method:**
```csharp
public void RebuildProximityMap() {
    InitSystem.BuildProximityMap(_world, _context);
    _proximityEntity = FindEntityWith<ProximityMapData>();
}
```

**15f — In `Update()`, after the debug command blocks, add:**
```csharp
var lastActionResult = new ActionSystem.ActionResult();
foreach (var cmd in _commandAccessor.ReadPlayActionCommand().AsSpan()) {
    lastActionResult = ActionSystem.ProcessPlayAction(
        _world, cmd, _actionConfig, _proximityEntity, _rng);
}
if (lastActionResult.Executed) {
    VisualState.LastAction.Set(lastActionResult.Success);
}
```

---

### Step 16 — `src/Game.Main/VisualState.cs`: add `OrgActionsState`, `LastActionResult`, `DiscoveredCountriesState`

**16a — `DiscoveredCountriesState`:**
```csharp
public class DiscoveredCountriesState : INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;
    // HashSet used directly (not IReadOnlySet<T> — that interface requires .NET 5+, not netstandard2.1).
    public System.Collections.Generic.HashSet<string> CountryIds { get; private set; } = new System.Collections.Generic.HashSet<string>();
    // Set by VisualStateConverter when a new country enters the discovered set.
    // Read by CardPlayAnimator to know which country to pan to; cleared after animation.
    public string RecentlyDiscovered { get; private set; } = "";

    public void Set(System.Collections.Generic.HashSet<string> ids, string recentlyDiscovered = "") {
        CountryIds = ids;
        RecentlyDiscovered = recentlyDiscovered;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public void ClearRecentlyDiscovered() {
        RecentlyDiscovered = "";
    }
}
```

**16b — `ActionCardEntry`:**
```csharp
public class ActionCardEntry {
    public string ActionId   { get; }
    public int    SlotIndex  { get; }  // -1 = in deck
    public bool   IsInHand   { get; }
    public ActionCardEntry(string actionId, int slotIndex, bool isInHand) {
        ActionId = actionId; SlotIndex = slotIndex; IsInHand = isInHand;
    }
}
```

**16c — `OrgActionsState`:**
```csharp
public class OrgActionsState : INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<ActionCardEntry> Hand  { get; private set; } = Array.Empty<ActionCardEntry>();
    public IReadOnlyList<ActionCardEntry> Deck  { get; private set; } = Array.Empty<ActionCardEntry>();
    public int HandSize { get; private set; }
    public void Set(System.Collections.Generic.List<ActionCardEntry> hand,
                    System.Collections.Generic.List<ActionCardEntry> deck,
                    int handSize) {
        Hand = hand; Deck = deck; HandSize = handSize;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
```

**16d — `LastActionResultState`:**

Generic signal: "an action was just played, here is the outcome". Effect-specific data (e.g. which country was discovered) is tracked through dedicated state changes (`DiscoveredCountriesState.RecentlyDiscovered`), not here. This keeps the struct stable as more action types are added.

```csharp
public class LastActionResultState : INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool HasResult { get; private set; }
    public bool Success   { get; private set; }
    public void Set(bool success) {
        HasResult = true; Success = success;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
    public void Clear() {
        HasResult = false; Success = false;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
```

**16e — In `VisualState` class, add properties:**
```csharp
public DiscoveredCountriesState DiscoveredCountries { get; } = new DiscoveredCountriesState();
public OrgActionsState PlayerOrgActions             { get; } = new OrgActionsState();
public LastActionResultState LastAction             { get; } = new LastActionResultState();
```

---

### Step 17 — `src/Game.Main/VisualStateConverter.cs`: add `UpdateDiscoveredCountries()` and `UpdateOrgActions()`

**17a — `UpdateDiscoveredCountries()`:**

The converter keeps a `_previousDiscoveredIds` field to detect newly discovered countries each frame. When a new ID appears it is passed as `recentlyDiscovered` to `DiscoveredCountriesState.Set()`. The animator later calls `ClearRecentlyDiscovered()` once it has consumed the value. Adding new action types that discover things simply requires marking `IsDiscovered` — this diff logic picks it up automatically.

Add field to `VisualStateConverter`:
```csharp
readonly System.Collections.Generic.HashSet<string> _previousDiscoveredIds = new();
```

Add method:
```csharp
void UpdateDiscoveredCountries(IReadOnlyWorld world) {
    var ids = new System.Collections.Generic.HashSet<string>();
    int[] req = { TypeId<Country>.Value, TypeId<IsDiscovered>.Value };
    foreach (var arch in world.GetMatchingArchetypes(req, null)) {
        Country[] cs = arch.GetColumn<Country>();
        for (int i = 0; i < arch.Count; i++) {
            ids.Add(cs[i].CountryId);
        }
    }

    // Find any country that just became discovered this frame
    string recently = "";
    foreach (var id in ids) {
        if (!_previousDiscoveredIds.Contains(id)) { recently = id; break; }
    }
    _previousDiscoveredIds.Clear();
    foreach (var id in ids) { _previousDiscoveredIds.Add(id); }

    // Preserve a pending RecentlyDiscovered value until the animator explicitly clears it.
    // Without this guard, the next converter frame would overwrite it with "" before the
    // coroutine has a chance to read it.
    string pendingRecently = recently != "" ? recently : _state.DiscoveredCountries.RecentlyDiscovered;
    _state.DiscoveredCountries.Set(ids, pendingRecently);
}
```

**17b — `UpdateOrgActions()`:**
```csharp
void UpdateOrgActions(IReadOnlyWorld world) {
    if (!_state.PlayerOrganization.IsValid) {
        _state.PlayerOrgActions.Set(
            new System.Collections.Generic.List<ActionCardEntry>(),
            new System.Collections.Generic.List<ActionCardEntry>(), 0);
        return;
    }
    string orgId = _state.PlayerOrganization.OrgId;

    int handSize = 1;
    int[] ownerReq = { TypeId<ActionOwner>.Value };
    foreach (var arch in world.GetMatchingArchetypes(ownerReq, null)) {
        ActionOwner[] owners = arch.GetColumn<ActionOwner>();
        for (int i = 0; i < arch.Count; i++) {
            if (owners[i].OwnerId == orgId) { handSize = owners[i].HandSize; break; }
        }
    }

    var hand = new System.Collections.Generic.List<ActionCardEntry>();
    var deck = new System.Collections.Generic.List<ActionCardEntry>();

    int[] cardReq = { TypeId<ActionCard>.Value };
    foreach (var arch in world.GetMatchingArchetypes(cardReq, null)) {
        ActionCard[] cards = arch.GetColumn<ActionCard>();
        int count = arch.Count;
        for (int i = 0; i < count; i++) {
            if (cards[i].OwnerId != orgId) { continue; }
            // check InHand
            bool inHand = false;
            int slotIndex = -1;
            // InHand is in separate archetype — need to check via entity
            // Since ECS archetypes are keyed by component signature, cards in hand
            // have a different archetype. Re-query with InHand included.
            deck.Add(new ActionCardEntry(cards[i].ActionId, -1, false));
        }
    }

    // Redo: separate queries for hand vs deck
    hand.Clear(); deck.Clear();
    int[] handReq = { TypeId<ActionCard>.Value, TypeId<InHand>.Value };
    foreach (var arch in world.GetMatchingArchetypes(handReq, null)) {
        ActionCard[] cards = arch.GetColumn<ActionCard>();
        InHand[] hands = arch.GetColumn<InHand>();
        for (int i = 0; i < arch.Count; i++) {
            if (cards[i].OwnerId != orgId) { continue; }
            hand.Add(new ActionCardEntry(cards[i].ActionId, hands[i].SlotIndex, true));
        }
    }
    int[] deckReq2 = { TypeId<ActionCard>.Value };
    int[] excludeInHand = { TypeId<InHand>.Value };
    foreach (var arch in world.GetMatchingArchetypes(deckReq2, excludeInHand)) {
        ActionCard[] cards = arch.GetColumn<ActionCard>();
        for (int i = 0; i < arch.Count; i++) {
            if (cards[i].OwnerId != orgId) { continue; }
            deck.Add(new ActionCardEntry(cards[i].ActionId, -1, false));
        }
    }
    hand.Sort((a, b) => a.SlotIndex.CompareTo(b.SlotIndex));
    _state.PlayerOrgActions.Set(hand, deck, handSize);
}
```

**17c — Call both in `Update()`:**
```csharp
UpdateDiscoveredCountries(world);
UpdateOrgActions(world);
```

---

### Step 18 — `src/Game.Main/GameLogicContext.cs`: wire `MapGeometry` through `GameLifetimeScope`

`GameLifetimeScope` will supply a `MapGeometryConfig` wrapper. Since `Core.Map.GeoJsonParser` already parses the raw bytes in `MapLoader`, create a Unity-side adapter (Step 27) that implements `IConfigSource<List<MapFeature>>` by reading the GeoJSON text asset and calling the parser. Pass it into `GameLogicContext` as the new `mapGeometry` parameter.

---

### Step 19 — Map rendering: hide undiscovered countries

**19a — `src/Game.Main/VisualState.cs`:** Already added `DiscoveredCountries` in Step 16.

**19b — `Assets/Scripts/Unity/Map/MapLensApplier.cs`:** Subscribe to `_state.DiscoveredCountries.PropertyChanged` in `OnEnable`/`OnDisable`. In `ApplyLens`, after computing the color, set `mr.enabled = false` for countries not in the discovered set (when lens != Geographic). Add method:

```csharp
bool IsDiscovered(string mapFeatureId) {
    var country = _domainCountryConfig?.FindByFeatureId(mapFeatureId);
    string domainId = country != null ? country.CountryId : mapFeatureId;
    return _state?.DiscoveredCountries?.CountryIds?.Contains(domainId) ?? true;
}
```

In `ApplyLens` loop, add after the color calculation:
```csharp
bool discovered = IsDiscovered(go.name);
mr.enabled = discovered;
if (!discovered) { continue; }
```

The `DiscoveredCountries` state is only populated once the game initializes. When empty (0 IDs), treat all countries as visible (pre-init state). Adjust the check:

```csharp
bool IsDiscovered(string mapFeatureId) {
    var ids = _state?.DiscoveredCountries?.CountryIds;
    if (ids == null || ids.Count == 0) { return true; } // pre-init: show all
    var country = _domainCountryConfig?.FindByFeatureId(mapFeatureId);
    string domainId = country != null ? country.CountryId : mapFeatureId;
    return ids.Contains(domainId);
}
```

Subscribe to `_state.DiscoveredCountries.PropertyChanged` in `OnEnable`; unsubscribe in `OnDisable`. Handler calls `ApplyLens(_state.MapLens.Lens)`.

---

### Step 20 — `Assets/Configs/action_config.json` and `effect_config.json` (already written in Steps 3–4)

These JSON files are already specified. They go in `Assets/Configs/`.

---

### Step 21 — Create `Assets/Scripts/Unity/Common/MapGeometryConfig.cs`

Adapter implementing `IConfigSource<List<MapFeature>>` for use in `GameLifetimeScope`:

```csharp
using System.Collections.Generic;
using UnityEngine;
using GS.Configs;
using GS.Core.Map;

namespace GS.Unity.Common {
    public class MapGeometryConfig : IConfigSource<List<MapFeature>> {
        readonly TextAsset _geoJsonAsset;

        public MapGeometryConfig(TextAsset geoJsonAsset) {
            _geoJsonAsset = geoJsonAsset;
        }

        public List<MapFeature> Load() {
            if (_geoJsonAsset == null) { return new List<MapFeature>(); }
            return GeoJsonParser.Parse(_geoJsonAsset.text);
        }
    }
}
```

Check that `GeoJsonParser.Parse(string)` is the correct static method signature in `src/Core.Map/Map/GeoJsonParser.cs`. If the method name or signature differs, adjust accordingly.

---

### Step 22 — `Assets/Scripts/Unity/DI/GameLifetimeScope.cs`: wire new configs

**22a — Add fields:**
```csharp
[SerializeField] TextAsset _actionConfigAsset;
[SerializeField] TextAsset _effectConfigAsset;
```

**22b — In `Configure()`, pass new sources into `GameLogicContext`:**
```csharp
var ctx = new GameLogicContext(
    ...,  // existing params
    character: ...,
    action: _actionConfigAsset != null ? new TextAssetConfig<GS.Game.Configs.ActionConfig>(_actionConfigAsset) : null,
    effect: _effectConfigAsset != null ? new TextAssetConfig<GS.Game.Configs.EffectConfig>(_effectConfigAsset) : null,
    mapGeometry: new MapGeometryConfig(_geoJsonConfig)
);
```

**22c — Register `ActionConfig` for UI injection:**
```csharp
builder.Register(c => c.Resolve<GameLogic>().ActionConfig, Lifetime.Singleton);
```

---

### Step 23 — Create `Assets/Scripts/Unity/Common/ActionVisualConfig.cs`

ScriptableObject mapping actionId → card front/back sprites.

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace GS.Unity.Common {
    [System.Serializable]
    public class ActionVisualEntry {
        public string   actionId;
        public Sprite   frontImage;
        public Sprite   backImage;
    }

    [CreateAssetMenu(fileName = "ActionVisualConfig", menuName = "GS/ActionVisualConfig")]
    public class ActionVisualConfig : ScriptableObject {
        public Sprite              defaultBackImage;
        public List<ActionVisualEntry> entries = new();

        public Sprite FindFront(string actionId) {
            foreach (var e in entries) {
                if (e.actionId == actionId) return e.frontImage;
            }
            return null;
        }

        public Sprite FindBack(string actionId) {
            foreach (var e in entries) {
                if (e.actionId == actionId && e.backImage != null) return e.backImage;
            }
            return defaultBackImage;
        }
    }
}
```

File path: `Assets/Scripts/Unity/Common/ActionVisualConfig.cs`.

---

### Step 24 — Update `Assets/UI/Overlay/OrgInfo/OrgInfo.uxml`: add "Actions" button

In the `org-bar` element, after the `chars-toggle-btn`, add:

```xml
<ui:Button name="actions-toggle-btn" class="actions-toggle-btn gs-btn gs-btn--small" text="Actions" />
```

---

### Step 25 — Create `Assets/UI/Overlay/OrgInfo/OrgActions.uxml`

Standalone template file that will be included in `OrgInfo.uxml` via `<ui:Template>` + `<ui:Instance>`. Its USS (`OrgActions.uss`) provides all card-related styles and is in scope because the template is used as an instance *inside* `OrgInfo.uxml` — which is the document that owns the container element (matching the USS scope rule from `.claude/rules/unity/uitoolkit.md`).

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" editor-extension-mode="False">
    <ui:Style src="project://database/Assets/UI/Shared/SharedStyles.uss"/>
    <ui:Style src="project://database/Assets/UI/Overlay/OrgInfo/OrgActions.uss"/>
    <ui:VisualElement name="org-actions-root" class="org-actions-root">
        <ui:VisualElement name="hand-section" class="hand-section">
            <ui:Label name="hand-label" class="gs-label" text="Hand" />
            <ui:VisualElement name="hand-container" class="hand-container" />
        </ui:VisualElement>
        <ui:VisualElement name="deck-section" class="deck-section">
            <ui:Label name="deck-label" class="gs-label" text="Deck" />
            <ui:VisualElement name="deck-container" class="deck-container" />
        </ui:VisualElement>
    </ui:VisualElement>
</ui:UXML>
```

---

### Step 26 — Create `Assets/UI/Overlay/OrgInfo/OrgActions.uss`

```css
.org-actions-root {
    flex-direction: row;
    padding: 8px;
    min-width: 360px;
}

.hand-section {
    flex: 1;
    flex-direction: column;
    align-items: flex-start;
    margin-right: 8px;
}

.deck-section {
    flex-direction: column;
    align-items: center;
    min-width: 80px;
}

.hand-container {
    flex-direction: row;
    flex-wrap: wrap;
    justify-content: flex-start;
}

.deck-container {
    flex-direction: column;
    align-items: center;
}

.action-card {
    width: 120px;
    min-height: 160px;
    margin: 4px;
    flex-direction: column;
    align-items: center;
    border-width: 2px;
    border-radius: 6px;
    padding: 6px;
}

.action-card--available {
    border-color: rgb(180, 160, 80);
    background-color: rgba(40, 30, 15, 0.9);
    transition-property: translate;
    transition-duration: 0.15s;
}

.action-card--available:hover {
    translate: 0 -8px;
}

.action-card--unavailable {
    border-color: rgb(100, 80, 60);
    background-color: rgba(30, 25, 15, 0.7);
    opacity: 0.7;
}

.action-card-name {
    font-size: 14px;
    color: rgb(220, 190, 120);
    -unity-font-style: bold;
    -unity-text-align: middle-center;
    margin-bottom: 4px;
}

.action-card-image {
    width: 80px;
    height: 80px;
    margin-bottom: 4px;
}

.action-card-price {
    font-size: 13px;
    -unity-text-align: middle-center;
}

.action-card-price--affordable {
    color: rgb(100, 200, 80);
}

.action-card-price--unaffordable {
    color: rgb(200, 80, 80);
}

.deck-pile {
    width: 80px;
    height: 110px;
    border-width: 2px;
    border-radius: 4px;
    border-color: rgb(140, 100, 60);
    background-color: rgba(30, 20, 10, 0.8);
    margin-top: 4px;
}

.deck-count-label {
    font-size: 16px;
    color: rgb(200, 170, 100);
    -unity-text-align: middle-center;
    margin-top: 4px;
}
```

---

### Step 27 — Create `Assets/Scripts/Unity/UI/OrgActionsView.cs`

Plain view class. Renders hand cards and deck pile. Exposes `OnCardClicked` callback.

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Main;
using GS.Game.Configs;
using GS.Unity.Common;

namespace GS.Unity.UI {
    class OrgActionsView {
        readonly VisualElement _handContainer;
        readonly VisualElement _deckContainer;
        readonly ILocalization _loc;
        readonly ActionConfig  _actionConfig;
        readonly ActionVisualConfig _visualConfig;
        readonly ResourceConfig _resourceConfig;
        readonly TooltipSystem _tooltip;

        public Action<string>? OnCardClicked;

        public OrgActionsView(
            VisualElement handContainer,
            VisualElement deckContainer,
            ILocalization loc,
            ActionConfig actionConfig,
            ActionVisualConfig visualConfig,
            ResourceConfig resourceConfig,
            TooltipSystem tooltip) {
            _handContainer = handContainer;
            _deckContainer = deckContainer;
            _loc = loc;
            _actionConfig = actionConfig;
            _visualConfig = visualConfig;
            _resourceConfig = resourceConfig;
            _tooltip = tooltip;
        }

        public void Refresh(OrgActionsState state, CountryResourcesState resources) {
            _handContainer.Clear();
            _deckContainer.Clear();

            foreach (var card in state.Hand) {
                var actionDef = _actionConfig.Find(card.ActionId);
                bool canAfford = actionDef != null && CanAffordAll(actionDef, resources);
                _handContainer.Add(BuildHandCard(card, actionDef, canAfford));
            }

            BuildDeckPile(state.Deck.Count);
        }

        VisualElement BuildHandCard(ActionCardEntry card, ActionDefinition? def, bool canAfford) {
            var cardEl = new VisualElement();
            cardEl.AddToClassList("action-card");
            cardEl.AddToClassList(canAfford ? "action-card--available" : "action-card--unavailable");

            string name = def != null ? _loc.Get(def.NameKey) : card.ActionId;
            var nameLabel = new Label(name);
            nameLabel.AddToClassList("action-card-name");
            cardEl.Add(nameLabel);

            var img = new VisualElement();
            img.AddToClassList("action-card-image");
            var sprite = _visualConfig?.FindFront(card.ActionId);
            if (sprite != null) {
                img.style.backgroundImage = new StyleBackground(sprite);
            }
            cardEl.Add(img);

            if (def != null && def.Prices.Count > 0) {
                // Show each price component (one line per resource)
                foreach (var price in def.Prices) {
                    var resConfig = _resourceConfig?.FindById(price.ResourceId);
                    string resName = resConfig != null ? _loc.Get(resConfig.NameKey) : price.ResourceId;
                    var priceLabel = new Label($"{price.Amount:F0} {resName}");
                    priceLabel.AddToClassList("action-card-price");
                    priceLabel.AddToClassList(canAfford ? "action-card-price--affordable" : "action-card-price--unaffordable");
                    cardEl.Add(priceLabel);
                }
            }

            if (canAfford) {
                string capturedActionId = card.ActionId;
                cardEl.RegisterCallback<ClickEvent>(_ => OnCardClicked?.Invoke(capturedActionId));
                RegisterTooltip(cardEl, def, true, card.ActionId);
            } else {
                RegisterTooltip(cardEl, def, false, card.ActionId);
            }

            return cardEl;
        }

        void BuildDeckPile(int deckCount) {
            var pile = new VisualElement();
            pile.AddToClassList("deck-pile");
            var sprite = _visualConfig?.defaultBackImage;
            if (sprite != null) {
                pile.style.backgroundImage = new StyleBackground(sprite);
            }
            _deckContainer.Add(pile);

            var countLabel = new Label($"×{deckCount}");
            countLabel.AddToClassList("deck-count-label");
            _deckContainer.Add(countLabel);
        }

        void RegisterTooltip(VisualElement trigger, ActionDefinition? def, bool available, string actionId) {
            if (def == null) { return; }
            string captured = actionId;
            bool cap = available;
            ActionDefinition capDef = def;
            _tooltip.RegisterTrigger(trigger, $"action-{actionId}", _ => BuildCardTooltip(capDef, cap), new HashSet<string>());
        }

        VisualElement BuildCardTooltip(ActionDefinition def, bool available) {
            var root = new VisualElement();
            string desc = _loc.Get(def.DescKey);
            int pct = (int)(def.SuccessRate * 100f);
            var descLabel = new Label($"{desc}\n{pct}% success");
            descLabel.AddToClassList("gs-content");
            root.Add(descLabel);
            var hint = new Label(available ? _loc.Get("action.tooltip.play_hint") : _loc.Get("action.tooltip.unaffordable_hint"));
            hint.AddToClassList(available ? "gs-color-positive" : "gs-color-negative");
            root.Add(hint);
            return root;
        }

        static bool CanAffordAll(ActionDefinition def, CountryResourcesState resources) {
            foreach (var price in def.Prices) {
                if (GetResourceValue(resources, price.ResourceId) < price.Amount) { return false; }
            }
            return true;
        }

        static double GetResourceValue(CountryResourcesState? resources, string resourceId) {
            if (resources == null) { return 0; }
            foreach (var r in resources.Resources) {
                if (r.ResourceId == resourceId) { return r.Value; }
            }
            return 0;
        }
    }
}
```

Note: The `Refresh` method receives both `OrgActionsState` and `CountryResourcesState` so it can colour the price correctly. Update the signature to remove the unused gold lookup and use the passed resources parameter.

---

### Step 28 — `Assets/Scripts/Unity/UI/OrgInfoDocument.cs`: add Actions button and view

**28a — Add fields:**
```csharp
Button _actionsToggleBtn;
VisualElement _actionsSlide;
OrgActionsView _actionsView;
bool _actionsOpen;
IWriteOnlyCommandAccessor _commands;
ActionConfig _actionConfig;
ActionVisualConfig _actionVisualConfig;
```

**28b — Extend `[Inject]`:**
```csharp
[Inject]
void Construct(VisualState state, ILocalization loc, ResourceConfig resourceConfig,
               CharacterConfig characterConfig, CharacterVisualConfig characterVisualConfig,
               IWriteOnlyCommandAccessor commands, ActionConfig actionConfig,
               ActionVisualConfig actionVisualConfig) {
    // existing fields...
    _commands = commands;
    _actionConfig = actionConfig;
    _actionVisualConfig = actionVisualConfig;
}
```

**28c — In `Awake()`**, after existing queries add:
```csharp
_actionsToggleBtn = docRoot.Q<Button>("actions-toggle-btn");
_actionsSlide = docRoot.Q("actions-slide");

if (_actionsToggleBtn != null) {
    _actionsToggleBtn.clicked += ToggleActions;
}
```

The `OrgInfo.uxml` needs an `actions-slide` element that wraps the actions view. Add it to the UXML in Step 24 update (see below).

**28d — In `InitViews()`**, add:
```csharp
_actionsView = new OrgActionsView(
    docRoot.Q("actions-hand-container"),
    docRoot.Q("actions-deck-container"),
    _loc, _actionConfig, _actionVisualConfig, _resourceConfig, _tooltip);
_actionsView.OnCardClicked = OnActionCardClicked;
```

**28e — Subscribe/unsubscribe `PlayerOrgActions` in `OnEnable`/`OnDisable`:**
```csharp
_state.PlayerOrgActions.PropertyChanged += HandleActionsChanged;
// OnDisable:
_state.PlayerOrgActions.PropertyChanged -= HandleActionsChanged;
```

**28f — In `Refresh()`:**
```csharp
_actionsView?.Refresh(_state.PlayerOrgActions, _state.PlayerResources);
bool hasActions = _state.PlayerOrgActions.Hand.Count > 0 || _state.PlayerOrgActions.Deck.Count > 0;
if (_actionsToggleBtn != null) {
    _actionsToggleBtn.style.display = hasActions ? DisplayStyle.Flex : DisplayStyle.None;
}
```

**28g — Add `ToggleActions()` and `SetActionsOpen(bool)` mirroring the chars toggle pattern.**

**28h — Add `OnActionCardClicked(string actionId)`:**
```csharp
void OnActionCardClicked(string actionId) {
    if (_commands == null || _state == null || !_state.PlayerOrganization.IsValid) { return; }
    _commands.Push(new PlayActionCommand {
        OwnerId  = _state.PlayerOrganization.OrgId,
        ActionId = actionId
    });
    // The animation is driven by LastActionResult state change in CardPlayAnimator
}
```

**28i — `HandleActionsChanged`:**
```csharp
void HandleActionsChanged(object sender, PropertyChangedEventArgs e) => Refresh();
```

---

### Step 29 — Update `Assets/UI/Overlay/OrgInfo/OrgInfo.uxml`: add actions slide via template include

Include `OrgActions.uxml` as a template instance inside the actions slide. This keeps card-related styles in `OrgActions.uss` and in scope, matching the characters panel pattern.

At the top of the file (after existing `<ui:Template>` declarations if any), add:
```xml
<ui:Template src="project://database/Assets/UI/Overlay/OrgInfo/OrgActions.uxml" name="OrgActions" />
```

After the `characters-slide` block, before `org-bar`, add the slide wrapper with a template instance:

```xml
<ui:VisualElement name="actions-slide" class="org-actions-slide">
    <ui:Instance template="OrgActions" name="org-actions-instance" class="org-actions-template" />
</ui:VisualElement>
```

In `OrgInfoDocument.Awake()`, resolve the hand/deck containers through the instance:
```csharp
var actionsInstance = docRoot.Q("org-actions-instance");
// OrgActionsView queries hand-container / deck-container relative to the instance root
_actionsView = new OrgActionsView(
    actionsInstance.Q("hand-container"),
    actionsInstance.Q("deck-container"),
    _loc, _actionConfig, _actionVisualConfig, _resourceConfig, _tooltip);
```

Also ensure the actions toggle button is in `org-bar`:
```xml
<ui:Button name="actions-toggle-btn" class="gs-btn gs-btn--small" text="Actions" />
```

Add to `OrgInfo.uss` the slide-open class mirroring `org-characters-slide--open`:
```css
.org-actions-slide {
    max-height: 0;
    overflow: hidden;
    transition-property: max-height;
    transition-duration: 0.3s;
    transition-timing-function: ease-in-out;
}
.org-actions-slide--open {
    max-height: 400px;
}
```

---

### Step 30 — Create `Assets/Scripts/Unity/UI/CardPlayAnimator.cs`

MonoBehaviour handling the full animated play sequence. Placed on the scene root `GameLifetimeScope` GameObject or a dedicated `Animators` GameObject.

**Design:**

- `PlaySequence(string actionId, bool success, string discoveredCountryId)` is the public entry point; called by `HUDDocument` when `VisualState.LastAction.HasResult == true`.
- Internally uses `IVisualElementScheduler` on the HUD root element for time-sequenced steps.
- A `bool _isPlaying` flag gates re-entry.
- Subscribes to the HUD root's `PointerDownEvent` during animation for skip.
- On skip or completion: calls `_state.LastAction.Clear()`, clears `_isPlaying`, re-enables input.

**Sequence steps (with timings):**

| # | Action | Duration |
|---|--------|----------|
| 1 | Pause game (`PauseCommand`), lock input, hide org panel | immediate |
| 2 | Card test view appears at screen center (card at ×1.5 scale, move from hand slot) | 0.4 s |
| 3 | Roll block appears (scale+alpha in) | 0.3 s |
| 4 | Roll block animates random numbers 6× over 2 s | 2.0 s |
| 5 | Roll block stops on real numbers (chance ≥/</= required %) | 0.1 s |
| 6 | Card highlights red or green (±10% rubber scale) | 0.5 s |
| 7 | Roll block hides (alpha out) | 0.3 s |
| 8 | Card moves to deck, flips to back | 0.5 s |
| 9 (success) | Camera pans to discovered country | 1.0 s |
| 10 (success) | Fly text appears | 1.5 s |
| 11 | Input unlocked, game unpaused | immediate |

**Card test view UXML element** (`card-test-overlay`) lives in `HUD.uxml` as an absolute-positioned full-screen element, hidden by default. It contains:
- `card-test-card` — the card visual
- `roll-block` — shows current chance % vs required %

**CardPlayAnimator fields:**
```csharp
VisualState _state;
IWriteOnlyCommandAccessor _commands;
MapCameraController _cameraController;
CountryConfig _domainConfig;
```

Full skeleton:

```csharp
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;
using GS.Main;
using GS.Game.Commands;
using GS.Game.Configs;
using GS.Unity.Map;
using System.ComponentModel;

namespace GS.Unity.UI {
    public class CardPlayAnimator : MonoBehaviour {
        UIDocument _hudDocument;
        VisualState _state;
        IWriteOnlyCommandAccessor _commands;
        MapCameraController _cameraController;
        CountryConfig _domainConfig;
        bool _isPlaying;
        VisualElement _testOverlay;
        VisualElement _cardEl;
        VisualElement _rollBlock;
        Label _rollResultLabel;
        Label _flyText;
        // Cached skip handler so UnregisterCallback can remove the exact same delegate instance.
        EventCallback<PointerDownEvent> _skipHandler;

        [Inject]
        void Construct(VisualState state, IWriteOnlyCommandAccessor commands,
                       MapCameraController cameraController, CountryConfig domainConfig) {
            _state = state;
            _commands = commands;
            _cameraController = cameraController;
            _domainConfig = domainConfig;
        }

        void Awake() {
            _hudDocument = GetComponent<UIDocument>();
        }

        void OnEnable() {
            if (_state != null) {
                _state.LastAction.PropertyChanged += HandleLastActionChanged;
            }
        }

        void OnDisable() {
            if (_state != null) {
                _state.LastAction.PropertyChanged -= HandleLastActionChanged;
            }
        }

        void HandleLastActionChanged(object sender, PropertyChangedEventArgs e) {
            if (_state.LastAction.HasResult && !_isPlaying) {
                // Discovered country (if any) is tracked via DiscoveredCountries.RecentlyDiscovered,
                // not in LastAction — keeps LastAction generic across all action types.
                string discoveredCountryId = _state.DiscoveredCountries.RecentlyDiscovered;
                StartCoroutine(PlaySequence(_state.LastAction.Success, discoveredCountryId));
            }
        }

        IEnumerator PlaySequence(bool success, string discoveredCountryId) {
            _isPlaying = true;
            ModalState.IsModalOpen = true;
            _commands.Push(new PauseCommand());

            var root = _hudDocument.rootVisualElement;
            _testOverlay = root.Q("card-test-overlay");
            _cardEl = root.Q("card-test-card");
            _rollBlock = root.Q("roll-block");
            _rollResultLabel = root.Q<Label>("roll-result-label");
            _flyText = root.Q<Label>("fly-text");

            if (_testOverlay == null) { yield return FinishSequence(success, discoveredCountryId); yield break; }

            bool skipped = false;
            _skipHandler = _ => skipped = true;
            root.RegisterCallback<PointerDownEvent>(_skipHandler, TrickleDown.TrickleDown);

            // Show test overlay
            _testOverlay.style.display = DisplayStyle.Flex;
            _testOverlay.style.opacity = 0f;
            _testOverlay.style.scale = new StyleScale(new Scale(new Vector2(0.8f, 0.8f)));
            // Animate in — use scheduler
            yield return AnimateTo(_testOverlay, 1f, Vector2.one, 0.4f, () => skipped);

            if (!skipped) {
                // Show roll block
                _rollBlock.style.display = DisplayStyle.Flex;
                _rollBlock.style.opacity = 0f;
                yield return AnimateTo(_rollBlock, 1f, Vector2.one, 0.3f, () => skipped);
            }

            if (!skipped) {
                // Animate random numbers
                float elapsed = 0f;
                int rollDuration = 2;
                while (elapsed < rollDuration && !skipped) {
                    int fakeVal = UnityEngine.Random.Range(1, 101);
                    if (_rollResultLabel != null) { _rollResultLabel.text = $"{fakeVal}%"; }
                    yield return new WaitForSeconds(0.33f);
                    elapsed += 0.33f;
                }
            }

            // Stop on real values
            // (actual roll result already in _state.LastAction.Success)
            // Display final chance (for now: show required success rate from action def)
            // The actual roll number is not exposed in VisualState — show success/fail colour
            if (_rollResultLabel != null) {
                _rollResultLabel.text = success ? "Success!" : "Fail!";
                _rollResultLabel.style.color = success
                    ? new StyleColor(new Color(0.4f, 0.9f, 0.4f))
                    : new StyleColor(new Color(0.9f, 0.3f, 0.3f));
            }
            yield return new WaitForSeconds(0.5f);

            // Hide roll block, highlight card
            yield return AnimateTo(_rollBlock, 0f, Vector2.one * 0.8f, 0.3f, () => skipped);
            _rollBlock.style.display = DisplayStyle.None;

            // Card rubber effect
            Color highlightColor = success ? new Color(0.4f, 1f, 0.4f, 0.8f) : new Color(1f, 0.3f, 0.3f, 0.8f);
            if (_cardEl != null) {
                _cardEl.style.borderTopColor   = highlightColor;
                _cardEl.style.borderBottomColor = highlightColor;
                _cardEl.style.borderLeftColor  = highlightColor;
                _cardEl.style.borderRightColor = highlightColor;
                _cardEl.style.borderTopWidth   = 3f;
                _cardEl.style.borderBottomWidth = 3f;
                _cardEl.style.borderLeftWidth  = 3f;
                _cardEl.style.borderRightWidth = 3f;
            }
            yield return new WaitForSeconds(0.5f);

            // Hide test overlay
            yield return AnimateTo(_testOverlay, 0f, Vector2.one * 0.8f, 0.4f, () => skipped);
            _testOverlay.style.display = DisplayStyle.None;

            yield return FinishSequence(success, discoveredCountryId);

            if (_skipHandler != null) {
                root.UnregisterCallback<PointerDownEvent>(_skipHandler, TrickleDown.TrickleDown);
                _skipHandler = null;
            }
        }

        IEnumerator FinishSequence(bool success, string discoveredCountryId) {
            if (success && !string.IsNullOrEmpty(discoveredCountryId)) {
                // Camera pan
                PanCameraToCountry(discoveredCountryId);
                yield return new WaitForSeconds(1.0f);
                // Fly text
                ShowFlyText(discoveredCountryId);
                yield return new WaitForSeconds(1.5f);
                HideFlyText();
            }

            _state.DiscoveredCountries.ClearRecentlyDiscovered();
            _state.LastAction.Clear();
            ModalState.IsModalOpen = false;
            _commands.Push(new UnpauseCommand());
            _isPlaying = false;
        }

        IEnumerator AnimateTo(VisualElement el, float targetAlpha, Vector2 targetScale, float duration, Func<bool> isSkipped) {
            float elapsed = 0f;
            float startAlpha = el.style.opacity.value;
            Vector2 startScale = el.style.scale.value.value;
            while (elapsed < duration && !isSkipped()) {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                el.style.opacity = Mathf.Lerp(startAlpha, targetAlpha, t);
                el.style.scale = new StyleScale(new Scale(Vector2.Lerp(startScale, targetScale, t)));
                yield return null;
            }
            el.style.opacity = targetAlpha;
            el.style.scale = new StyleScale(new Scale(targetScale));
        }

        void PanCameraToCountry(string countryId) {
            if (_cameraController == null) { return; }
            var entry = _domainConfig?.FindByCountryId(countryId);
            if (entry == null || entry.MainMapFeatureIds.Count == 0) { return; }
            // Use the MapCameraController to pan to the country's approximate world position.
            // The centroid will be computed from the feature's mesh bounds in a future step.
            // For now: call a new method MapCameraController.PanToCountry(countryId).
            _cameraController.PanToCountry(countryId);
        }

        void ShowFlyText(string countryId) {
            if (_flyText == null) { return; }
            string name = countryId.Replace("_", " ").ToUpperInvariant();
            _flyText.text = $"You discovered {name}!";
            _flyText.style.display = DisplayStyle.Flex;
            _flyText.style.opacity = 0f;
            _flyText.style.translate = new StyleTranslate(new Translate(0, 40, 0));
            // Animate in via scheduler
            _flyText.schedule.Execute(() => {
                _flyText.style.opacity = 1f;
                _flyText.style.translate = new StyleTranslate(new Translate(0, 0, 0));
            }).ExecuteLater(50);
        }

        void HideFlyText() {
            if (_flyText == null) { return; }
            _flyText.style.display = DisplayStyle.None;
        }
    }
}
```

---

### Step 31 — `Assets/Scripts/Unity/Map/MapCameraController.cs`: add `PanToCountry()`

`MapCameraController` currently handles scroll. Add new fields and extend the `[Inject]` method to accept `MapController` and `CountryConfig`, then add the smooth pan method.

**Add fields:**
```csharp
MapController _mapController;
GS.Game.Configs.CountryConfig _domainConfig;
Vector3? _panTarget;
float _panSpeed = 5f;
```

**Update `[Inject]` method** (existing one already injects a camera config — add the two new params):
```csharp
[Inject]
void Construct(MapCameraConfig config, MapController mapController, GS.Game.Configs.CountryConfig domainConfig) {
    _config = config;
    _mapController = mapController;
    _domainConfig = domainConfig;
}
```

**Add `PanToCountry` method:**
```csharp
public void PanToCountry(string countryId) {
    var renderer = _mapController?.ActiveRenderer;
    if (renderer == null) { return; }
    foreach (var go in renderer.FeatureObjects) {
        if (go == null) { continue; }
        var entry = _domainConfig?.FindByFeatureId(go.name);
        if (entry == null || entry.CountryId != countryId) { continue; }
        var mf = go.GetComponent<MeshFilter>();
        if (mf == null) { continue; }
        var center = go.transform.TransformPoint(mf.mesh.bounds.center);
        _panTarget = new Vector3(center.x, center.y, _camera.transform.position.z);
        return;
    }
}
```

**In `Update()`**, after existing scroll handling, add:
```csharp
if (_panTarget.HasValue) {
    _camera.transform.position = Vector3.Lerp(
        _camera.transform.position, _panTarget.Value, _panSpeed * Time.deltaTime);
    if (Vector3.Distance(_camera.transform.position, _panTarget.Value) < 0.05f) {
        _camera.transform.position = _panTarget.Value;
        _panTarget = null;
    }
}
```

---

### Step 32 — HUD UXML: add card test overlay and fly text

`Assets/UI/HUD/HUD.uxml` — add inside `hud-root`, after existing panels:

```xml
<!-- Card test overlay -->
<ui:VisualElement name="card-test-overlay" class="gs-modal-root" style="display: none;">
    <ui:VisualElement name="card-test-inner" style="flex-direction: row; align-items: center; justify-content: center; gap: 32px;">
        <ui:VisualElement name="card-test-card" class="action-card" style="width: 180px; min-height: 240px;" />
        <ui:VisualElement name="roll-block" style="display: none; flex-direction: column; align-items: center; padding: 16px; background-color: rgba(20,15,8,0.92); border-radius: 8px; min-width: 140px;">
            <ui:Label name="roll-result-label" class="gs-header" text="??" />
            <ui:Label name="roll-vs-label" class="gs-content" text="vs 75% required" />
        </ui:VisualElement>
    </ui:VisualElement>
</ui:VisualElement>

<!-- Fly text -->
<ui:Label name="fly-text" class="gs-title" style="display: none; position: absolute; left: 50%; top: 40%; translate: -50% -50%; -unity-text-align: middle-center; transition-property: opacity, translate; transition-duration: 0.4s;" text="" />
```

Add to `HUD.uss`:
```css
.card-test-overlay {
    transition-property: opacity, scale;
    transition-duration: 0.4s;
}
```

---

### Step 33 — `Assets/Scripts/Unity/DI/GameLifetimeScope.cs`: register new types

```csharp
[SerializeField] ActionVisualConfig _actionVisualConfig;
// in Configure():
builder.RegisterInstance(_actionVisualConfig);
builder.Register(c => c.Resolve<GameLogic>().ActionConfig, Lifetime.Singleton);
builder.RegisterComponentInHierarchy<CardPlayAnimator>();
```

Also pass `mapGeometry` to `GameLogicContext` (Step 22).

Inject `ActionConfig` and `ActionVisualConfig` into `OrgInfoDocument` via `GameLifetimeScope` — they are already registered above so VContainer can resolve them.

---

### Step 34 — Localization: add new keys

Add to `Assets/Localization/en.asset` and `ru.asset`:

| Key | English | Russian |
|-----|---------|---------|
| `action.discover_country.name` | Discover Country | Открыть страну |
| `action.discover_country.desc` | Send agents to uncover a nearby unknown nation. | Отправить агентов для разведки неизвестной страны. |
| `action.tooltip.play_hint` | Click to play | Нажмите, чтобы сыграть |
| `action.tooltip.unaffordable_hint` | Not enough gold | Недостаточно золота |
| `effect.discover_country.name` | Discover Country | Открыть страну |
| `effect.discover_country.desc` | Reveals a previously unknown country on the map. | Открывает неизвестную страну на карте. |
| `hud.actions` | Actions | Действия |

---

### Step 35 — Verify asmdef references

All `src/` assemblies compile to DLLs in `Assets/Plugins/Core/` with `autoReferenced: true`, so they are available to all Unity assemblies without explicit GUID references. However, confirm the following before building:

- `Assets/Scripts/Unity/UI/GS.Unity.UI.asmdef` — `OrgActionsView` and `CardPlayAnimator` reference `GS.Main` (ActionCardEntry, OrgActionsState, LastActionResultState), `GS.Game.Configs` (ActionConfig, ActionDefinition), and `GS.Game.Commands` (PlayActionCommand, PauseCommand, UnpauseCommand). All these DLLs are in `Plugins/Core/` with autoReferenced — no manual GUID entry needed.
- `Assets/Scripts/Unity/Common/GS.Unity.Common.asmdef` — `MapGeometryConfig` references `GS.Core.Map` (MapFeature, GeoJsonParser). Same — autoReferenced DLL.
- `Assets/Scripts/Unity/Map/GS.Unity.Map.asmdef` — `MapCameraController` now references `GS.Game.Configs.CountryConfig` (autoReferenced DLL) and `MapController` (same assembly — no change needed).

If Unity reports missing type errors after `refresh_unity`, add the specific DLL GUID from `Assets/Plugins/Core/<name>.dll.meta` to the relevant `.asmdef` references array.

---

### Step 36 — Image generation: card back and Discover card front

Create `.tmp/images.json`:

```json
[
  {
    "outputPath": "Assets/Textures/Actions/card_back_standard.png",
    "size": "256x384",
    "prompt": "ornate card back design, occult illuminati secret society symbol, dark red and gold geometric pattern, all-seeing eye, 19th century secret order aesthetic, detailed border ornament, flat card art, symmetrical design"
  },
  {
    "outputPath": "Assets/Textures/Actions/discover_country.png",
    "size": "256x384",
    "prompt": "historical map explorer card, 19th century world map unfurling, compass rose, ships on ocean, discovering new lands, spy telescope, gold and dark green color palette, card game illustration style, detailed decorative border"
  }
]
```

Run:
```powershell
$env:PYTHONUTF8 = '1'; & ".venv\Scripts\python.exe" ".claude\generate_images_batch.py" ".tmp\images.json"
```

Then:
- Import both PNGs into Unity (`Assets/Textures/Actions/`), set Texture Type = Sprite (2D and UI).
- Create `ActionVisualConfig` ScriptableObject at `Assets/Configs/ActionVisualConfig.asset`.
- Set `defaultBackImage` = `card_back_standard`.
- Add one entry: `actionId = "discover_country"`, `frontImage = discover_country`, `backImage = card_back_standard`.
- Wire `_actionVisualConfig` field on `GameLifetimeScope` in the scene inspector.

---

### Step 37 — Scene wiring: add `CardPlayAnimator` to scene

The `CardPlayAnimator` MonoBehaviour needs a `UIDocument` component (to access the HUD root visual element). It should be placed on the existing `HUDUI` GameObject (which already has a `UIDocument`). Alternatively, add a new `Animators` GameObject with its own `UIDocument` reference set to the HUD document asset — but it is simpler to add `CardPlayAnimator` directly to `HUDUI` since it already has the `UIDocument`. Update `Assets/Scenes/Map.unity` scene YAML to add the `CardPlayAnimator` component to the `HUDUI` GameObject block.

Register in `GameLifetimeScope.Configure()`:
```csharp
builder.RegisterComponentInHierarchy<CardPlayAnimator>();
```

---

---

## Files to Create / Modify

| Action | Path |
|--------|------|
| Create | `src/Game.Configs/EffectConfig.cs` |
| Create | `src/Game.Configs/ActionConfig.cs` |
| Modify | `src/Game.Configs/GameLogicContext.cs` — add `Action`, `Effect`, `MapGeometry` sources |
| Create | `src/Game.Components/ActionCard.cs` |
| Create | `src/Game.Components/InHand.cs` |
| Create | `src/Game.Components/ActionOwner.cs` |
| Create | `src/Game.Components/IsDiscovered.cs` |
| Create | `src/Game.Components/ProximityMapData.cs` |
| Create | `src/Game.Commands/PlayActionCommand.cs` |
| Modify | `src/Game.Main/InitSystem.cs` — add `BuildProximityMap`, `CreateActionEntities`, `DiscoverOwnCountry` |
| Modify | `src/Game.Main/GameLogic.cs` — add `_proximityEntity`, `ActionConfig`, `RebuildProximityMap()`, process `PlayActionCommand` |
| Create | `src/Game.Systems/ActionSystem.cs` |
| Modify | `src/Game.Main/VisualState.cs` — add `DiscoveredCountriesState`, `ActionCardEntry`, `OrgActionsState`, `LastActionResultState` and properties |
| Modify | `src/Game.Main/VisualStateConverter.cs` — add `UpdateDiscoveredCountries()`, `UpdateOrgActions()`, call both |
| Create | `Assets/Configs/effect_config.json` |
| Create | `Assets/Configs/action_config.json` |
| Create | `Assets/Scripts/Unity/Common/ActionVisualConfig.cs` |
| Create | `Assets/Scripts/Unity/Common/MapGeometryConfig.cs` |
| Modify | `Assets/Scripts/Unity/Map/MapLensApplier.cs` — subscribe to `DiscoveredCountries`, hide undiscovered |
| Modify | `Assets/Scripts/Unity/Map/MapCameraController.cs` — add `PanToCountry()` |
| Modify | `Assets/Scripts/Unity/UI/OrgInfoDocument.cs` — add Actions button, `OrgActionsView`, `PlayActionCommand` push |
| Create | `Assets/Scripts/Unity/UI/OrgActionsView.cs` |
| Create | `Assets/Scripts/Unity/UI/CardPlayAnimator.cs` |
| Modify | `Assets/Scripts/Unity/DI/GameLifetimeScope.cs` — add `_actionConfigAsset`, `_effectConfigAsset`, `_actionVisualConfig` fields; wire `MapGeometryConfig`; register new types |
| Modify | `Assets/UI/Overlay/OrgInfo/OrgInfo.uxml` — add `actions-slide` element and `actions-toggle-btn` |
| Create | `Assets/UI/Overlay/OrgInfo/OrgActions.uxml` |
| Create | `Assets/UI/Overlay/OrgInfo/OrgActions.uss` |
| Modify | `Assets/UI/HUD/HUD.uxml` — add `card-test-overlay` and `fly-text` elements |
| Modify | `Assets/UI/HUD/HUD.uss` — add card test transition styles |
| Modify | `Assets/Localization/en.asset` — add new action/effect locale keys |
| Modify | `Assets/Localization/ru.asset` — add Russian translations |
| Modify | `Assets/Scenes/Map.unity` — add `CardPlayAnimator` component to `HUDUI` |

---

## Tests

Add `src/Game.Tests/ActionSystemTests.cs`. Use the `BuildLogic`-style helper from existing test files (`ResourceSystemTests`, `SelectPlayerCountrySystemTests`).

### Helper: `BuildActionWorld()`

Creates a minimal `World` with:
- One `Country("Russian_Empire")` entity with `Player` tag
- One `Country("France")` entity (undiscovered, no `IsDiscovered`)
- One `Country("Ottoman_Empire")` entity (undiscovered)
- One `Organization { OrganizationId = "Illuminati" }` entity
- One `ResourceOwner("Illuminati")` + `Resource { ResourceId = "gold", Value = 500 }` entity
- One `ActionOwner { OwnerId = "Illuminati", OwnerType = "org", HandSize = 1 }`
- One `ActionCard { ActionId = "discover_country", OwnerId = "Illuminati", DeckIndex = 0 }` + `InHand { SlotIndex = 0 }`
- One `IsDiscovered` on Russian_Empire entity
- One `ProximityMapData { Distances = new() }` entity (empty = equal weights)

Returns `(World world, int goldEntity, int cardEntity)`.

Minimal `ActionConfig` with one `ActionDefinition`:
```
actionId = "discover_country", SuccessRate = 1.0f, Price = { resourceId = "gold", amount = 100 },
MinCountryChance = 0.01f
```

### Test cases

| Test | What | Assertion |
|------|------|-----------|
| `play_action_deducts_gold` | Build world (gold=500), call `ActionSystem.ProcessPlayAction` with success=guaranteed (rate=1.0, roll forced) | Gold entity value == 400 after call |
| `play_action_insufficient_gold_returns_not_executed` | Set gold = 50, call play | `result.Executed == false`; gold unchanged |
| `play_action_success_marks_country_discovered` | rate=1.0, call play | One of `France`/`Ottoman_Empire` has `IsDiscovered` component |
| `play_action_failure_does_not_discover` | rate=0.0, call play | Neither France nor Ottoman has `IsDiscovered` |
| `play_action_removes_card_from_hand` | After play | `ActionCard` entity no longer has `InHand` component |
| `play_action_draws_new_card_from_deck` | Add a second deck card (no InHand) before play | After play, exactly one `ActionCard` entity has `InHand` |
| `play_action_executed_sets_result` | rate=1.0 | `result.Executed == true`, `result.Success == true` |
| `proximity_map_built_with_all_available_countries` | Call `InitSystem.BuildProximityMap(world, ctx)` with 3 available countries | `ProximityMapData.Distances` count == 3 (pairs: A–B, A–C, B–C) |
| `discover_own_country_adds_is_discovered` | Call `CreateActionEntities` via full `InitSystem.Update` | Country entity for `initialPlayerCountryId` has `IsDiscovered` |
| `visual_state_discovered_countries_reflects_ecs` | After `InitSystem.Update` + `VisualStateConverter.Update` | `VisualState.DiscoveredCountries.CountryIds.Contains("Russian_Empire") == true` |
| `visual_state_org_actions_hand_count` | After `InitSystem.Update` + converter | `VisualState.PlayerOrgActions.Hand.Count == 1` |
| `visual_state_org_actions_deck_empty_when_only_card_in_hand` | single card in hand | `VisualState.PlayerOrgActions.Deck.Count == 0` |
| `last_action_result_cleared_after_set_then_clear` | `state.LastAction.Set(true)` then `Clear()` | `HasResult == false`, `Success == false` |

### Test infrastructure note

`ActionSystem.ProcessPlayAction` takes a `Random` parameter. Pass `new Random(42)` for deterministic tests. To force success/failure, pass `successRate = 1.0f` or `successRate = 0.0f` in the `ActionDefinition`.

---

Use /implement to start working on the plan or request changes.
