# Plan: Province Occupation

## Spec

Source: `Docs/Specs/26_07_18_19_province-occupation/spec.md`.

**Intent.** Add optional, mutable, persisted province occupation state layered beside province ownership. Every province starts unoccupied at runtime, occupation can be toggled only through a Province-lens debug cheat that uses the current player org's HQ country, and occupied provinces render as the owner's normal fill color plus an additive diagonal hatching overlay in the occupier country's color. Occupation is display/runtime state only: it never changes `ProvinceOwnership.OwnerId` and never affects territory, scoring, control, income, or country aggregates.

**Dependency.** Builds directly on `Docs/Specs/26_07_11_10_province-ownership/spec.md`: reuse the ownership component/system/version pattern, `VisualState.ProvinceOwnership` dirty-state shape, `MapLensApplier`'s fill-color reapply pipeline, and `DebugChangeProvinceOwnerCommand`/`HUDDocument` debug button flow.

**Key acceptance criteria (design targets):**
- New `[Savable]` `ProvinceOccupation` component keyed by `ProvinceId` with empty-string `OccupierId` meaning unoccupied; seeded empty for every province at startup, with no `province_config.json` or generator changes.
- New `ProvinceOccupationSystem` mirrors `ProvinceOwnershipSystem` with `Seed`, `SetOccupier`, `ClearOccupier`, `ToggleOccupier`, `GetOccupier`, and a per-world non-savable version component for `VisualStateConverter` dirty checks.
- New `VisualState.ProvinceOccupation` exposes `OccupierByProvinceId` and raises `PropertyChanged` the same way `ProvinceOwnershipState.Set` does, so `MapLensApplier` can reapply immediately after occupation changes.
- Province rendering remains unchanged when occupier is empty or equals owner; visible occupation is only owner != occupier.
- Occupation hatching appears in Province, Political, Org, and Geographic lenses without changing Province-lens border rendering.
- A single debug button is visible/enabled under the same precondition as the existing reassign-owner cheat (`MapLens.Province` + selected province), pushes a new command through the command accessor, sets the selected province's occupier to the player HQ country when absent/different, and clears it when already occupied by that HQ country.
- Save/load round-trips occupation exactly.
- Existing owner-derived systems (`ProvinceOwnershipSystem.GetProvincesByOwner`, score, control/income, territory aggregation) do not read or change behavior based on occupation.

**Out of scope:** gameplay systems that create occupation, war/conquest mechanics, occupation timers/resolution, multiple occupiers/history, tooltips/panels/notifications, any ownership mutation or ownership cheat change, and province config/generator changes.

## Goal

Implement province occupation as a separate ECS runtime state with visual hatching and one debug toggle, while keeping all ownership semantics and derived owner-based mechanics exactly as they are today.

## Approach

### 1. Occupation components

- **`src/Game.Components/ProvinceOccupation.cs`**:
  ```csharp
  namespace GS.Game.Components {
      [Savable]
      public struct ProvinceOccupation {
          public string ProvinceId;
          public string OccupierId; // "" = unoccupied
      }
  }
  ```
  This mirrors `ProvinceOwnership` but persists runtime changes rather than seed config ownership.

- **`src/Game.Components/ProvinceOccupationVersion.cs`**:
  ```csharp
  namespace GS.Game.Components {
      // Not [Savable] — per-World dirty counter for VisualStateConverter.
      public struct ProvinceOccupationVersion {
          public int Value;
      }
  }
  ```
  Same rationale as `ProvinceOwnershipVersion`: scoped to the `World`, not static, and rebuilt naturally by seed/load mutations.

- **Tests:** update `src/Game.Tests/SavableDiscoveryTests.cs` so `ProvinceOccupation` is expected savable and `ProvinceOccupationVersion` is expected non-savable.

### 2. Occupation system

Add **`src/Game.Systems/ProvinceOccupationSystem.cs`** parallel to `ProvinceOwnershipSystem`:

- `Seed(World world, ProvinceConfig config)` creates one `ProvinceOccupation` entity for every province with `OccupierId = ""`, then bumps the version once.
- `SetOccupier(World world, string provinceId, string occupierId)` mutates only the matching occupation entity. Empty/null `occupierId` clears to `""`; if the value is unchanged, returns `(false, oldOccupierId)` and does not bump the version.
- `ClearOccupier(World world, string provinceId)` delegates to `SetOccupier(..., "")`.
- `ToggleOccupier(World world, string provinceId, string occupierId)` clears when the current occupier equals `occupierId`; otherwise sets it to `occupierId`. Returns `(Changed, OldOccupierId, NewOccupierId)` for command handling and tests.
- `GetOccupier(IReadOnlyWorld world, string provinceId)` returns the occupier id or `""` when missing/unoccupied.
- `GetOccupierByProvinceId(IReadOnlyWorld world)` returns a dictionary containing **only non-empty occupiers**. This avoids treating every province as visually occupied and keeps `MapLensApplier` lookups cheap. `VisualState` consumers must treat a missing key exactly the same as an empty occupier.
- `GetVersion(IReadOnlyWorld world)` and private `BumpVersion(World world)` match the ownership system.

`ProvinceOccupationSystem` must never call `ProvinceOwnershipSystem.ChangeOwner`, never write `ProvinceOwnership`, and never be read by score/control/resource systems.

### 3. Startup and save/load behavior

- **`src/Game.Main/InitSystem.cs`**: call `ProvinceOccupationSystem.Seed(world, provinceConfig)` immediately after `ProvinceOwnershipSystem.Seed(world, provinceConfig)`. This guarantees every new game has an occupation row for every province but no province is occupied by default.
- **Save/load:** no custom serializer code should be required because `SaveSystem`/`LoadSystem` discover `[Savable]` component types by reflection. After load, `VisualStateConverter` must rebuild the visual dictionary from the loaded components. If the non-savable version counter is absent after load, `ProvinceOccupationSystem.GetVersion` returning `0` plus `_lastSeenProvinceOccupationVersion = -1` in the converter is enough for the first post-load conversion to run.

### 4. VisualState and converter

- **`src/Game.Main/VisualState.cs`**: add `ProvinceOccupationState : INotifyPropertyChanged`, mirroring `ProvinceOwnershipState`:
  - `IReadOnlyDictionary<string, string> OccupierByProvinceId { get; private set; }`
  - `RecentProvinceId`, `RecentOldOccupierId`, `RecentNewOccupierId`
  - `Set(...)` stores the dictionary/recent values and raises `PropertyChanged`.
  - Add `public ProvinceOccupationState ProvinceOccupation { get; } = new ProvinceOccupationState();` to `VisualState`.

- **`src/Game.Main/VisualStateConverter.cs`**:
  - Add `_lastSeenProvinceOccupationVersion = -1`.
  - Call `UpdateProvinceOccupation(world)` from `Update(...)` alongside `UpdateProvinceOwnership(world)`.
  - `UpdateProvinceOccupation` checks `ProvinceOccupationSystem.GetVersion(world)`, scans `ProvinceOccupation` components when dirty, builds an occupier dictionary (prefer storing only non-empty occupiers to keep `MapLensApplier` lookup cheap and avoid falsely signaling occupation), and calls `VisualState.ProvinceOccupation.Set(...)`.

- **Immediate command feedback:** when `GameLogic` handles the debug occupation command, update `VisualState.ProvinceOccupation.Set(...)` immediately after a successful mutation, mirroring the ownership command's direct visual-state update. This avoids waiting for the converter's next dirty scan and satisfies the immediate re-render acceptance criterion.

### 5. Command pipeline

- **`src/Game.Commands/DebugToggleProvinceOccupationCommand.cs`**:
  ```csharp
  namespace GS.Game.Commands {
      public struct DebugToggleProvinceOccupationCommand : ICommand {
          public string ProvinceId;
          public string OccupierId;
      }
  }
  ```
  Use `Toggle` in the command name because the only accepted UX is a single debug button that toggles the selected province on/off for the player's HQ country. Avoid a misleading `Set` command whose handler secretly clears state.

- **Generated accessors:** after adding the command file, rebuild so the existing source-generator/partial-command flow produces `ReadDebugToggleProvinceOccupationCommand()` the same way it already produces `ReadDebugChangeProvinceOwnerCommand()`.

- **`src/Game.Main/GameLogic.cs`**: add a loop next to the ownership debug-command loop:
  ```csharp
  foreach (var cmd in _commandAccessor.ReadDebugToggleProvinceOccupationCommand().AsSpan()) {
      var (changed, oldOccupierId, newOccupierId) = ProvinceOccupationSystem.ToggleOccupier(_world, cmd.ProvinceId, cmd.OccupierId);
      if (changed) {
          VisualState.ProvinceOccupation.Set(
              ProvinceOccupationSystem.GetOccupierByProvinceId(_world),
              cmd.ProvinceId,
              oldOccupierId,
              newOccupierId);
      }
  }
  ```
  Do not change the existing `DebugChangeProvinceOwnerCommand` handling.

