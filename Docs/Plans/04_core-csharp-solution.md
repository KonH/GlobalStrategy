# 04 — Core C# Solution (independent from Unity)

## Goal

Extract the game's core logic into a plain C# class library that compiles independently of Unity, lives under `src/`, and is consumed by the Unity project as a prebuilt DLL placed in `Assets/Plugins/`.

## Approach

Create a .NET class library solution at `src/GlobalStrategy.Core.sln`. The library targets `netstandard2.1` (compatible with Unity's Mono/IL2CPP runtimes). The Unity project drops the resulting DLL into `Assets/Plugins/Core/` and removes the corresponding source-based assembly definition. A CI/build script (or developer workflow) rebuilds the DLL on demand.

## Steps

### 1. Identify what belongs in Core

- Audit `Assets/Scripts/Core/Map/` — these are already Unity-agnostic (`Vector2d`, `Ring`, `Polygon`, `MapFeature`, `GeoJsonParser`)
- Confirm no `UnityEngine` references exist in that folder (they don't)

### 2. Create the C# solution

```
src/
  GlobalStrategy.Core/
    GlobalStrategy.Core.csproj   ← TargetFramework: netstandard2.1
    Map/
      Vector2d.cs
      Ring.cs
      Polygon.cs
      MapFeature.cs
      GeoJsonParser.cs
  GlobalStrategy.Core.sln
```

- `src/GlobalStrategy.Core/GlobalStrategy.Core.csproj`:
  - `<TargetFramework>netstandard2.1</TargetFramework>`
  - `<Nullable>enable</Nullable>` (optional)
  - No Unity or third-party dependencies
- Add a `.gitignore` entry for `src/**/bin/` and `src/**/obj/`

### 3. Move source files

- Copy (not move yet) `.cs` files from `Assets/Scripts/Core/Map/` into `src/GlobalStrategy.Core/Map/`
- Keep namespaces identical (`GS.Core.Map`) so Unity code referencing them needs no changes
- Verify the project builds: `dotnet build src/GlobalStrategy.Core.sln -c Release`

### 4. Output DLL to Unity Plugins

- Set output path in `.csproj`:
  ```xml
  <PropertyGroup Condition="'$(Configuration)'=='Release'">
    <OutputPath>../../Assets/Plugins/Core/</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
  </PropertyGroup>
  ```
- After first successful build, `Assets/Plugins/Core/GlobalStrategy.Core.dll` appears

### 5. Remove source from Unity

- Delete `Assets/Scripts/Core/Map/*.cs` and their `.meta` files
- Delete `Assets/Scripts/Core/Map/GS.Core.Map.asmdef` and its `.meta`
- Delete `Assets/Scripts/Core/` folder and `.meta` if empty
- Refresh Unity — it now resolves types from the DLL

### 6. Unity DLL import settings

- Select `Assets/Plugins/Core/GlobalStrategy.Core.dll` in the Project window
- Ensure platform includes `Editor` and `Standalone` (default for Plugins folder)
- Add `Assets/Plugins/Core/GlobalStrategy.Core.dll.meta` to version control (Unity-managed)

### 7. Update downstream assembly references

- `GS.Unity.Map.asmdef` currently references `GS.Core.Map` by GUID — remove that reference since the DLL is auto-referenced via `Assets/Plugins/`
- Check all other asmdefs for references to `GS.Core.Map` and remove them

### 8. Verify

- Unity compiles without errors after source removal
- Play-mode map rendering works (GeoJSON parsing still runs)
- `dotnet test` (if tests are added later) passes in isolation

## Notes

- The DLL is committed to the repo under `Assets/Plugins/Core/` (binary artifact, Unity convention)
- For iteration speed, developers run `dotnet build` before entering Unity; a future CI step can enforce this
- The `.pdb` file can optionally be copied alongside the DLL for editor-side stack traces
