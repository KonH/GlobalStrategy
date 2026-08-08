---
paths:
  - "Assets/**"
---

# Unity WebGL Build Gotchas

## Saves need `autoSyncPersistentDataPath`

`PersistentStorage` writes save JSON via `File.WriteAllText` under `Application.persistentDataPath`. On WebGL that path is Emscripten's MEMFS backed by IndexedDB — writes stay in memory until the loader syncs them.

Built-in Unity templates leave `autoSyncPersistentDataPath` unset, so a successful in-session save disappears after refresh. This project uses `Assets/WebGLTemplates/Minimal/index.html` (`PROJECT:Minimal`) with `autoSyncPersistentDataPath: true` so every write under `persistentDataPath` is flushed to IndexedDB.

If you switch templates or host the build behind a custom `createUnityInstance` page (e.g. Unity Play embed), keep that flag set (or call `JS_FileSystem_Sync()` after writes). IndexedDB is also per-origin and keyed by `companyName` + `productName` — changing either, or opening a different host/port, looks like "no saves."

`autoSyncPersistentDataPath` alone was not reliable enough in practice (saves still went missing after reload, notably on the Unity Play–hosted build). `PersistentStorage.Write`/`Delete` (`Assets/Scripts/Unity/Save/PersistentStorage.cs`) now also call an explicit `FS.syncfs` flush right after each write/delete via `Assets/Plugins/WebGL/PersistentStorageSync.jslib`, instead of relying solely on the auto path's internal timing. This also surfaces a failure as a `console.error` in the browser, which the auto path does silently. If saves are still missing after this, check the browser's DevTools console for that error first — a `FS.syncfs` error there points at IndexedDB itself being blocked or partitioned (e.g. third-party/iframe storage restrictions from the embedding host), which no amount of sync-timing fixes will solve; a `PlayerPrefs`-based storage backend would hit the same IndexedDB restriction on WebGL, since it uses the same browser storage under the hood.

`autoSyncPersistentDataPath` does not change the player payload download. A noticeably slower first load should be investigated separately from the IndexedDB sync. In particular, keep `webGLCompressionFormat` set to Brotli in both `ProjectSettings/ProjectSettings.asset` and the serialized PlayerSettings snapshot in `Assets/Settings/Build Profiles/Web - Desktop - Release.asset`. CI builds use that build profile, and an uncompressed profile makes the large `.data` and `.wasm` payloads substantially more expensive to download.

## StreamingAssets files are not TextAssets

Files in `Assets/StreamingAssets/` are imported with `DefaultImporter` — they are raw blobs, not `TextAsset` objects. A `[SerializeField] TextAsset` field cannot hold a reference to them.

To expose JSON/text files as `TextAsset` references, place them in a regular folder (e.g. `Assets/Configs/`) and ensure their `.meta` uses `TextScriptImporter`. Same GUID is preserved when moving — just change the importer in the meta file.

## Unicode icons are invisible in WebGL

Unity's bundled WebGL font (LiberationSans) only covers ASCII and basic Latin. Any character outside that range — geometric shapes (▶ ⏸ ▮), math symbols (≡), emoji (🪙) — renders as blank.

Replace with ASCII-safe alternatives, or bundle a Unicode font and apply it via `font-family` in USS / PanelSettings.

**Do not use emoji or Unicode symbol glyphs (▲▼●■ etc.) in UI text at all, even outside WebGL.** For state indicators (expanded/collapsed, on/off), prefer a visual state on the control itself — e.g. toggle the `gs-toggle-on`/`gs-toggle-off` classes on the button for a pressed/unpressed look — over encoding state in the label text. If an icon is genuinely needed, generate a proper image asset (see `.claude/rules/image_generation.md` and `.claude/rules/flag_assets.md`) and reference it via `background-image` in USS, rather than relying on a font glyph.

## Decorative SDF fonts lack Cyrillic glyphs — RU text renders as tofu

`Assets/UI/Fonts/Cinzel-*` and `IMFellEnglish-*` are Latin-only Google Fonts — their source `.ttf` files contain zero Cyrillic codepoints (verified via cmap inspection), so any text styled with those SDF font assets renders as tofu in the `ru` locale, in every build (not just WebGL). `PlayfairDisplay-*` is the only bundled family with Cyrillic coverage.

Each `TMP_FontAsset`/`FontAsset` `.asset` file has an `m_FallbackFontAssetTable` field — a plain YAML list — that TMP consults at runtime when the primary font lacks a requested glyph. Since these fonts use `m_AtlasPopulationMode: 1` (Dynamic, not DynamicOS) and their source `.ttf` import settings have `includeFontData: 1`, missing glyphs are rasterized on demand from the fallback's source font at runtime, including in WebGL builds — no Editor/Font Asset Creator re-bake is required. Wire a Cyrillic-capable fallback (e.g. the matching-weight `PlayfairDisplay-* SDF` asset) into `m_FallbackFontAssetTable` for any Latin-only font asset used where localized text can appear:

```yaml
m_FallbackFontAssetTable:
- {fileID: 11400000, guid: <guid-of-fallback-.asset>, type: 2}
```

This is a visual/typography change (RU text falls back to a different font family than the EN headline font) — always ask the owner to confirm the look in-Editor or in a build after making this kind of change.

## Shader stripping: use preloadedAssets, not Shader.Find fallbacks

If `Shader.Find("X")` returns null in a WebGL build, the shader is being stripped. The correct fix is to add the shader's material to **Player Settings → Preloaded Assets**, not to silently fall back to a different shader (which changes rendering behaviour).

Alternatively, assign a material referencing the shader to a scene object in the first scene — Unity then includes it automatically.