### 6. Unity map rendering model

Implement hatching as an additive duplicate-mesh child under each province fill object; do not generate free-floating bbox stripes. The previous bbox/CPU-clipping idea is rejected because it is riskier and can leak lines outside irregular province shapes. A duplicate of the already-triangulated province fill mesh guarantees the overlay is clipped to the province silhouette.

- **`Assets/Scripts/Unity/Map/MapMeshBuilder.cs`**:
  - Extend `BuildFeatureMesh` so generated meshes include stable UVs derived from world-space `x/y` (for example `uv = world.xy * hatchUvScale`). Existing fill materials can ignore UVs; the hatch material uses them for a repeated diagonal stripe texture/shader.
  - Keep the current triangulation and border mesh behavior unchanged.

- **Hatch material asset:** prefer an existing transparent URP Unlit/Lit material with a repeatable diagonal stripe texture or shader property. If a new material asset is required, place it with the existing map materials and use a transparent render mode so the owner fill remains visible underneath. The material must expose a color/tint property that `MapLensApplier` can set per occupier.

- **`Assets/Scripts/Unity/Map/ProvinceRenderer.cs`**:
  - Add serialized fields for hatch tuning, e.g. `_occupationHatchMaterialTemplate`, `_occupationHatchUvScale`, and `_occupationHatchZOffset`. If no dedicated material is assigned, fall back to `_materialTemplate` only as a development fallback and log at most once; production/prefab validation should assign the hatch material.
  - During `Render(...)`, after creating the fill renderer and before creating the border child, create an occupation hatch child object named `provinceId + "_OccupationHatch"`. Reuse the same feature mesh instance (or instantiate an identical mesh if material/render ordering requires it), offset the child by a tiny negative/positive local z to avoid z-fighting, assign a cloned hatch material, and start disabled.
  - Keep the existing border object creation exactly as-is and ensure border toggling only affects border children, not the hatch child.

- **Marker components:** add tiny MonoBehaviours under `Assets/Scripts/Unity/Map/`:
  - `ProvinceBorderRendererMarker` on border children.
  - `ProvinceOccupationHatchMarker` on hatch children.
  Then `MapLensApplier` can separately enable borders and hatches without relying on child name suffixes.

### 7. MapLensApplier occupation overlay

- Subscribe/unsubscribe to `_state.ProvinceOccupation.PropertyChanged` in `OnEnable`/`OnDisable`, with a handler that calls `ApplyLens(_state.MapLens.Lens)`.
- In `ApplyLens`:
  - Resolve the owner exactly as today.
  - Resolve `occupierId` from `_state.ProvinceOccupation.OccupierByProvinceId`.
  - Treat missing/empty occupier, undiscovered owner, or `occupierId == ownerId` as no visible occupation.
  - Set fill color exactly as today via `GetColor(lens, ownerId)`.
  - Set hatch renderer enabled only when the province is discovered and visibly occupied.
  - Set hatch material color from the occupier country's political visual color (not org lens color), with alpha tuned high enough to be visible over owner fill.
- Update `SetBorderRenderersEnabled` to affect only `ProvinceBorderRendererMarker` children so occupation hatch children do not accidentally turn on only in Province lens or get disabled/enabled with borders.
- Add a `SetOccupationHatchEnabled(GameObject fillGo, bool enabled, Color color)` helper that finds `ProvinceOccupationHatchMarker` children.

This preserves the current fill-color behavior for every unoccupied province and keeps Province-lens borders unchanged.

### 8. HUD debug button

- **`Assets/Scripts/Unity/UI/HUDDocument.cs`**:
  - Add a field `_btnToggleProvinceOccupation` next to `_btnReassignProvince`.
  - Create a debug-panel button with copy such as `Toggle Occupation by My HQ` near `Reassign Province to My HQ`.
  - Reuse the same visibility logic as `RefreshProvinceCheatButton`: visible only when `MapLens.Province` and `SelectedProvince.IsValid`. Either rename the method to `RefreshProvinceCheatButtons` or update it to manage both buttons.
  - `PushToggleProvinceOccupationCommand` reads `_state.SelectedProvince.ProvinceId` and `_state.PlayerOrganization.HqCountryId`, validates both non-empty, and pushes `DebugToggleProvinceOccupationCommand { ProvinceId = provinceId, OccupierId = hqCountryId }`.
  - Do not directly mutate `VisualState` or ECS from UI.

