---
name: unity-e2e-run
description: Drive GlobalStrategy through a live Unity Editor play session via the .e2e file handshake. Use when verifying or debugging a change against the running game. Starting a run needs no confirmation.
---

# Unity E2E run (GlobalStrategy)

Before dropping a request, run the `unity-plugins` skill
(`python scripts/unity/ensure_plugin_dlls.py`) so gitignored
`Assets/Plugins/Core/` DLLs exist, and so Unity MCP is pinned to this
checkout. Another GlobalStrategy Editor will not pick up this `.e2e/`
handshake. Then invoke `cc:unity-e2e-run`. Project specifics:

- Handshake root: `.e2e/` (gitignored). Drop a `RunRequest` at `.e2e/requests/<runId>.json`.
- Committed flows: `Docs/E2E/flows/new_game_to_map.json` and `Docs/E2E/flows/load_save_to_map.json`. Pass `"script": "new_game_to_map"` or `"script": "load_save_to_map"`.
- Scratch scripts: `.e2e/scratch/<name>.json`.
- `protocolVersion` is `1` (`GS.Game.E2E.E2EProtocol.Version`).
- Scenes / screens: `MainMenu`, `CountrySelection`, `Map`. Gallery is not drivable.
- In `waitFor`, prefer those **scene names**. The runner accepts a match on either the active scene or the guessed UI document. Step snapshots use a different `screen` field: the top visible `PanelRenderer` GameObject when it is not named like the scene (e.g. `GameHUD` on Map, `SelectCountryUI` on CountrySelection, `LoadWindowUI` when the load window is open). Do not copy a snapshot `screen` into `waitFor` unless you mean that document.
- The watcher **refuses** the request if any open scene is dirty (`An open scene is dirty; refusing so restoration cannot destroy unsaved work.`). Save or discard before dropping a request.
- Org ids come from `Assets/Configs/organizations.json` (`OrganizationId`). Default org is the first entry; name it in the report when unset.
- Commands are the `src/Game.Commands` set, executed through `src/Game.Commands.Text` the same way as the web terminal.
- Starting a run needs no confirmation.

Example request:

```json
{
  "protocolVersion": 1,
  "runId": "verify-1",
  "script": "new_game_to_map",
  "initiator": "agent"
}
```
