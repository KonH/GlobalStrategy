# 05 — ECS Core Framework

## Goal

Add a minimalistic, performance-oriented ECS framework as pure C# libraries inside the existing `src/` solution (`netstandard2.1`). No Unity dependency; designed for zero-GC steady state.

---

## Libraries

| Project | Target | Purpose |
|---|---|---|
| `ECS.Core` | netstandard2.1 | World, Entity, archetypes, component ops, query |
| `ECS.Core.Systems` | netstandard2.1 | ISystem marker, SystemGroup, runner interfaces |
| `ECS.Core.Extensions` | netstandard2.1 | Convenience helpers (GetOrAdd, AddRange, etc.) |
| `ECS.Core.SourceGenerators` | netstandard2.0 | Roslyn generator for query overloads + system dispatch |
| `ECS.Tests` | net8.0 | xUnit test suite for all core functionality |

All `ECS.*` projects register inside `GlobalStrategy.Core.sln`.

---

## Design

### Entity — packed `int`

```
bits [0..19]  = index  (up to ~1M entities)
bits [20..31] = generation (wraps at 4096 — stale ref detection)
```

Helper: `EntityPacker.Pack(int index, int gen) → int`, `Unpack(int id, out int index, out int gen)`.

---

### Component type identity

```csharp
static class TypeId<T> { static readonly int Value = Interlocked.Increment(ref _next); }
```

One `int` per component type, assigned at first access. Used as archetype signature keys.

---

### Archetype storage

- **Signature** = sorted `int[]` of TypeIds; equality by content.
- **Archetype** stores one `T[]` array per component type (SoA), plus a parallel `int[] entities`.
- `World._archetypes: Dictionary<Signature, Archetype>`.
- `World._records: EntityRecord[]` — maps entity index → `(Archetype, row)`.

Adding/removing a component **moves** the entity to a new archetype (copy row, swap-remove from old).

---

### World API

```csharp
int  entity = world.Create();
world.Destroy(int entity);

world.Add<TComp>(int entity, TComp comp);   // moves entity → new archetype
world.Remove<TComp>(int entity);             // moves entity → new archetype
bool world.Has<TComp>(int entity);
ref  TComp world.Get<TComp>(int entity);     // throws if absent
bool world.TryGet<TComp>(int entity, out TComp comp);  // value copy; returns false if absent
bool world.IsAlive(int entity);              // generation check

// Singletons — stored separately, not in archetypes
world.SetSingleton<T>(T value);
ref T world.GetSingleton<T>();
```

Entity ID recycling: freed indices go to a `Stack<int> _freeList`; generation bumped on each reuse.

---

### Query API — two options, **Option A is recommended**

#### Option A — callback with static lambda ✓

```csharp
world.Query<Position, Velocity>(
    static (int e, ref Position p, ref Velocity v) => { p.X += v.X; });

// With exclude filter:
world.Query<Position, Velocity>()
     .Exclude<Frozen>()
     .Run(static (int e, ref Position p, ref Velocity v) => { p.X += v.X; });
```

**Pros:** `ref` params work (no closure restriction on ref), source-generator produces typed overloads up to N components, minimal call-site boilerplate.
**Cons:** no early break; `static` keyword required on the lambda to prevent accidental allocation.

#### Option B — chunk foreach (alternative)

```csharp
foreach (var chunk in world.Query<Position, Velocity>().Exclude<Frozen>()) {
    for (int i = 0; i < chunk.Count; i++) {
        ref var p = ref chunk.C1(i);
        ref var v = ref chunk.C2(i);
        p.X += v.X;
    }
}
```

**Pros:** zero allocation, early break, explicit iteration.
**Cons:** verbose; ref struct enumerator cannot cross async boundaries or be stored.

Both options iterate per-archetype chunk — one call per matching archetype, zero entity-by-entity dispatch overhead.

Source generator produces Query overloads for 1–4 component types by default (configurable via MSBuild property `EcsMaxQueryArity`).

---

### System API

`ISystem` is a **marker interface** — no fixed `Update` method. The source generator reads the concrete `Update` signature and emits a runner.

```csharp
// User code (ECS.Core.Systems)
public partial struct MovementSystem : ISystem {
    public void Update(in World world, ref DeltaTime dt) {
        world.Query<Position, Velocity>(
            static (int e, ref Position p, ref Velocity v) => { p.X += v.X * dt.Value; });
    }
}

// Registration
var group = new SystemGroup(world);
group.Add<MovementSystem>();

// Each frame
group.Update();
```

**Singleton injection rules:**
- `in World world` — always first, injected by SystemGroup directly.
- `ref TSingleton` — mutable singleton; SystemGroup calls `world.GetSingleton<TSingleton>()` and passes by ref.
- `in TSingleton` — read-only singleton; same source, passed `in`.

Source generator emits one `ISystemRunner` implementation per system type. `SystemGroup` stores `List<ISystemRunner>` and calls `runner.Run(world)` per tick.

---

### Source Generator project

- Targets `netstandard2.0` (Roslyn analyzer requirement).
- Referenced by `ECS.Core` and `ECS.Core.Systems` as `<ProjectReference OutputItemType="Analyzer" ReferenceOutputAssembly="false">`.
- Generates:
  - `QueryCallback<C1..CN>` delegate types and `World.Query<C1..CN>` overloads into `ECS.Core`.
  - `ISystemRunner` implementations for each `ISystem` partial struct into `ECS.Core.Systems`.

---

### ECS.Core.Extensions

Convenience helpers that compose the core API — no new storage mechanisms:

- `world.GetOrAdd<TComp>(int entity, TComp defaultValue)`
- `world.AddRange<TComp>(ReadOnlySpan<int> entities, TComp comp)`
- `world.DestroyAll()` — clears all entities
- `QueryBuilder` fluent helper (if not already in Core)

---

### ECS.Tests — coverage checklist

- Entity lifecycle: `Create`, `Destroy`, `IsAlive`, generation reuse
- Component ops: `Add`, `Remove`, `Has`, `Get`, `TryGet` (present and absent cases)
- Archetype transitions: component arrays correct after add/remove; old archetype row removed
- Query single-component, multi-component, exclude filter
- Singleton: `SetSingleton`, `GetSingleton`, ref mutation visible on next read
- System: `Update` called with correct singleton values; `ref` param change persists
- Stale entity: accessing destroyed entity returns `IsAlive = false`, `TryGet = false`
- Generation wrap: reused entity ID does not appear alive under old id

---

## Steps

1. Add four `.csproj` files and register all five projects in `GlobalStrategy.Core.sln`.
2. Implement `ECS.Core`: `TypeId<T>`, `ArchetypeSignature`, `Archetype`, `EntityRecord`, `World`.
3. Implement `QueryBuilder` + query iteration (per-archetype loop, exclude mask).
4. Implement `ECS.Core.SourceGenerators`: query delegate overloads, system runner emitter.
5. Implement `ECS.Core.Systems`: `ISystem`, `ISystemRunner`, `SystemGroup`.
6. Implement `ECS.Core.Extensions`: helper methods.
7. Implement `ECS.Tests`: xUnit, cover the checklist above.
8. Build solution (`dotnet build`), run tests (`dotnet test`), fix any issues.
