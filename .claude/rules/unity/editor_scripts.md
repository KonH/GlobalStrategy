# Editor Scripts

## Accessing Runtime State from Menu Items

Editor scripts (`[MenuItem]`) run outside the VContainer scope and cannot call `container.Resolve<T>()`. When a menu item needs runtime state from a MonoBehaviour (e.g. a server URL, a port, a running flag), expose it as a `static` property on the MonoBehaviour:

```csharp
// On the MonoBehaviour:
public static string? CurrentUrl { get; private set; }

void Awake()  { CurrentUrl = $"http://localhost:{port}"; }
void OnDestroy() { CurrentUrl = null; }
```

```csharp
// In the Editor script:
[MenuItem("Game/MyFeature/Open")]
static void Open() => Application.OpenURL(EcsViewerBridge.CurrentUrl!);

[MenuItem("Game/MyFeature/Open", validate = true)]
static bool OpenValidate() => EcsViewerBridge.CurrentUrl != null;
```

Use `validate = true` on the paired `[MenuItem]` to gray out the item when the value is null/invalid — this gives clear feedback that the feature requires Play mode.

## Assembly Setup

Editor-only assemblies must set `"includePlatforms": ["Editor"]` in their `.asmdef`. They reference runtime assemblies by GUID as normal; the reverse is not allowed (runtime assemblies must not reference Editor assemblies).
