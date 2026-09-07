---
paths:
  - "Assets/**"
---

# Unity MCP Usage

When Unity Editor is connected via UnityMCP, prefer MCP tools over file operations.

## Scripts

- DO NOT use `create_script` for new `.cs` files — just `Write`, put all required content and `refresh_unity`
- To modify existing `.cs` files: DO NOT use `Edit`, just modify file and `refresh_unity`
- `Edit` requires reading the file first (`Read`) before it will accept edits
- Path is project-relative: `Assets/Scripts/[Feature]/ClassName.cs`
- After any script create or edit, call `read_console(types=["error"])` before proceeding — it correctly surfaces compilation errors after `refresh_unity`
- Poll `mcpforunity://editor/state` → `is_compiling` if compilation may still be in progress

## Assembly Definitions

- No MCP tool for `.asmdef` — write via `Write` tool, then call `refresh_unity` to trigger import
- Follow `.claude/rules/unity/asmdef.md` for format

## Prefabs

- Create flow: `manage_gameobject(create)` → `manage_components(add)` → `manage_prefabs(create_from_gameobject, prefab_path=...)`
- `create_from_gameobject` also links the scene instance to the new prefab automatically
- Instantiate in scene: `manage_gameobject(action=create, prefab_path=...)`
- Edit fields: `manage_components(set_property)`
- Create variant: `manage_prefabs(create_variant)`

## Scenes

- Inspect: `manage_scene(get_hierarchy)`
- Add/remove/modify objects: `manage_gameobject`, `manage_components`
- Always save after changes: `manage_scene(save)`
- Scene build registration has no MCP tool — edit `ProjectSettings/EditorBuildSettings.asset` directly

## Targeting GameObjects

- After a domain reload, numeric instance IDs can go stale — prefer targeting by name string
- Use `find_gameobjects` to resolve current instance IDs when needed
- Pay extra attention to setup references - when some prefab instance parts are used in prefab/scene context, use references to instance parts, not prefab parts

## Console Warnings to Ignore

- "Bridge is not running" and "WebSocket is not initialised" are benign — HTTP transport works fine

## Do not self-test in Play mode

Do not hand-roll ad-hoc synthetic input events, and never interrupt an active human play session.

Agent-initiated Play-mode runs **through this project's E2E runner** (see the `unity-e2e-run` skill) are permitted for verification and debugging, with no per-run confirmation. The runner delivers device-level input through the real Input System stack, refuses to start while a session is active, and restores the editor afterwards.

Ad-hoc `manage_editor(action="play")` and hand-dispatched `PointerDownEvent`/`PointerUpEvent` remain discouraged. What's fine and expected as a normal edit-verify step:

- `refresh_unity` + `read_console(types=["error"])` to confirm a change compiles — this is not a Play mode test.
- Adding targeted `Debug.Log` calls at a suspect code path to help diagnose an issue.
- Dropping a `RunRequest` into `.e2e/requests/` and reading `.e2e/runs/<runId>/report.md`.

What to avoid unless explicitly asked, or unless you are driving the E2E runner:

- `manage_editor(action="play")` to reproduce or confirm a bug/fix by hand.
- `execute_code` calls that poke at live GameObjects/UI state to "confirm" behavior, or that construct/dispatch synthetic input events.

After a change that the E2E runner cannot reach, describe what changed and ask the user to try it in Play mode themselves; only pull `read_console` logs afterward once they've triggered the scenario. If the user explicitly asks for Play mode testing or a specific simulated input, that overrides this default for that request only.
