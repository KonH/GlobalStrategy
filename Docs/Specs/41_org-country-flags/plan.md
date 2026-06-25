# Plan: Org and Country Flags — Stage 1: Asset Download

## Spec

Stage 1 covers downloading all PNG flag and org image assets required for the Org and Country Flags feature. For each of the 20 available countries a historical 19th-century flag PNG is fetched from Wikimedia Commons using the Special:FilePath render API and saved to `Assets/Textures/Flags/Countries/<countryId>.png`. For the one available org (Illuminati) a public-domain eye-in-pyramid image is downloaded and saved to `Assets/Textures/Flags/Orgs/Illuminati.png`. Source images may be any size or aspect ratio; Unity will scale them to 64×64 at display time. No Unity Editor changes, C# code changes, or ScriptableObject wiring are included in this stage.

## Goal

Download all 21 historical flag / org PNG assets (20 countries + 1 org) into the correct `Assets/Textures/Flags/` folder structure using a single Python script.

## Approach

A Python script in `.tmp/run.py` holds a hardcoded mapping of `countryId → Wikimedia Commons filename` and `orgId → direct PNG URL`. For country flags the script constructs a Wikimedia Commons render URL of the form:

```
https://commons.wikimedia.org/w/index.php?title=Special:FilePath/<filename>&width=256
```

This endpoint returns a redirect to the actual CDN PNG render. `requests` follows the redirect automatically with `allow_redirects=True`. Files are saved as-is (PNG from the CDN). If the response content-type is not `image/png` the script prints a WARN and skips the file — all mapped files are SVGs rendered server-side to PNG by Wikimedia so the FilePath endpoint returns a PNG render, but the WARN-and-skip guard handles any unexpected response.

For the Illuminati org image a direct PNG URL from Wikimedia Commons is used instead of the FilePath API (the file is a raster PNG, no render step needed).

The script skips files that already exist and reports a summary count at the end. After the run, a verification step confirms all 21 files are present and are valid PNG headers (first 8 bytes = `\x89PNG\r\n\x1a\n`).

Output folders:
```
Assets/Textures/Flags/Countries/   ← 20 files, one per countryId
Assets/Textures/Flags/Orgs/        ← 1 file: Illuminati.png
```

### Country Flag Mapping

Research notes: all flags are era-accurate for the 19th century (roughly 1815–1900). Wikimedia Commons filenames are the actual page title on `commons.wikimedia.org/wiki/File:<name>`. The FilePath API accepts the `File:` prefix or just the filename — the script uses the `File:` prefix form for clarity.

| countryId | Historical period | Wikimedia Commons filename | Confidence |
|---|---|---|---|
| Argentina | Argentine Confederation / Republic, flag adopted 1818 | `File:Flag_of_Argentina_(1818).svg` | High |
| Austria_Hungary | Austro-Hungarian Empire, 1869–1918 civil flag | `File:Civil_Ensign_of_Austria-Hungary_(1869-1918).svg` | High |
| Belgium | Kingdom of Belgium, flag adopted 1831 | `File:Flag_of_Belgium_(civil).svg` | High |
| Egypt | Khedivate of Egypt (Ottoman vassal), 19th century | `File:Flag_of_Egypt_(1882-1922).svg` | High |
| Ethiopia | Ethiopian Empire (Solomonic dynasty), pre-tricolor era | `File:Flag_of_Ethiopia_(1897-1914).svg` | High — note: Lion of Judah flag; for earlier period use `File:Flag_of_Ethiopia_(1897-1914).svg` |
| France | French Republic / Second Empire / Third Republic | `File:Flag_of_France.svg` | High — tricolor unchanged through the period |
| Germany | German Empire (Deutsches Reich), post-1871 | `File:Flag_of_the_German_Empire.svg` | High |
| Imperial_Japan | Empire of Japan, Meiji era (Rising Sun navy flag also in use) | `File:Flag_of_Japan.svg` | High — Hinomaru adopted 1870 as national flag |
| Italy | Kingdom of Italy, post-unification 1861 | `File:Flag_of_Italy_(1861-1946).svg` | High — Savoy coat-of-arms variant |
| Kingdom_of_Brazil | Empire of Brazil, 1870–1889 | `File:Flag_of_Brazil_(1870-1889).svg` | High |
| Manchu_Empire | Qing Dynasty, Yellow Dragon Flag (1862–1912) | `File:Flag_of_the_Qing_Dynasty_(1862-1889).svg` | High |
| Netherlands | Kingdom of the Netherlands, 19th century | `File:Flag_of_the_Netherlands.svg` | High — same tricolor throughout |
| Ottoman_Empire | Ottoman Empire, star-and-crescent flag (post-1844) | `File:Flag_of_the_Ottoman_Empire_(1844-1922).svg` | High |
| Persia | Qajar dynasty, Lion and Sun flag | `File:Flag_of_Persia_(1910).svg` | Medium — 1910 is slightly late; alternative: `File:State_flag_of_Persia_(1806-1925).svg` — use `Flag_of_Persia_(1910).svg` as primary and fall back if 404 |
| Portugal | Kingdom of Portugal, pre-1910 monarchy flag | `File:Flag_of_Portugal_(1830).svg` | Medium — verify filename; alternative `File:Bandeira_de_Portugal_(1830).svg` |
| Russian_Empire | Russian Empire, tricolor civil flag | `File:Flag_of_Russia.svg` | High — imperial white-blue-red tricolor |
| Spain | Kingdom of Spain, 19th century | `File:Flag_of_Spain_(1785-1873,_1875-1931).svg` | High |
| SwedenNorway | United Kingdoms of Sweden and Norway, union flag 1844–1905 | `File:Flag_of_Sweden-Norway.svg` | High |
| United_Kingdom_of_Great_Britain_and_Ireland | United Kingdom post-1801 Acts of Union, Union Jack with St Patrick's Cross | `File:Flag_of_the_United_Kingdom.svg` | High — same design since 1801 |
| United_States_of_America | United States, 19th century (star count varied; 38-star 1877–1890 is mid-period) | `File:US_flag_38_stars.svg` | Medium — 38-star flag for ~1877 era; alternative is `File:Flag_of_the_United_States.svg` (current 50-star) if the historical version 404s |

