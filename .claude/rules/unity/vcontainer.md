# VContainer Usage

## Composition Root

`GameLifetimeScope : LifetimeScope` is the single composition root per scene. It lives on a dedicated `GameLifetimeScope` GameObject.

## Registration Patterns

| What | How |
|---|---|
| MonoBehaviour already in scene hierarchy | `builder.RegisterComponentInHierarchy<T>()` |
| ScriptableObject / data asset | `[SerializeField] T _field;` on the scope, then `builder.RegisterInstance(_field)` |
| Pure C# singleton | `builder.Register<T>(Lifetime.Singleton)` |
| Forwarded property (e.g. sub-object of a singleton) | `builder.Register<IFoo>(c => c.Resolve<Bar>().Foo, Lifetime.Singleton)` |
| Entry point (ITickable / IStartable) | `builder.RegisterEntryPoint<T>()` |

## MonoBehaviour Injection

Always use a `[Inject]` method — never constructor injection on MonoBehaviours:

```csharp
[Inject]
void Construct(IWriteOnlyCommandAccessor commands, MapController map) {
    _commands = commands;
    _map = map;
}
```

## Assembly Setup

Any assembly whose scripts use `[Inject]` must reference VContainer in its `.asmdef`:

```json
"references": [
    "GUID:b0214a6008ed146ff8f122a6a9c2f6cc"
]
```

VContainer GUID: `b0214a6008ed146ff8f122a6a9c2f6cc`  
VContainer.Unity GUID (ITickable etc.): same package, same asmdef.

## Interface Registration

Use the typed overload when registering by interface:

```csharp
builder.Register<IWriteOnlyCommandAccessor>(c => c.Resolve<GameLogic>().Commands, Lifetime.Singleton);
```

## What NOT to Put in the Container

- Prefab references used only as factory input to `Instantiate` — keep as `[SerializeField]`
- Pure data assets (TextAsset, Texture2D) wired to a single component — keep as `[SerializeField]`
