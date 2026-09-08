---
paths:
  - "Assets/**"
  - "src/**"
---

# Unity Project Structure

- Every feature folder under `Scripts/` has exactly one `.asmdef` file named after the folder
- Prefabs and scenes mirror the same feature subfolder names used in Scripts
- Do not put assets directly under `Assets/` root (except Unity-generated files)
- `Assets/Plugins/Core/` holds the DLLs built from `src/` (gitignored binaries, committed `*.dll.meta`); rebuild with `dotnet build src/GlobalStrategy.Core.sln -c Release` or the `unity-plugins` skill. Opening the Editor also regenerates missing/stale DLLs.
- Asmdefs must not reference assemblies whose source has moved to `src/`; the DLL is picked up automatically from `Plugins/`

## MCP Workflow

When Unity Editor is connected via UnityMCP:

- Use `manage_asset` (action `search`) to verify existing asset paths before creating new ones
- Use `create_script` for new `.cs` files — place them under `Assets/Scripts/[Feature]/`
- `.asmdef` files have no MCP tool — write them directly following `.claude/rules/unity/asmdef.md`
- Before creating a new feature folder, check `mcpforunity://project/info` for the `assetsPath`
