# 07 — VContainer Integration for Game Architecture

## Goal

Wire the pure-C# game logic layer (`GameLogic`, `VisualState`, `IWriteOnlyCommandAccessor`) introduced in plan 06 into the Unity scene using VContainer for dependency injection. Replace serialized-field wiring and `VisualStateHolder` with a proper DI container.

Unity side only — the `src/` projects themselves are out of scope here.

---

## Approach

VContainer's `LifetimeScope` acts as the composition root. It constructs `GameLogic` (passing `IConfigSource<T>` implementations backed by `StreamingAssets` files), exposes `VisualState` and `IWriteOnlyCommandAccessor` as injectable singletons, and injects them into MonoBehaviours via `[Inject]`.

A single `GameLifetimeScope` replaces the `VisualStateHolder` pattern. MonoBehaviours stop holding `[SerializeField] VisualStateHolder` and receive their dependencies through method injection.

---

## Dependency Graph (Unity layer)

```
GameLifetimeScope (LifetimeScope)
  registers:
    GameLogicContext  ← FileConfig<T> from StreamingAssets
    GameLogic         ← singleton
    VisualState       ← forwarded from GameLogic.VisualState
    IWriteOnlyCommandAccessor ← forwarded from GameLogic.Commands

  injects into:
    GameLoopRunner    ← calls GameLogic.Update each frame
    MapClickHandler   ← pushes SelectCountryCommand
    CountryInfoUIDocument ← reads VisualState.SelectedCountry
```

---

## Steps

### 1. Add `GameLifetimeScope`

New script `Assets/Scripts/Unity/DI/GameLifetimeScope.cs`:

```csharp
using VContainer;
using VContainer.Unity;
using Game.Main;
using Game.Configs;
using Core.Configs.IO;

public class GameLifetimeScope : LifetimeScope {
    protected override void Configure(IContainerBuilder builder) {
        var ctx = new GameLogicContext(
            new FileConfig<GeoJsonConfig>(StreamingAssetsPath("geojson_world.json")),
            new FileConfig<MapEntryConfig>(StreamingAssetsPath("map_entry_config.json")),
            new FileConfig<CountryConfig>(StreamingAssetsPath("country_config.json"))
        );

        builder.RegisterInstance(ctx);
        builder.Register<GameLogic>(Lifetime.Singleton);
        builder.Register(c => c.Resolve<GameLogic>().VisualState, Lifetime.Singleton);
        builder.Register(c => c.Resolve<GameLogic>().Commands, Lifetime.Singleton);

        builder.RegisterEntryPoint<GameLoopRunner>();
    }

    static string StreamingAssetsPath(string file) =>
        System.IO.Path.Combine(UnityEngine.Application.streamingAssetsPath, "Configs", file);
}
```

### 2. Add `GameLoopRunner`

New script `Assets/Scripts/Unity/DI/GameLoopRunner.cs` — drives `GameLogic.Update` each frame:

```csharp
using VContainer.Unity;
using Game.Main;

public class GameLoopRunner : ITickable {
    readonly GameLogic _logic;
    public GameLoopRunner(GameLogic logic) => _logic = logic;
    public void Tick() => _logic.Update(UnityEngine.Time.deltaTime);
}
```

### 3. Update `MapClickHandler`

- Remove `[SerializeField] VisualStateHolder _stateHolder`
- Remove `[SerializeField] CountryConfig _countryConfig` (now comes from `GameLogic` world)
- Add `[Inject] void Construct(IWriteOnlyCommandAccessor commands)` method
- On click: push `new SelectCountryCommand(countryId)` instead of mutating `VisualState` directly

### 4. Update `CountryInfoUIDocument`

- Remove `[SerializeField] VisualStateHolder _stateHolder`
- Add `[Inject] void Construct(VisualState state)` method
- Subscribe to `state.SelectedCountry.OnChanged` as before

### 5. Remove `VisualStateHolder`

Delete `Assets/Scripts/Unity/VisualState/VisualStateHolder.cs` and its `.meta`. It is replaced by VContainer injection.

### 6. Scene wiring

- Add a `GameLifetimeScope` GameObject to the scene (with the `GameLifetimeScope` component)
- Remove the old `VisualStateHolder` GameObject
- Clear `[SerializeField]` refs to `VisualStateHolder` from all scene objects
- Save scene

### 7. Verify

- Play mode: click a country → `CountryInfoUIDocument` shows name
- No `NullReferenceException` from missing serialized refs
- `GameLogic.VisualState.SelectedCountry.CountryId` matches clicked feature

---

## Notes

- `FileConfig<T>` from `Core.Configs.IO` uses `File.ReadAllText` — valid on desktop/editor; mobile needs `UnityWebRequest` (out of scope).
- `IWriteOnlyCommandAccessor.Push<T>` is the only surface `MapClickHandler` needs — keeps it decoupled from the full `GameLogic`.
- `VisualState` and `IWriteOnlyCommandAccessor` are forwarded registrations from `GameLogic`; VContainer resolves them lazily after `GameLogic` is constructed.
