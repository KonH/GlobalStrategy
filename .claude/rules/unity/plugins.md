---
paths:
  - "src/**/*.csproj"
  - "Assets/Plugins/**"
---

# Unity Plugins (DLLs from src/)

## Output Path Convention

All `netstandard2.1` projects in `src/` that Unity needs add this to their `.csproj`:

```xml
<PropertyGroup Condition="'$(Configuration)'=='Release'">
    <OutputPath>../../Assets/Plugins/Core/</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
</PropertyGroup>
```

Running `dotnet build src/GlobalStrategy.Core.sln -c Release` then puts all DLLs directly into `Assets/Plugins/Core/` — no manual copy needed.

**Required:** after any change under `src/`, always end the turn with `/dotnet-build Release` (see `.claude/rules/workflow.md`). Do not leave Unity on stale plugin DLLs.

**Required before Unity work:** load the `unity-plugins` skill and run `python scripts/unity/ensure_plugin_dlls.py` before the first Unity Editor / MCP action in a session (see `.claude/skills/unity-plugins/SKILL.md`). Opening the Editor also regenerates missing/stale DLLs via `Assets/Scripts/Editor/PluginDlls/PluginDllRegenerator.cs`.

## Not committed

`Assets/Plugins/Core/*.dll`, `*.pdb`, `*.deps.json`, and `*.xml` are gitignored. They are local/CI build output — never stage or commit them. This avoids binary merge conflicts and keeps Git LFS from growing with every `src/` change.

**Do commit** `Assets/Plugins/Core/*.dll.meta` (and other `.meta` sidecars). Unity GUIDs for plugin assemblies live there. When adding a new Plugins-bound `src/` project, Release-build once, keep the new `*.dll.meta` (author it if Unity has not yet imported), and commit the `.meta` only.

## What Goes to Plugins

- `netstandard2.1` library projects that Unity scripts reference (ECS.Core, Game.Main, Game.Configs, etc.)

## What Does NOT Go to Plugins

- Source generator projects (`netstandard2.0`, `OutputItemType="Analyzer"`)
- Executable projects (`net8.0`): ConsoleRunner, Game.Configs.Loader
- `Core.Configs.IO` — uses `System.Text.Json` NuGet v8 which conflicts with Unity's bundled version and causes a load error

## System.Text.Json in Unity

`System.Text.Json` is **not available** in Unity scripts (it is not part of the .NET Standard 2.1 API surface exposed by Unity). Use Newtonsoft.Json instead:

- Package: `com.unity.nuget.newtonsoft-json` (already a transitive dep, add explicitly if needed)
- For Unity-side JSON: `JsonConvert.DeserializeObject<T>(json)`
- For `src/` projects that need JSON and target Unity: avoid `System.Text.Json` NuGet or keep them out of Plugins

## JSON Field Naming Convention

All config JSON files must use **camelCase** field names (e.g. `isAvailable`, `countryId`).

Newtonsoft.Json's default `DefaultContractResolver` matches properties case-insensitively but does **not** strip underscores. A snake_case key like `"is_available"` will NOT match a C# property named `IsAvailable` — the field is silently ignored and the property keeps its default value. No error is thrown.

## Assembly Naming

DLL name matches the project name exactly (no custom `<AssemblyName>`). The old `GlobalStrategy.Core` assembly name was removed — `Core.Map` now produces `Core.Map.dll`.
