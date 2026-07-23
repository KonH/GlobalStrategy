---
name: flag-assets
description: Add or troubleshoot country flag and org image assets (Assets/Textures/Flags/Countries and Orgs) sourced from Wikimedia Commons, including the download_flags.py/check_flags.py/svg_to_png.py scripts and CountryVisualConfig/OrgVisualConfig wiring. Load when adding a new country flag or org image.
---

# Flag and Org Image Assets

Flag PNGs live in `Assets/Textures/Flags/Countries/<countryId>.png` and org images in `Assets/Textures/Flags/Orgs/<orgId>.png`. All assets are sourced from Wikimedia Commons and downloaded as server-side PNG renders — no local SVG rendering required for download.

## Scripts

All scripts live in `scripts/utils/` and must be run from the **project root** so relative paths resolve correctly.

| Script | Purpose |
|---|---|
| `download_flags.py` | Download all mapped flags/org images; skips existing files |
| `check_flags.py` | Diagnose a Wikimedia filename — prints resolved URL and MIME type |
| `svg_to_png.py` | Convert any SVG (file path or URL) to PNG locally |

### Dependencies

```powershell
.venv\Scripts\pip.exe install requests svglib reportlab
```

`svg_to_png.py` also supports `cairosvg` (better quality) if the Cairo C library is available, but falls back to `svglib+reportlab` automatically.

## Adding a new country flag

1. Find the era-accurate flag on Wikimedia Commons (`commons.wikimedia.org/wiki/File:…`). Copy the full page title including `File:`.

2. Add an entry to `COUNTRY_FLAGS` in `scripts/utils/download_flags.py`:
   ```python
   "NewCountryId": "File:Flag_of_New_Country_(1850).svg",
   ```
   If the filename is uncertain, also add a fallback to `COUNTRY_FLAGS_FALLBACK`:
   ```python
   "NewCountryId": "File:Alternative_Flag_Name.svg",
   ```

3. If the filename is uncertain, verify it resolves before downloading:
   ```powershell
   .venv\Scripts\python.exe scripts\utils\check_flags.py "File:Flag_of_New_Country_(1850).svg"
   ```
   A `NOT FOUND` result means the filename is wrong — search Wikimedia Commons and try again.

4. Download (skips files that already exist):
   ```powershell
   .venv\Scripts\python.exe scripts\utils\download_flags.py
   ```
   Or force re-download of all files:
   ```powershell
   .venv\Scripts\python.exe scripts\utils\download_flags.py --force
   ```

5. Confirm the script reports `Verified N/N files OK`.

6. Import the PNG as a Sprite in Unity via MCP:
   ```
   manage_texture(action="set_import_settings", path="Textures/Flags/Countries/<countryId>.png", as_sprite=true)
   ```

7. Get the asset GUID via MCP:
   ```
   manage_asset(action="get_info", path="Textures/Flags/Countries/<countryId>.png")
   ```
   Note the `guid` from the result.

8. Find the index of the country in `CountryVisualConfig.asset` (its position in the `Entries` list, 0-based) by reading the asset file, then assign the flag via MCP:
   ```
   manage_scriptable_object(action="modify",
     target={"path": "Assets/Configs/CountryVisualConfig.asset"},
     patches=[{"path": "Entries.Array.data[<index>].flag", "value": {"guid": "<guid>"}}])
   ```

9. Commit: `git add Assets/Textures/Flags/` and commit with the assets.

## Adding a new org image

Same process as a country flag, but add to `ORG_FLAGS` instead:

```python
ORG_FLAGS = {
    "Illuminati": "File:Eye_of_Providence.svg",
    "NewOrg":     "File:Some_Symbol.svg",
}
```

The output goes to `Assets/Textures/Flags/Orgs/<orgId>.png`.

After downloading, follow the same Unity steps (6–8 above) but target `Textures/Flags/Orgs/<orgId>.png` and `Assets/Configs/OrgVisualConfig.asset` with property path `Entries.Array.data[<index>].flag`.

## Troubleshooting

**`WARN: could not resolve URL`** — the Wikimedia filename does not exist. Run `check_flags.py` with the exact filename to confirm, then find the correct title on Commons.

**`WARN: response is not a PNG`** — the URL resolved but Wikimedia returned something other than a PNG (rare; usually a temporary server error). Re-run with `--force`.

**`WARN: unexpected Content-Type`** — the file may be a raster PNG on Commons that doesn't go through the SVG render pipeline. In that case, find the direct PNG URL and download it manually via `svg_to_png.py` or `requests`.

## Converting a standalone SVG to PNG

```powershell
.venv\Scripts\python.exe scripts\utils\svg_to_png.py path\to\file.svg output.png --width 256
.venv\Scripts\python.exe scripts\utils\svg_to_png.py https://example.com/flag.svg output.png
```
