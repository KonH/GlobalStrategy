# Plan: ECS Web Viewer

## Goal

A separate, optional web-based inspector that connects to a running ECS runtime (ConsoleRunner or Unity) via HTTP/WebSocket, allowing real-time entity/component inspection and pause control.

## Constraints & Key Observations

- `TypeId<T>.Value` is a runtime integer with no name — reflection is needed to recover type names (available in `net8.0` and netstandard2.1)
- `Archetype` columns are `Array` instances — values are readable via reflection (`GetValue`)
- Entity references are currently bare `int` IDs; a typed `EntityRef` wrapper is needed so the UI can render them as hyperlinks
- The viewer must not affect ECS performance when disabled
- `Game.Main` targets `netstandard2.1`; the HTTP server can live in a `net8.0` project (ConsoleRunner host or a new host library)
- Unity: embed a tiny `net8.0`-compatible HTTP listener inside a Unity Editor-only or runtime assembly

---

## Architecture

```
src/
  ECS.Viewer/            ← netstandard2.1 lib — pure observer API (no HTTP)
  ECS.Viewer.Server/     ← net8.0 lib — HttpListener + JSON serialization
  Game.ConsoleRunner/    ← existing, wires ECS.Viewer.Server (optional)

Assets/Scripts/EcsViewer/  ← Unity wrapper (starts server on Play, logs URL)
```

### Module responsibilities

| Module | Target | Contents |
|---|---|---|
| `ECS.Viewer` | netstandard2.1 | `WorldSnapshot`, `EntitySnapshot`, `ComponentSnapshot`, `EntityRef` struct, `IWorldObserver`, `PauseToken` |
| `ECS.Viewer.Server` | net8.0 | `HttpListener` REST API, JSON serialization, static web assets |
| Unity `EcsViewer` | netstandard2.1 (Unity) | `EcsViewerBridge` MonoBehaviour — starts server, hooks into `GameLogic.Update` pause token |

---

## Steps

### 1. `EntityRef` wrapper in `ECS.Core`

Add `src/ECS.Core/EntityRef.cs`:
```csharp
namespace ECS {
    public readonly struct EntityRef {
        public readonly int Id;
        public EntityRef(int id) => Id = id;
        public static implicit operator int(EntityRef r) => r.Id;
        public static implicit operator EntityRef(int id) => new EntityRef(id);
    }
}
```
No API changes required — `EntityRef` is used as a component field type; existing `int`-based World API is unchanged.

### 2. `ECS.Viewer` — snapshot types and observer interface

`WorldSnapshot`:
- `List<EntitySnapshot> Entities`

`EntitySnapshot`:
- `int Id`
- `List<ComponentSnapshot> Components`

`ComponentSnapshot`:
- `string TypeName`
- `Dictionary<string, object?> Fields` (populated via reflection)
- Fields whose declared type is `EntityRef` are serialized as `{ "__entityRef": id }`

`IWorldObserver`:
```csharp
public interface IWorldObserver {
    WorldSnapshot Capture(World world);
}
```

`PauseToken`:
```csharp
public class PauseToken {
    public bool IsPaused { get; set; }
}
```

**Reflection strategy:**
- `TypeId<T>.Value → Type` map: maintain a `Dictionary<int, Type>` populated each time `Add<T>` is called — add a `RegisterType<T>` hook on `World`, or populate it lazily from column array element types (`GetColumnRaw(typeId).GetType().GetElementType()`)
- Field values: `typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance)`; if field type is `EntityRef`, emit the special marker

### 3. `ECS.Viewer.Server` — HTTP server

Endpoints (REST over HTTP, JSON):
```
GET /snapshot                              → full WorldSnapshot JSON
GET /pause                                 → { "paused": bool }
POST /pause                                → body { "paused": bool }  → sets PauseToken
PATCH /entity/{id}/component/{typeName}    → body { "fieldName": value, ... } → writes field(s) back via reflection
```

Static assets:
- Embed `index.html`, `app.js` as embedded resources in the assembly
- Serve via `GET /`