**Fallback strategy in script:** for any URL that returns a non-PNG response or a file under 1 KB (likely an error page), print a warning and skip rather than saving a corrupt file.

### Org Image Mapping

| orgId | Image description | Source URL |
|---|---|---|
| Illuminati | Eye of Providence / eye-in-pyramid symbol, public domain | `https://upload.wikimedia.org/wikipedia/commons/thumb/a/a9/Eye_of_Providence.svg/256px-Eye_of_Providence.svg.png` |

The Wikimedia Commons thumbnail URL above is a direct PNG render and does not require the FilePath redirect step. It is a well-known public-domain image (the Great Seal eye motif).

## Agent Steps

- [x] **Step 1 — Create permanent download script** — The download script lives at `.claude/download_flags.py` (alongside `generate_image.py` etc.). It holds the full `COUNTRY_FLAGS` and `ORG_FLAGS` mapping tables and supports `--dry-run` and `--force` flags. To add a new country or org, edit the mapping tables at the top of that file and re-run. The script creates output directories automatically via `os.makedirs(..., exist_ok=True)`.

- [ ] **Step 2 — Install dependencies** — Run `python3 -m pip install requests`

- [ ] **Step 3 — Run download script** — Execute: `python3 .claude/download_flags.py`; observe output for any WARN lines; if any country 404s or returns a bad content-type, try the fallback filename by editing `COUNTRY_FLAGS_FALLBACK` in the script and re-running with `--force`.

- [ ] **Step 4 — Verify** — The script prints `Verified X/21 files OK` at the end. Only proceed when it reports 21/21.

- [ ] **Step 5 — Commit assets** — Only proceed if Step 4 confirmed all 21/21 files present and valid. Then stage and commit:
  ```
  git add Assets/Textures/Flags/
  git commit -m "assets: add historical country flags and org images (Stage 1)"
  ```
  If any files are missing, resolve the WARNs from Step 3 before committing.

## User Steps

None for Stage 1 — all steps are fully automated.

## Constitution Check

- **Unity 6 + URP only** — Not applicable; this stage contains no Unity project changes.
- **ECS for all game logic in `src/`** — Not applicable; no game logic is introduced.
- **VContainer is sole DI** — Not applicable; no C# code is written.
- **UI Toolkit only** — Not applicable; no UI changes.
- **Plan before implement** — Satisfied; this document is the plan.
- **Spec before plan for features** — Satisfied; `spec.md` exists at `Docs/Specs/41_org-country-flags/spec.md`.
- **One `.asmdef` per feature folder** — Not applicable; no new assembly or C# files are created.

This plan only produces static PNG asset files and a temporary Python script. It has no impact on any architectural principle.

---

Use /implement to start working on the plan or request changes.
