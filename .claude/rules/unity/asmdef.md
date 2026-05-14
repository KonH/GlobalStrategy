# Assembly Definition Format

Each feature folder under `Scripts/` contains one `.asmdef` file. Use this template:

```json
{
    "name": "FeatureName",
    "rootNamespace": "",
    "references": [],
    "includePlatforms": [],
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

- `name` matches the folder name exactly (e.g. folder `Scripts/Samples/` → name `"Samples"`)
- `rootNamespace` is always `""` (empty)
- `references` use GUID format: `"GUID:7e93558c536f24149a3181fbe0a14523"` — get the GUID from the referenced assembly's `.asmdef.meta` file
- `includePlatforms` and `excludePlatforms` are both `[]` (all platforms)
- `allowUnsafeCode` is `false` unless explicitly needed
- `autoReferenced` is `true` so other assemblies can reference this one without explicit wiring
- **Exception:** Editor-only assemblies (`"includePlatforms": ["Editor"]`) must set `autoReferenced: false`. Setting it to `true` would make every other Editor assembly implicitly depend on it, causing unnecessary coupling. Editor tool assemblies are leaf nodes — nothing should reference them automatically.
- `noEngineReferences` is `false` (engine types available)
- To add a reference to another assembly, find its GUID in the corresponding `.asmdef.meta` file and add `"GUID:<guid>"` to the `references` array
