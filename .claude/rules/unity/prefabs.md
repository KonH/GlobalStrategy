# Prefab and Prefab Variant Format

Prefabs are Unity YAML files (`%YAML 1.1`). They can be created and modified by hand following the rules below. FileIDs are large unique positive integers — pick random 9–18 digit values that don't collide with existing anchors in the file.

## Regular Prefab

Contains all GameObjects and components serialized as top-level YAML documents. Each object has a unique `&fileID` anchor. The root GameObject has `m_Father: {fileID: 0}` on its Transform.

Key object types:
- `!u!1` — GameObject
- `!u!4` — Transform
- `!u!114` — MonoBehaviour

MonoBehaviour blocks reference their script by GUID (from the `.cs.meta` file):
```yaml
m_Script: {fileID: 11500000, guid: <script-guid>, type: 3}
m_EditorClassIdentifier: AssemblyName::ClassName
```

Serialized fields appear directly under the MonoBehaviour by their C# field name:
```yaml
_variable: 0
```

## Prefab Variant

A prefab variant file contains a single `!u!1001 PrefabInstance` block instead of the full hierarchy. It stores only the delta from the base prefab:

```yaml
--- !u!1001 &<fileID>
PrefabInstance:
  m_Modification:
    m_TransformParent: {fileID: 0}
    m_Modifications:
    - target: {fileID: <source-object-fileID>, guid: <base-prefab-guid>, type: 3}
      propertyPath: _variable
      value: 42
      objectReference: {fileID: 0}
    m_RemovedGameObjects:
    - {fileID: <source-fileID>, guid: <base-prefab-guid>, type: 3}
    m_AddedGameObjects: []
    m_AddedComponents: []
  m_SourcePrefab: {fileID: 100100000, guid: <base-prefab-guid>, type: 3}
```

- `target.fileID` refers to the object's fileID inside the base prefab
- `target.guid` is the GUID from the base prefab's `.meta` file
- `m_RemovedGameObjects` lists GameObjects from the base that are stripped in this variant
- To override a field: add an entry to `m_Modifications` with the correct `propertyPath`

## MCP Workflow (preferred)

When Unity Editor is connected via UnityMCP, use MCP tools instead of hand-writing YAML:

- **Create prefab:** `manage_asset` (action `create`) or `manage_gameobject` + `manage_prefabs` (action `create_from_gameobject`)
- **Instantiate prefab in scene:** `manage_gameobject` (action `create`, param `prefab_path`)
- **Edit prefab fields:** `manage_components` (action `set_property`) targeting the prefab asset
- **Create prefab variant:** `manage_prefabs` (action `create_variant`)

Fall back to the manual YAML workflow below only when MCP is unavailable.

## Manual Workflow

### Create a prefab

1. Write the `.prefab` file with a `%YAML 1.1` header and one block per object (root GameObject + Transform + components). Refer to the Regular Prefab section above for the structure.
2. Create a matching `.prefab.meta` file with a unique GUID:
```yaml
fileFormatVersion: 2
guid: <new-guid>
PrefabImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```
The GUID must be a 32-character lowercase hex string, unique across the project.

### Create a prefab variant

1. Write the `.prefab` file containing only a single `PrefabInstance` block (see Prefab Variant section above).
2. Create a matching `.prefab.meta` file with a unique GUID (same format as above).
3. The `m_SourcePrefab.guid` must match the base prefab's GUID from its `.meta` file.

### Edit a prefab

- To change a serialized field value: find the MonoBehaviour block for that component and edit the field value directly.
- To add a component: add a new block with a unique fileID and add its fileID to the owning `GameObject.m_Component` list.
- To modify a variant override: edit the `value` in the matching `m_Modifications` entry; do not change `target.fileID` or `target.guid`.
- To add a new override to a variant: append a new entry to `m_Modifications` using the target object's fileID from the base prefab and the base prefab's GUID.
