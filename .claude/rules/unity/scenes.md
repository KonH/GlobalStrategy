# Scene Format and Registration

## File Format

Scene files (`.unity`) are Unity YAML files (`%YAML 1.1`). They must be created through the Unity Editor. Structure:

**Fixed header objects** (always present, fileIDs 1–4):
- `!u!29 &1` — OcclusionCullingSettings
- `!u!104 &2` — RenderSettings
- `!u!157 &3` — LightmapSettings
- `!u!196 &4` — NavMeshSettings

**Scene content** — GameObjects, components, and prefab instances with large random fileIDs.

**Prefab instances in a scene** use `!u!1001 PrefabInstance` blocks:
```yaml
--- !u!1001 &<fileID>
PrefabInstance:
  m_Modification:
    m_TransformParent: {fileID: 0}          # 0 = root; or fileID of parent Transform
    m_Modifications:
    - target: {fileID: <obj-fileID>, guid: <prefab-guid>, type: 3}
      propertyPath: m_LocalPosition.x
      value: 0
      objectReference: {fileID: 0}
    m_RemovedComponents: []
    m_RemovedGameObjects: []
    m_AddedGameObjects: []
    m_AddedComponents: []
  m_SourcePrefab: {fileID: 100100000, guid: <prefab-guid>, type: 3}
```

**SceneRoots** (always the last object, fileID `9223372036854775807`) lists the fileIDs of all root-level Transform/PrefabInstance objects:
```yaml
--- !u!1660057539 &9223372036854775807
SceneRoots:
  m_Roots:
  - {fileID: <transform-or-prefabinstance-fileID>}
```

## MCP Workflow (preferred)

When Unity Editor is connected via UnityMCP, use MCP tools instead of hand-editing YAML:

- **Inspect scene:** `manage_scene` (action `get_hierarchy`) or `mcpforunity://scene/gameobject-api`
- **Add prefab instance:** `manage_gameobject` (action `create`, param `prefab_path`)
- **Add plain GameObject:** `manage_gameobject` (action `create`)
- **Modify object:** `manage_gameobject` (action `modify`) or `manage_components` (action `set_property`)
- **Remove object:** `manage_gameobject` (action `delete`)
- **Save scene:** `manage_scene` (action `save`)

Scene registration in `EditorBuildSettings.asset` has no MCP tool — edit that file directly (see Scene Registration section below).

Fall back to the manual YAML workflow below only when MCP is unavailable.

## Modifying Scenes via Text

Scene files can be modified by hand if the rules below are followed precisely.

### FileID generation

Every object in a scene needs a unique positive integer fileID. When adding new objects, pick a large random integer (e.g. 9 digits) that does not collide with any existing `&fileID` anchor in the file. FileIDs 1–4 are reserved for the header objects.

### Adding a prefab instance

1. Get the prefab's GUID from its `.meta` file.
2. Get the fileIDs of the prefab's internal objects from the `.prefab` file (needed for `m_Modifications` targets).
3. Choose a new unique fileID for the `PrefabInstance` block.
4. Append the block before `SceneRoots`:

```yaml
--- !u!1001 &<new-fileID>
PrefabInstance:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_Modification:
    serializedVersion: 3
    m_TransformParent: {fileID: 0}
    m_Modifications:
    - target: {fileID: <root-transform-fileID-in-prefab>, guid: <prefab-guid>, type: 3}
      propertyPath: m_LocalPosition.x
      value: 0
      objectReference: {fileID: 0}
    - target: {fileID: <root-transform-fileID-in-prefab>, guid: <prefab-guid>, type: 3}
      propertyPath: m_LocalPosition.y
      value: 0
      objectReference: {fileID: 0}
    - target: {fileID: <root-transform-fileID-in-prefab>, guid: <prefab-guid>, type: 3}
      propertyPath: m_LocalPosition.z
      value: 0
      objectReference: {fileID: 0}
    - target: {fileID: <root-transform-fileID-in-prefab>, guid: <prefab-guid>, type: 3}
      propertyPath: m_LocalRotation.x
      value: 0
      objectReference: {fileID: 0}
    - target: {fileID: <root-transform-fileID-in-prefab>, guid: <prefab-guid>, type: 3}
      propertyPath: m_LocalRotation.y
      value: 0
      objectReference: {fileID: 0}
    - target: {fileID: <root-transform-fileID-in-prefab>, guid: <prefab-guid>, type: 3}
      propertyPath: m_LocalRotation.z
      value: 0
      objectReference: {fileID: 0}
    - target: {fileID: <root-transform-fileID-in-prefab>, guid: <prefab-guid>, type: 3}
      propertyPath: m_LocalRotation.w
      value: 1
      objectReference: {fileID: 0}
    - target: {fileID: <root-gameobject-fileID-in-prefab>, guid: <prefab-guid>, type: 3}
      propertyPath: m_Name
      value: MyInstance
      objectReference: {fileID: 0}
    m_RemovedComponents: []
    m_RemovedGameObjects: []
    m_AddedGameObjects: []
    m_AddedComponents: []
  m_SourcePrefab: {fileID: 100100000, guid: <prefab-guid>, type: 3}
```

5. Add `{fileID: <new-fileID>}` to `SceneRoots.m_Roots`.

To set position to something other than origin, change the `value` fields for `m_LocalPosition.*`.

To override a serialized field on a component inside the prefab, add a `m_Modifications` entry pointing to that component's fileID and `propertyPath` matching the C# field name.

To parent the instance under another GameObject's Transform, set `m_TransformParent: {fileID: <parent-transform-fileID>}` and do **not** add it to `SceneRoots`.

### Adding a plain GameObject

Requires three blocks: GameObject (`!u!1`), Transform (`!u!4`), and any components. Each gets its own unique fileID. Cross-references:
- `GameObject.m_Component` lists all component fileIDs
- `Transform.m_GameObject` points to its GameObject
- `Transform.m_Father: {fileID: 0}` for root objects
- Add the Transform fileID to `SceneRoots.m_Roots` for root objects

### Modifying an existing object

Edit the value directly in the relevant block. For prefab instances, only change `value` fields inside `m_Modifications` — do not change `target.fileID` or `target.guid`.

### Removing a prefab instance

Delete its `PrefabInstance` block and remove its fileID from `SceneRoots.m_Roots`.

## Scene Registration

To include a scene in the build, add it to `ProjectSettings/EditorBuildSettings.asset`:

```yaml
m_Scenes:
- enabled: 1
  path: Assets/Scenes/Feature/SceneName.unity
  guid: <scene-guid>
```

- `guid` comes from the scene's `.meta` file (`guid:` field)
- Scenes not listed here are excluded from builds (but can still be loaded by path in the Editor)
- Order in `m_Scenes` determines the build index used by `SceneManager.LoadScene(int)`
