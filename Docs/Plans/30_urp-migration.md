# 30 — URP Migration (Built-in → Universal Render Pipeline)

## Goal

Migrate the project from Unity's Built-in Render Pipeline to the Universal Render Pipeline (URP). The game is a 2D-ish grand strategy title with a flat map, no custom shaders, no post-processing, and a fully UI Toolkit–based UI. The scope is narrow: install URP, generate the required assets, convert all materials, verify nothing is pink or broken in play mode, and confirm the WebGL build path remains sound.

## Approach

Because the project uses only Unity built-in shaders and no custom ShaderGraph or GLSL, Unity's automated **Render Pipeline Converter** can handle all material upgrades. Manual steps are limited to asset creation, settings assignment, and post-conversion verification.

---

## Section 1: Agent Steps

Steps that Claude/AI can perform autonomously via file edits and MCP tools — no Unity Editor UI interaction required.

- [ ] **Install the URP package** — Add `"com.unity.render-pipelines.universal": "17.x.x"` to `Packages/manifest.json`, refresh Unity, and confirm compilation completes with no errors.
- [ ] **Create URP Pipeline Asset files** — Write `Assets/Settings/UniversalRenderPipelineAsset.asset` and `UniversalRenderPipelineAsset_Renderer.asset` at defaults; the Forward renderer suits this project's flat map geometry.
- [ ] **Assign the pipeline in GraphicsSettings.asset** — Set `m_CustomRenderPipeline` in `ProjectSettings/GraphicsSettings.asset` to reference the new pipeline asset.
- [ ] **Assign the pipeline in QualitySettings.asset** — Set `customRenderPipeline` in every quality level in `ProjectSettings/QualitySettings.asset` to the same asset, covering all tiers including WebGL.
- [ ] **Clean up stale m_AlwaysIncludedShaders entries** — Grep `ProjectSettings/GraphicsSettings.asset` for `m_AlwaysIncludedShaders`, remove stale Built-in shader references, and replace with URP equivalents where still needed.
- [ ] **Grep scripts for Shader.Find calls** — Search `Assets/Scripts/` for `Shader.Find`; any hits must be resolved via Preloaded Assets (WebGL rule) before the build step.

---

## Section 2: User Steps

Steps that require manual Unity Editor interaction — visual inspection, wizard-driven tools, or Editor UI forms.

### 7. Run the Render Pipeline Converter

Open **Window → Rendering → Render Pipeline Converter**. Select the **Built-in to URP** conversion type. Enable at minimum:
- **Rendering Settings**
- **Material Upgrade**

Click **Initialize Converters**, review the list for warnings, then click **Convert**. This upgrades all materials in `Assets/` in place.

### 8. Visually inspect for pink/magenta materials

In the Scene and Game views, check for any pink or magenta objects indicating unconverted materials. Open each affected material and manually reassign its shader to the appropriate URP equivalent (`Universal Render Pipeline/Lit` or `Universal Render Pipeline/Unlit`).

### 9. Check Preloaded Assets in Player Settings

Open **Edit → Project Settings → Player → Other Settings → Preloaded Assets**. Verify that any previously listed materials or shaders are still present and now reference URP shaders rather than Built-in ones. Update entries as needed.

### 10. Trigger a WebGL development build and check the build log

Run a local Development Build targeting WebGL. Review the build log for shader stripping warnings. Confirm no `Shader 'X' not found` errors appear at runtime.

### 11. Full play-mode smoke test

Enter Play mode and verify:
- Map loads and all country regions render with correct flat colours.
- Selecting a country highlights it correctly.
- All UI Toolkit panels (HUD, overlay, modal layers, card play flow) display without regressions.
- Time passes, resources tick, no console errors.

---

Use /implement to start working on the plan or request changes.
