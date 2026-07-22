---
paths:
  - "Assets/**"
---

# Unity WebGL Build Gotchas

## StreamingAssets files are not TextAssets

Files in `Assets/StreamingAssets/` are imported with `DefaultImporter` — they are raw blobs, not `TextAsset` objects. A `[SerializeField] TextAsset` field cannot hold a reference to them.

To expose JSON/text files as `TextAsset` references, place them in a regular folder (e.g. `Assets/Configs/`) and ensure their `.meta` uses `TextScriptImporter`. Same GUID is preserved when moving — just change the importer in the meta file.

## Unicode icons are invisible in WebGL

Unity's bundled WebGL font (LiberationSans) only covers ASCII and basic Latin. Any character outside that range — geometric shapes (▶ ⏸ ▮), math symbols (≡), emoji (🪙) — renders as blank.

Replace with ASCII-safe alternatives, or bundle a Unicode font and apply it via `font-family` in USS / PanelSettings.

**Do not use emoji or Unicode symbol glyphs (▲▼●■ etc.) in UI text at all, even outside WebGL.** For state indicators (expanded/collapsed, on/off), prefer a visual state on the control itself — e.g. toggle the `gs-toggle-on`/`gs-toggle-off` classes on the button for a pressed/unpressed look — over encoding state in the label text. If an icon is genuinely needed, generate a proper image asset (see `.claude/rules/image_generation.md` and `.claude/rules/flag_assets.md`) and reference it via `background-image` in USS, rather than relying on a font glyph.

## Shader stripping: use preloadedAssets, not Shader.Find fallbacks

If `Shader.Find("X")` returns null in a WebGL build, the shader is being stripped. The correct fix is to add the shader's material to **Player Settings → Preloaded Assets**, not to silently fall back to a different shader (which changes rendering behaviour).

Alternatively, assign a material referencing the shader to a scene object in the first scene — Unity then includes it automatically.
