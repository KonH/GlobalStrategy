# Plan 25: Character Visual Config

## Goal

Display character portrait sprites in the characters panel. Portrait PNGs already exist at `Assets/Textures/Characters/{characterId}.png`. This plan introduces a `CharacterVisualConfig` ScriptableObject that maps `characterId → Sprite`, an Editor auto-populate tool, and wires the sprites into `CharactersView`.

## Approach

1. Add two new files in `Assets/Scripts/Unity/Common/` — `CharacterVisualEntry.cs` (a plain `[Serializable]` class) and `CharacterVisualConfig.cs` (the ScriptableObject) — one type per file as required.
2. Create the asset instance at `Assets/Configs/CharacterVisualConfig.asset`.
3. Create an Editor-only asmdef `GS.Editor.Characters` under `Assets/Scripts/Editor/Characters/` that references `GS.Unity.Common` (GUID `7e5a37e68b84aeb48bf5de2cbe39a94e`), and implement the auto-populate tool in the same folder.
4. Run the menu item to populate the config (manual step).
5. Thread `CharacterVisualConfig` through `CharactersView` → `CountryInfoView` → `HUDDocument` → `GameLifetimeScope`.
6. Remove the hard-coded grey `background-color` from `.character-portrait` in `CountryInfo.uss` so the sprite is the sole source of the portrait image (grey fallback preserved when no sprite is set, via `background-color` removal — grey colour is no longer needed since a missing portrait simply shows nothing).

---

## Steps

### 1. Create `CharacterVisualEntry.cs`

File: `Assets/Scripts/Unity/Common/CharacterVisualEntry.cs`

```csharp
using System;
using UnityEngine;

namespace GS.Unity.Common {
	[Serializable]
	public class CharacterVisualEntry {
		public string characterId;
		public Sprite portrait;
	}
}
```

### 2. Create `CharacterVisualConfig.cs`

File: `Assets/Scripts/Unity/Common/CharacterVisualConfig.cs`

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace GS.Unity.Common {
	[CreateAssetMenu(fileName = "CharacterVisualConfig", menuName = "GS/CharacterVisualConfig")]
	public class CharacterVisualConfig : ScriptableObject {
		public List<CharacterVisualEntry> entries = new();

		public Sprite FindPortrait(string characterId) {
			foreach (var e in entries) {
				if (e.characterId == characterId) {
					return e.portrait;
				}
			}
			return null;
		}
	}
}
```

### 3. Create the ScriptableObject asset

Create `Assets/Configs/CharacterVisualConfig.asset` via `Assets → Create → GS → CharacterVisualConfig` in the Unity Editor, or by writing the YAML manually.

### 4. Create Editor asmdef

File: `Assets/Scripts/Editor/Characters/GS.Editor.Characters.asmdef`

```json
{
    "name": "GS.Editor.Characters",
    "rootNamespace": "",
    "references": [
        "GUID:7e5a37e68b84aeb48bf5de2cbe39a94e"
    ],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

### 5. Create the auto-populate tool

File: `Assets/Scripts/Editor/Characters/CharacterVisualConfigPopulator.cs`

```csharp
using UnityEditor;
using UnityEngine;
using GS.Unity.Common;

namespace GS.Editor.Characters {
	public static class CharacterVisualConfigPopulator {
		const string ConfigPath = "Assets/Configs/CharacterVisualConfig.asset";
		const string TexturesFolder = "Assets/Textures/Characters";

		[MenuItem("GS/Tools/Populate Character Visual Config")]
		static void Populate() {
			var config = AssetDatabase.LoadAssetAtPath<CharacterVisualConfig>(ConfigPath);
			if (config == null) {
				Debug.LogError($"[CharacterVisualConfigPopulator] Asset not found at {ConfigPath}");
				return;
			}

			config.entries.Clear();
			var guids = AssetDatabase.FindAssets("t:Sprite", new[] { TexturesFolder });
			foreach (var guid in guids) {
				var path = AssetDatabase.GUIDToAssetPath(guid);
				var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
				if (sprite == null) {
					continue;
				}
				var characterId = System.IO.Path.GetFileNameWithoutExtension(path);
				config.entries.Add(new CharacterVisualEntry { characterId = characterId, portrait = sprite });
			}

			EditorUtility.SetDirty(config);
			AssetDatabase.SaveAssets();
			Debug.Log($"[CharacterVisualConfigPopulator] Populated {config.entries.Count} entries.");
		}
	}
}
```

### 6. Run the menu item (manual step)

In the Unity Editor, go to **GS → Tools → Populate Character Visual Config**. This scans `Assets/Textures/Characters/` and fills the asset. Verify the entry count in the Console matches the number of PNG files.

Note: PNG textures must be imported as `Sprite` (Texture Type = Sprite (2D and UI)) for `FindAssets("t:Sprite", ...)` to return them. If the textures are imported as `Default`, change the Texture Type to `Sprite` on each (or batch-change via a selection in the Project window → Inspector → Texture Type).

### 7. Update `CharactersView.cs`

- Add `CharacterVisualConfig _visualConfig` field.
- Add parameter to constructor.
- In `BuildCharacterCard`, after creating the `portrait` element:

```csharp
var sprite = _visualConfig?.FindPortrait(entry.CharacterId);
if (sprite != null) {
    portrait.style.backgroundImage = new StyleBackground(sprite);
}
```

### 8. Update `CountryInfoView.cs`

- Add `CharacterVisualConfig characterVisualConfig` parameter to the constructor.
- Pass it through when constructing `CharactersView`:

```csharp
_charactersView = new CharactersView(root.Q("characters-container"), loc, characterConfig, tooltip, characterVisualConfig);
```

### 9. Update `HUDDocument.cs`

- Add `CharacterVisualConfig _characterVisualConfig` field.
- Add it to the `[Inject] void Construct(...)` signature.
- Pass `_characterVisualConfig` when constructing `CountryInfoView` in `Awake`.

### 10. Register in `GameLifetimeScope`

- Add `[SerializeField] CharacterVisualConfig _characterVisualConfig;` field.
- In `Configure()`: `builder.RegisterInstance(_characterVisualConfig);`

### 11. Assign in Inspector (manual step)

In the Unity Editor, select the `GameLifetimeScope` GameObject in the Game scene and drag `Assets/Configs/CharacterVisualConfig.asset` to the `_characterVisualConfig` slot.

### 12. Update `CountryInfo.uss`

Remove the `background-color: rgb(100, 100, 100);` line from `.character-portrait`. The grey placeholder is no longer needed — when a sprite is present the portrait element shows it; when absent it shows nothing (transparent). If a fallback placeholder is desired, it can be reintroduced later.

---

Use /implement to start working on the plan or request changes.
