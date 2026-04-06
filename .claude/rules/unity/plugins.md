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

## Assembly Naming

DLL name matches the project name exactly (no custom `<AssemblyName>`). The old `GlobalStrategy.Core` assembly name was removed — `Core.Map` now produces `Core.Map.dll`.
