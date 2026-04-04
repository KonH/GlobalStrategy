# Unity Project Structure

```
Assets/
├── Plugins/
│   └── Core/            # prebuilt DLL(s) from src/
├── Prefabs/
│   └── [Feature]/       # e.g. UI/, Units/
├── Scenes/
│   └── [Feature]/
├── Scripts/
│   └── [Feature]/       # contains .cs files and one .asmdef per feature folder
└── UI/
    └── [Feature]/       # UXML, USS, PanelSettings assets

src/                     # plain C# solution, Unity-independent
└── GlobalStrategy.Core/
    └── Map/             # domain logic with no UnityEngine dependency
```

- Every feature folder under `Scripts/` has exactly one `.asmdef` file named after the folder
- Prefabs and scenes mirror the same feature subfolder names used in Scripts
- Do not put assets directly under `Assets/` root (except Unity-generated files)
- `Assets/Plugins/Core/` holds the DLL built from `src/`; rebuild with `dotnet build src/GlobalStrategy.Core.sln -c Release`
- Asmdefs must not reference assemblies whose source has moved to `src/`; the DLL is picked up automatically from `Plugins/`

## MCP Workflow

When Unity Editor is connected via UnityMCP:

- Use `manage_asset` (action `search`) to verify existing asset paths before creating new ones
- Use `create_script` for new `.cs` files — place them under `Assets/Scripts/[Feature]/`
- `.asmdef` files have no MCP tool — write them directly following `.claude/rules/unity/asmdef.md`
- Before creating a new feature folder, check `mcpforunity://project/info` for the `assetsPath`