### 9. Documentation and project metadata

- Add the new C# files to the relevant `.csproj` files if this repository uses explicit compile includes; otherwise rely on SDK-style globbing. Confirm by inspecting `src/Game.Components/Game.Components.csproj`, `src/Game.Systems/Game.Systems.csproj`, and `src/Game.Commands/Game.Commands.csproj` before editing.
- No change to `Assets/Configs/province_config.json`, `scripts/generate_provinces.py`, or province processing code.
- If a visible Unity scene/prefab requires assignment of the new hatch material field, update the relevant prefab/scene according to `.Codex/rules/unity/prefabs.md`/`scenes.md` if those files are restored; otherwise keep serialized fallback to avoid hard scene dependency.

## Plan Review Notes

This revision addresses the plan-review concerns found while re-reading the plan against the current codebase:

- The command is now explicitly named `DebugToggleProvinceOccupationCommand`; the previous `DebugSetProvinceOccupierCommand` name was misleading because the handler intentionally clears occupation on the second click.
- The visual-state dictionary is specified as non-empty occupiers only, removing ambiguity about whether every seeded province should appear in `OccupierByProvinceId`.
- The rendering approach now duplicates the existing province fill mesh with UV-driven hatch material instead of generating bbox stripe geometry. This is simpler, guarantees clipping to province boundaries, and avoids interaction with Province-lens border meshes.

## Steps

### Agent Steps

- [ ] **Add occupation ECS components** — `ProvinceOccupation` and `ProvinceOccupationVersion` in `src/Game.Components/`; update savable-discovery tests.
- [ ] **Add `ProvinceOccupationSystem`** — seed, get, set/clear/toggle, dictionary query, and per-world version dirty counter in `src/Game.Systems/`.
- [ ] **Seed new games** — call `ProvinceOccupationSystem.Seed` from `src/Game.Main/InitSystem.cs` after ownership seeding.
- [ ] **Add visual state** — `ProvinceOccupationState` and `VisualState.ProvinceOccupation` in `src/Game.Main/VisualState.cs`.
- [ ] **Convert occupation to visual state** — add version tracking and `UpdateProvinceOccupation` in `src/Game.Main/VisualStateConverter.cs`.
- [ ] **Add debug command** — `DebugToggleProvinceOccupationCommand` in `src/Game.Commands/` and rebuild generated command accessors.
- [ ] **Handle debug command in game loop** — call `ProvinceOccupationSystem.ToggleOccupier` from `src/Game.Main/GameLogic.cs` and update visual state immediately on success.
- [ ] **Render occupation hatch meshes** — extend `ProvinceRenderer` and add hatch/border marker/helper code under `Assets/Scripts/Unity/Map/`.
- [ ] **Apply hatches per lens** — update `MapLensApplier` to subscribe to occupation changes, set hatch visibility/color, and restrict border toggling to border markers.
- [ ] **Add HUD cheat button** — update `HUDDocument` to show the occupation toggle button under the same Province-lens selection precondition and push the new command.
- [ ] **Add/extend tests** — per the Tests section below.
- [ ] **Build Core DLLs** — run `dotnet build src/GlobalStrategy.Core.sln -c Release` so new ECS/command/component types update `Assets/Plugins/Core/`.
- [ ] **Unity validation if available** — let Unity import/reload, inspect console errors, and take a screenshot of the visible map hatching/debug button if the app can be run in this environment.

### User Steps

### 1. Confirm Unity import and material assignment

After the Core DLL rebuild, let Unity finish domain reload. If a dedicated occupation hatch material field was added and not auto-populated, assign an appropriate transparent URP material in the map prefab/scene; otherwise confirm the fallback material renders acceptably.

### 2. Verify debug toggle in Play mode

Enter Play mode, switch to Province lens, select a province, open the debug menu, and click `Toggle Occupation by My HQ`. Confirm hatching appears in the player's HQ country color without changing the province owner.

### 3. Verify toggle-off and owner-equals-occupier behavior

Click the toggle again and confirm hatching disappears. Also test a province already owned by the HQ country: toggling occupation to the same country should produce no visible hatching even though the stored occupier may momentarily equal the owner until toggled clear.

### 4. Verify lens coverage and borders

