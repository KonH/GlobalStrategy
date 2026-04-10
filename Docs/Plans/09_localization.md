# 09 — Localization

## Goal

Add localization support for user-facing text in the Unity project, without touching the ECS layer or ConsoleRunner. Use a custom hand-rolled solution — no third-party or Unity localization packages.

## Scope

- **ECS layer (`src/`):** no localization — logic works with IDs, not display strings
- **ConsoleRunner:** no localization — developer/debug tool, raw values are fine
- **Unity (`Assets/Scripts/`):** all display text goes through localization

---

## Approach — Custom ScriptableObject Dictionary

One `LocaleConfig` ScriptableObject per language holds all key→value translations for that locale. Locale is identified by a plain string (e.g. `"en"`, `"ru"`). A `LocalizationConfig` ScriptableObject holds an array of `LocaleConfig` references and the active locale string.

### Data structure

```csharp
// One key-value pair inside a locale
[Serializable]
class LocaleEntry {
    public string Key;
    public string Value;
}

// One asset per language: en.asset, ru.asset
[CreateAssetMenu(fileName = "LocaleConfig", menuName = "Game/LocaleConfig")]
class LocaleConfig : ScriptableObject {
    public string Locale;          // e.g. "en", "ru"
    public LocaleEntry[] Entries;
}

// Root config — lists all locales, sets the default
[CreateAssetMenu(fileName = "LocalizationConfig", menuName = "Game/LocalizationConfig")]
class LocalizationConfig : ScriptableObject {
    public string DefaultLocale;   // e.g. "en"
    public LocaleConfig[] Locales;
}
```

Assets live at:
```
Assets/Localization/
  LocalizationConfig.asset   ← root, DefaultLocale = "en", Locales = [en.asset, ru.asset]
  en.asset                   ← Locale = "en", Entries = [...]
  ru.asset                   ← Locale = "ru", Entries = [...]
```

### Interface

```csharp
interface ILocalization {
    string Get(string key);
}
```

### Implementation

```csharp
class CustomLocalization : ILocalization {
    readonly LocalizationConfig _config;
    string _locale;
    LocaleConfig _active;

    public string CurrentLocale {
        get => _locale;
        set {
            _locale = value;
            _active = FindLocale(value);
        }
    }

    public CustomLocalization(LocalizationConfig config) {
        _config = config;
        CurrentLocale = config.DefaultLocale;
    }

    public string Get(string key) {
        if (_active != null)
            foreach (var e in _active.Entries)
                if (e.Key == key) return e.Value;
        return key; // fallback: raw key
    }

    LocaleConfig FindLocale(string locale) {
        foreach (var l in _config.Locales)
            if (l.Locale == locale) return l;
        return null;
    }
}
```

### VContainer registration

```csharp
[SerializeField] LocalizationConfig _localizationConfig;

// in Configure:
builder.Register<ILocalization>(
    _ => new CustomLocalization(_localizationConfig),
    Lifetime.Singleton);
```

### UI Toolkit integration — CountryInfoView

```csharp
class CountryInfoView {
    readonly VisualElement _root;
    readonly Label _name;
    readonly ILocalization _loc;

    public CountryInfoView(VisualElement root, ILocalization loc) {
        _root = root;
        _name = root.Q<Label>("country-name");
        _loc = loc;
    }

    public void Refresh(SelectedCountryState state) {
        _root.style.display = state.IsValid ? DisplayStyle.Flex : DisplayStyle.None;
        if (state.IsValid)
            _name.text = _loc.Get($"country_name.{state.CountryId}");
    }
}
```

`HUDDocument` injects `ILocalization` via `[Inject]` and passes it to view constructors.

### Key format

`country_name.{countryId}` where `countryId` matches the id in `CountryConfig`.

### Locale switching

```csharp
static class LocaleMenu {
    [MenuItem("Game/Locale/English")]
    static void SetEnglish() => GetLoc()?.CurrentLocale = "en";

    [MenuItem("Game/Locale/Russian")]
    static void SetRussian() => GetLoc()?.CurrentLocale = "ru";

    static CustomLocalization GetLoc() =>
        Object.FindFirstObjectByType<GameLifetimeScope>()
              ?.Container.Resolve<ILocalization>() as CustomLocalization;
}
```

Views re-render on the next state change.

---

## Implementation Steps

1. Create `LocaleEntry`, `LocaleConfig`, `LocalizationConfig` in `Assets/Scripts/Unity/UI/`
2. Create `ILocalization` interface and `CustomLocalization` implementation in the same folder
3. Create `Assets/Localization/en.asset` and `ru.asset` (`LocaleConfig`) and populate country name keys
4. Create `Assets/Localization/LocalizationConfig.asset` referencing both locale assets
5. Register `ILocalization` in `GameLifetimeScope` with `LocalizationConfig` serialized field
6. Update `CountryInfoView` to accept and use `ILocalization`
7. Update `HUDDocument` to inject `ILocalization` and pass it to `CountryInfoView`
8. Add `LocaleMenu.cs` editor script under `Assets/Scripts/Editor/Localization/`
