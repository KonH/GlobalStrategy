# 06 — Game Architecture (ECS + Commands + VisualState)

## Goal

Introduce a layered, pure-C# game architecture with strict access control, clean dependencies, and one-directional data flow:

```
Client writes commands → Systems read commands, update World → World translated to VisualState
```

Unity is explicitly out of scope for this plan (covered separately).

---

## Dependency Graph

```
Core.Configs  ←── Core.Configs.IO
     ↑
Game.Configs ←── Game.Configs.Loader (exe)
     ↑                  ↑
Game.Components    Core.Configs.IO
Game.Commands
     ↑
Game.Systems (Game.Components + Game.Configs + ECS.Core)
     ↑
Game.Main (Game.Systems + Game.Commands + Game.Components)
     ↑
Game.ConsoleRunner (Game.Main + Core.Configs.IO)
```

---

## Projects

### Core.Configs — `src/Core.Configs/`
Target: `netstandard2.1`. No deps.

```csharp
interface IConfigSource<TConfig> {
    TConfig Load();
}
```

---

### Game.Configs — `src/Game.Configs/`
Target: `netstandard2.1`. No deps.

Plain data classes (no logic):
- `GeoJsonConfig` — raw GeoJSON structure
- `MapEntryConfig` — per-feature ID/display metadata
- `CountryConfig` — country list with feature IDs

---

### Core.Configs.IO — `src/Core.Configs.IO/`
Target: `netstandard2.1`. Deps: `Core.Configs`.

```csharp
class FileConfig<TConfig> : IConfigSource<TConfig> {
    FileConfig(string filePath);
    TConfig Load(); // File.ReadAllText + JSON deserialize
}
```

---

### Game.Configs.Loader — `src/Game.Configs.Loader/`
Target: `net8.0` (executable). Deps: `Game.Configs`, `Core.Configs.IO`.

Replaces the Unity menu item tool. Reads paths from `loader_config.json` (project-relative):

```json
{
  "geoJsonSourcePath": "../../RawData/world.geojson",
  "outputPath": "../../Assets/StreamingAssets/Configs"
}
```

Steps:
1. Read raw GeoJSON source file
2. Normalize feature IDs
3. Write `geojson_world.json`, `map_entry_config.json`, `country_config.json` to `outputPath`

---

### Game.Components — `src/Game.Components/`
Target: `netstandard2.1`. No deps.

POCO structs for ECS:
```csharp
// Common/
record struct Country(string CountryId);
struct IsSelected { }
```

---

### Game.Commands — `src/Game.Commands/`
Target: `netstandard2.1`. No deps.

Command types are plain structs. A marker interface enables source generation:
```csharp
interface ICommand { }

// Visual/
record struct SelectCountryCommand(string CountryId) : ICommand;
```

---

### ECS.Core — add `IReadOnlyWorld`

`World` gains a read-only interface exposing only query and singleton read operations:

```csharp
interface IReadOnlyWorld {
    bool IsAlive(int entity);
    bool Has<TComp>(int entity);
    TComp Get<TComp>(int entity);
    bool TryGet<TComp>(int entity, out TComp comp);
    ref TComp GetSingleton<TComp>();
    QueryBuilder Query(); // read-only query entry point
}

class World : IReadOnlyWorld { ... }
```

Used by `VisualStateConverter` to prevent accidental mutation.

---

### Source Generator split

| Project | Responsibility |
|---|---|
| `ECS.Core.SourceGenerators` | ECS query overloads, system runner dispatch — unchanged |
| `Game.SourceGenerators` | **Command buffer + accessor generation** (scans `ICommand` types from `Game.Commands`); any future game-specific generation |

Both target `netstandard2.0` (Roslyn requirement) and are referenced as analyzers by their consumers.

---

### Command System — source generation (`Core.SourceGenerators`)

Scans for types implementing `ICommand` and emits:

```csharp
// Generated — one per ICommand type
class SelectCountryCommandBuffer {
    List<SelectCountryCommand> _items = new();
    void Add(SelectCountryCommand cmd) => _items.Add(cmd);
    ReadOnlySpan<SelectCountryCommand> AsSpan() => CollectionsMarshal.AsSpan(_items);
    void Clear() => _items.Clear();
}

// Generated — typed accessor aggregate
partial class CommandAccessor {
    SelectCountryCommandBuffer SelectCountry { get; } = new();

    void IWriteOnlyCommandAccessor.Push(SelectCountryCommand cmd)
        => SelectCountry.Add(cmd);

    ReadCommands<SelectCountryCommand> IReadOnlyCommandAccessor.Read<SelectCountryCommand>()
        => new(SelectCountry.AsSpan());

    void Clear() {
        SelectCountry.Clear();
        // … all buffers
    }
}
```

No boxing, no dictionary lookup — each command type has its own `List<T>`.

`ReadCommands<T>` wraps a `ReadOnlySpan<T>` and is the unit passed to systems:

```csharp
readonly ref struct ReadCommands<T> {
    ReadOnlySpan<T> _span;
    // batch access — systems see all commands of this type at once
    public void ForEach(Action<T> handler) { foreach (var c in _span) handler(c); }
    public ReadOnlySpan<T> AsSpan() => _span;
    public int Count => _span.Length;
}
```

Systems receive the full batch, not one command at a time — consistent with ECS per-archetype chunk iteration.

---

### Game.Systems — `src/Game.Systems/`
Target: `netstandard2.1`. Deps: `Game.Components`, `Game.Configs`, `ECS.Core`.

Static stateless systems:
```csharp
static class SelectCountrySystem {
    public static void Update(World world, ReadCommands<SelectCountryCommand> commands) {
        if (commands.Count == 0) return;
        // only last command matters for selection
        var cmd = commands.AsSpan()[^1];
        world.Query<Country>(static (e, ref Country c) => {
            if (c.CountryId == cmd.CountryId) world.Add<IsSelected>(e);
            else world.Remove<IsSelected>(e);
        });
    }
}
```

---

### Game.Main — `src/Game.Main/`
Target: `netstandard2.1`. Deps: `Game.Systems`, `Game.Commands`, `Game.Components`, `Game.Configs`, `Core.Configs`, `ECS.Core`.

#### CommandAccessor (internal, partially generated)
```csharp
interface IWriteOnlyCommandAccessor {
    void Push<TCommand>(TCommand cmd) where TCommand : ICommand;
}
interface IReadOnlyCommandAccessor {
    ReadCommands<TCommand> Read<TCommand>() where TCommand : ICommand;
}
// CommandAccessor : IWriteOnlyCommandAccessor, IReadOnlyCommandAccessor
// — partial class, generated side adds typed buffers and method dispatch
```

#### VisualState
```csharp
class SelectedCountryState : INotifyPropertyChanged {
    public bool IsValid { get; private set; }
    public string CountryId { get; private set; }
    public void Set(bool isValid, string countryId); // atomic, fires OnChanged once
}

class VisualState {
    public SelectedCountryState SelectedCountry { get; } = new();
}
```

#### VisualStateConverter (internal)
```csharp
class VisualStateConverter {
    VisualState _state;
    void Update(IReadOnlyWorld world);
    // queries Country + IsSelected, calls _state.SelectedCountry.Set(...)
}
```

#### GameLogicContext
```csharp
record GameLogicContext(
    IConfigSource<GeoJsonConfig> GeoJson,
    IConfigSource<MapEntryConfig> MapEntry,
    IConfigSource<CountryConfig> Country
);
```

#### GameLogic
```csharp
class GameLogic {
    GameLogic(GameLogicContext context);

    public VisualState VisualState { get; }            // read-only UI binding
    public IWriteOnlyCommandAccessor Commands { get; } // write-only for callers

    public void Update(float deltaTime);
    // → _systems.Update(_world, _commandAccessor)
    // → _commandAccessor.Clear()
    // → _visualStateConverter.Update(_world)  — mutates VisualState in place
}
```

---

### Game.ConsoleRunner — `src/Game.ConsoleRunner/`
Target: `net8.0` (executable). Deps: `Game.Main`, `Core.Configs.IO`.

```csharp
static class Program {
    static void Main() {
        var ctx = new GameLogicContext(
            new FileConfig<GeoJsonConfig>("data/geojson_world.json"),
            new FileConfig<MapEntryConfig>("data/map_entry_config.json"),
            new FileConfig<CountryConfig>("data/country_config.json")
        );
        var logic = new GameLogic(ctx);
        logic.Commands.Push(new SelectCountryCommand("FR"));
        logic.Update(0f);
        Console.WriteLine(logic.VisualState.SelectedCountry.CountryId); // "FR"
    }
}
```

---

## Steps

1. Add `IReadOnlyWorld` to `ECS.Core.World`.
2. Add `Core.Configs` project + `IConfigSource<T>`.
3. Add `Game.Configs` project with data classes (migrate from existing Unity scripts).
4. Add `Core.Configs.IO` project with `FileConfig<T>`.
5. Add `Game.Commands` project with `ICommand` marker and `SelectCountryCommand`.
6. Add `Game.SourceGenerators` project; implement command buffer generation (scan `ICommand`, emit typed buffers + `partial CommandAccessor`).
7. Add `Game.Components` project with `Country`, `IsSelected`.
8. Add `Game.Systems` project with `SelectCountrySystem`.
9. Add `Game.Main` project: `CommandAccessor` (partial), `VisualState`, `VisualStateConverter`, `GameLogic`.
10. Add `Game.Configs.Loader` executable with `loader_config.json` path config.
11. Add `Game.ConsoleRunner` executable; smoke-test the full update loop.
12. Register all new projects in `GlobalStrategy.Core.sln`.
13. Build + run `Game.ConsoleRunner`; verify end-to-end.