Switch between Province, Political, Org, and Geographic lenses. Confirm visible occupation hatching remains additive in all four fill-color lenses, and Province-lens gray borders render exactly as before.

### 5. Verify save/load

Save after setting occupation, reload, and confirm the same province is still occupied and hatches immediately after load.

## Tests

Test project: `src/Game.Tests/` (xUnit, existing project conventions).

- **New `src/Game.Tests/ProvinceOccupationTests.cs`:**
  - `seed_creates_unoccupied_entry_for_each_province` — after init, there is one `ProvinceOccupation` per province and all `OccupierId` values are empty.
  - `set_occupier_changes_runtime_state_without_changing_owner` — set occupation, assert `ProvinceOccupationSystem.GetOccupier` returns the occupier and `ProvinceOwnershipSystem.GetOwner` is unchanged.
  - `setting_same_occupier_is_noop_and_does_not_bump_version` — regression guard matching ownership behavior.
  - `clear_occupier_returns_to_unoccupied` — clear returns empty and bumps version.
  - `toggle_sets_when_absent_and_clears_when_same_occupier` — covers debug-cheat semantics.
  - `debug_command_toggles_occupation_through_game_logic` — push `DebugToggleProvinceOccupationCommand`, run `GameLogic.Update`, assert occupier set; push again, assert cleared.
  - `visual_state_updates_when_occupation_changes` — subscribe/check `VisualState.ProvinceOccupation` after command handling and verify `RecentProvinceId`, old occupier, new occupier, and dictionary contents.
  - `occupation_round_trips_through_save_load` — set occupier, save, reload into a new logic instance, assert occupier restored.
  - `owner_aggregates_ignore_occupation` — set occupation to a different country, assert `ProvinceOwnershipSystem.GetProvincesByOwner` and relevant existing score/control assertions still follow owner only.

- **Extend map/HUD edit-mode tests if present; otherwise add lightweight Unity tests where the current test setup supports it:**
  - `map_lens_applier_enables_hatch_only_for_different_occupier` — construct a province object with fill, hatch marker, and border marker; owner A/occupier B enables hatch, owner A/occupier A disables hatch, empty occupier disables hatch.
  - `province_border_toggle_ignores_hatch_renderer` — Province lens border toggling affects only `ProvinceBorderRendererMarker`, not `ProvinceOccupationHatchMarker`.
  - `hud_occupation_button_uses_same_visibility_as_reassign_button` — Province lens + selected province shows both buttons; no selected province hides both.

- **Extend `src/Game.Tests/SavableDiscoveryTests.cs`:** ensure `ProvinceOccupation` is savable and `ProvinceOccupationVersion` is not.

Run:
- `dotnet test src/GlobalStrategy.Core.sln`
- `dotnet build src/GlobalStrategy.Core.sln -c Release`

If Unity CLI or MCP is available, also run the project's Unity edit-mode tests/import validation and inspect console errors after the DLL rebuild.

## Constitution Check

Checked against `Docs/Constitution.md`.

**No conflicts found.**

- *Unity 6 + URP only.* The visual layer uses Unity mesh renderers/materials in the existing URP map path; no Built-in RP shaders, camera-stack changes, or render-pipeline migration.
- *ECS for all game logic in `src/`.* Occupation state, mutation, persistence, command handling, and save/load behavior are ECS/component/system work under `src/`. Unity MonoBehaviours only present state and push commands.
- *VContainer sole DI.* No new service locator/static singleton; Unity-side consumers continue receiving `VisualState` and command accessors through existing VContainer wiring.
- *UI Toolkit only.* The debug cheat is an additional UI Toolkit button in the existing `HUDDocument` debug panel; no Canvas/UGUI.
- *Planning before implement.* This plan lives alongside its spec at `Docs/Specs/26_07_18_19_province-occupation/plan.md` before implementation.
- *Spec before plan for feature work.* The plan follows `Docs/Specs/26_07_18_19_province-occupation/spec.md`.
- *File organisation.* Uses `Docs/Specs/<YY_MM_DD_HH>_<name>/plan.md`, not legacy `Docs/Plans/`.
- *Assembly structure.* New Unity scripts remain in existing feature folders under `Assets/Scripts/Unity/Map/` and `Assets/Scripts/Unity/UI/`; no new cross-folder assembly is introduced.
- *C# style.* Implementation should follow tabs, braces always, `_`-prefixed private fields, and no redundant access modifiers, matching existing project conventions.

Use /implement to start working on the plan or request changes.