Implementation:
- `System.Net.HttpListener` (no external deps)
- `System.Text.Json` for serialization (net8.0 only, no Unity)
- Start on a free port; print `http://localhost:{port}` to console

### 4. Wire into `Game.ConsoleRunner`

In `Program.cs`:
```csharp
#if DEBUG
var pauseToken = new PauseToken();
var observer = new WorldObserver();
var server = new ViewerServer(observer, pauseToken);
server.Start(); // logs URL
// In game loop: if (pauseToken.IsPaused) continue;
#endif
```

### 5. Wire into Unity (`EcsViewer` assembly)

`EcsViewerBridge : MonoBehaviour`:
- `[SerializeField] bool _enabled`
- `Awake`: if enabled, start `ViewerServer`, log URL to `Debug.Log`
- `[Inject] void Construct(GameLogic logic)` — store reference
- `Update`: feed `PauseToken.IsPaused` as an early-return guard before calling `GameLogic.Update` (or expose a `bool IsPaused` property on `GameLogic` that `Update` checks internally)

**Note on Unity web builds:** `HttpListener` requires sockets and is not available in WebGL. The viewer is Editor/standalone only. Mark with `#if !UNITY_WEBGL`.

### 6. Web UI (`index.html` + `app.js`)

Single-page, no framework, vanilla JS:

- **Pause/Resume button** — `POST /pause` toggle
- **Filter bar** — list of component type names with +/- toggles; filters entity list by presence/absence
- **Entity list** — each row: `Entity #id | CompA, CompB, ...`; click → expand detail
- **Entity detail panel** — table of field/value pairs per component:
  - `EntityRef` fields render as `→ #id` hyperlink that selects that entity in the list
  - Primitive fields (`int`, `float`, `double`, `bool`, `string`, `enum`) render as inline editable inputs
  - Editing a field sends `PATCH /entity/{id}/component/{typeName}` with the changed value; response triggers a snapshot refresh
  - Struct fields that are not `EntityRef` and not primitive display as read-only (no nested editing)
- **Auto-refresh** — `setInterval(() => fetch('/snapshot')..., 500)` when not paused; suppressed while an edit input is focused

### 7. `EntityRef` adoption in components

Replace bare `int` entity-reference fields in `Game.Components` with `EntityRef` wherever a field holds an entity ID. Audit all structs in `Game.Components` for `int` fields that represent entity references and migrate them. This is required — the viewer relies on `EntityRef` field type detection to render entity links correctly.

---

## Tests

Changes land in `ECS.Core` (new `EntityRef`) and new `ECS.Viewer` / `ECS.Viewer.Server` projects under `src/`. Add tests in `ECS.Tests` and a new `ECS.Viewer.Tests` project.

### `ECS.Tests` additions

- `EntityRef` round-trips through implicit int conversions
- `World.Add<T>` with a component containing an `EntityRef` field stores and retrieves it correctly

### `ECS.Viewer.Tests` (new project, `net8.0`)

**Snapshot capture:**
- World with two entities and mixed components → `WorldObserver.Capture` returns correct entity count and component type names
- `EntityRef` field is serialized as `{ "__entityRef": id }` in `ComponentSnapshot.Fields`
- Filtering: `EntitySnapshot` for entity without a required component is excluded; entity without an excluded component is included

**Field editing (reflection write-back):**
- Primitive field (`int`, `float`, `bool`, `string`) on a live entity is updated correctly via the same reflection path the server uses
- Enum field is updated by name string → enum value
- `EntityRef` field is treated as read-only (write attempt is ignored / throws a known exception)

**Server endpoints (integration, using `HttpListener` on a random port):**
- `GET /snapshot` returns valid JSON matching the live world state
- `POST /pause` with `{ "paused": true }` sets `PauseToken.IsPaused = true`; subsequent `GET /pause` returns `true`
- `PATCH /entity/{id}/component/{typeName}` with a valid primitive payload updates the component and returns 200
- `PATCH` with an unknown entity or type name returns 404

---

## Not in scope

- Unity web build support (HttpListener unavailable in WebGL)
- Nested struct field editing (display only)
- Source generators for component metadata (reflection is sufficient)
