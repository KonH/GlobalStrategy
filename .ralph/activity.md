# Ralph Activity Journal

Append-only log written by each loop iteration. Newest entry last.

---

## 2026-07-15 — Add OwnerType.Province enum case

Task: `components` — Add OwnerType.Province enum case.

Change: Added `Province` as a fourth case (after `Character`) to `src/Game.Components/OwnerType.cs`.

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` — Build succeeded, 0 Warning(s), 0 Error(s).

Notes for next iteration: Next task is "Add ProvinceEntry.Population field" in `src/Game.Configs/ProvinceConfig.cs` (add `public double Population { get; set; }`). No blockers encountered.

---

## 2026-07-15 — Add ProvinceEntry.Population field

Task: `config` — Add ProvinceEntry.Population field.

Change: Added `public double Population { get; set; }` to `ProvinceEntry` in `src/Game.Configs/ProvinceConfig.cs`.

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` — Build succeeded, 0 Warning(s), 0 Error(s).

Notes for next iteration: Next task is "Pass population through Stage 2 ProvinceProcessor" in `src/Game.Configs.Loader/ProvinceProcessor.cs` — add a `GetDoubleProp` helper mirroring `GetStringProp` (return 0.0 if absent), read the new `population` GeoJSON property per feature, and set it on the constructed `ProvinceEntry`. Gate is `dotnet test src/GlobalStrategy.Core.sln`. No blockers encountered.

---

## 2026-07-15 — Pass population through Stage 2 ProvinceProcessor

Task: `config-loader` — Pass population through Stage 2 ProvinceProcessor.

Change: In `src/Game.Configs.Loader/ProvinceProcessor.cs`, added a `static double GetDoubleProp(JsonNode? props, string key)` helper mirroring `GetStringProp` (returns `0.0` when `props` is null or the key is absent). `Process` now reads `population` from each feature's properties via `GetDoubleProp` and sets it on the constructed `ProvinceEntry`. `countryId` cross-validation logic is unchanged.

Gotcha for future iterations: this file (and possibly other `src/` files) has CRLF line endings. The `Edit` tool's exact-string matching failed repeatedly against multi-line blocks that included the file's real `\r\n` endings (single-line matches worked, blocks spanning closing braces did not) — a PowerShell regex-replace attempt to patch around it also went wrong (mangled tabs/backticks into literal `t`/`n` characters). What worked: `Read` the file, then `Write` the whole corrected file back out. Prefer `Write` (full-file rewrite) over `Edit` for CRLF files in `src/` if `Edit` reports "String to replace not found" despite the text visually matching in `Read` output.

Also discovered: this machine only has the .NET 10 runtime installed (`dotnet --list-runtimes` shows only `10.0.9`), but `Game.Tests`/`ECS.Tests`/`ECS.Viewer.Tests` target `net8.0`. Plain `dotnet test src/GlobalStrategy.Core.sln` fails immediately with "You must install or update .NET to run this application." Workaround: prefix the gate command with `DOTNET_ROLL_FORWARD=LatestMajor`, e.g. `DOTNET_ROLL_FORWARD=LatestMajor dotnet test src/GlobalStrategy.Core.sln`. Future iterations running the `dotnet test` gate should use this env var.

Gate: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test src/GlobalStrategy.Core.sln` — Passed! 34 (ECS.Tests) + 16 (ECS.Viewer.Tests) + 126 (Game.Tests) = 176 total, 0 failures, 0 skipped.

Notes for next iteration: Next task is "Add region lookup and density ranges to generate_provinces.py" — add `COUNTRY_REGION` and `REGION_DENSITY_RANGES` dicts near `PER_COUNTRY_DENSITY_MULTIPLIER` in `scripts/generate_provinces.py`. Gate is `.venv\Scripts\python.exe -m py_compile scripts\generate_provinces.py`. No blockers encountered on this task; remember the CRLF/Edit-tool and DOTNET_ROLL_FORWARD gotchas above.

---

## 2026-07-15 — Add region lookup and density ranges to generate_provinces.py

Task: `pipeline` — Add region lookup and density ranges to generate_provinces.py.

Change: In `scripts/generate_provinces.py`, added `COUNTRY_REGION` (dict, `countryId` -> region key) and `REGION_DENSITY_RANGES` (dict, region -> `(min_people_per_km2, max_people_per_km2)`) near `PER_COUNTRY_DENSITY_MULTIPLIER`. `COUNTRY_REGION` explicitly maps all 154 countryIds currently in `Assets/Configs/country_config.json` (verified via a one-off Python check that `ids - keys` and `keys - ids` are both empty) into 16 regions: WesternEurope, SouthernEurope, NorthernEurope, EasternEurope, SouthAsia, EastAsia, SoutheastAsia, MiddleEast, CentralAsia, NorthAfrica, SubSaharanAfrica, NorthAmerica, CentralAmericaCaribbean, SouthAmerica, Oceania, plus a "Default" fallback (unused today since every current country has an explicit mapping, but required by the task spec for future new countries). Density bands are approximate/invented per the task's guidance (not researched real 1880 data), skewed denser for SouthAsia/EastAsia/WesternEurope and sparser for NorthernEurope/CentralAsia/interior-desert regions, per spec intent.

No other code in the file was touched — `try_option_c`, `run()`, and the rest of the pipeline are unchanged; the actual sampling/attachment of density to provinces is the next task.

Gate: `.venv/Scripts/python.exe -m py_compile scripts/generate_provinces.py` — compiled cleanly (bash tool required forward slashes; backslash path via Bash tool failed with "command not found" since bash swallows the backslashes — use forward slashes for this gate when invoking through the Bash tool, or PowerShell for the literal backslash form).

Gotcha for next iteration: the `Edit` tool failed to match multi-line replacement blocks in `.ralph/prd.md` itself despite visually-identical `Read` output — this file uses tabs for indentation and Edit's exact-string matching over multiple lines was finicky. Wrote a small inline Python script (find the task's unique description marker, then flip the next `"passes": false` after it to `true`) instead of `Edit`/`Write` for this flag-flip; worked reliably and is a good fallback pattern for future prd.md edits.

Notes for next iteration: Next task is "Sample density and attach population per province in generate_provinces.py" — create one seeded `random.Random` per country in `run()`'s loop (reusing `deterministic_seed`), thread it into `try_option_c` (removing its internal `rng = random.Random(...)` line), then after `assign_province_ids` look up `COUNTRY_REGION`/`REGION_DENSITY_RANGES`, draw `density = rng.uniform(*density_range)` per province and stash as `prov['_density']`, and add a `'population': None` placeholder to each feature's properties dict (final population values come from the geometry-based task after this one). Gate is the same py_compile command. No blockers.

---

## 2026-07-15 — Sample density and attach population per province in generate_provinces.py

Task: `pipeline` — Sample density and attach population per province in generate_provinces.py.

Change: In `scripts/generate_provinces.py`:
- Added top-level `import random` (was previously imported locally inside `try_option_c`).
- `run()`'s per-country loop now creates `rng = random.Random(deterministic_seed(country_id))` up front, before the Micro/OptionA/OptionC branch.
- `try_option_c` signature now takes `rng` as a parameter instead of constructing its own; removed its internal `import random` / `rng = random.Random(...)` lines. Call site in `run()` updated to pass `rng`.
- After `assign_province_ids` runs and the per-country `provinces` list is finalized, look up `region = COUNTRY_REGION.get(country_id, "Default")` and `density_range = REGION_DENSITY_RANGES.get(region, REGION_DENSITY_RANGES["Default"])`, then for each `prov` in list order set `prov["_density"] = rng.uniform(*density_range)`.
- Each emitted feature's `properties` dict now includes `"population": None` alongside `provinceId`/`countryId`/`displayName`/`generationMethod`/`compassKey`.
- Updated the module docstring's Output property list to mention `population`.

Gate: `.venv/Scripts/python.exe -m py_compile scripts/generate_provinces.py` — compiled cleanly.

Notes for next iteration: Next task is "Compute final population from simplified geometry after mapshaper pass" — after the `npx mapshaper -simplify keep-shapes <pct>%` subprocess call succeeds, reload `INTERMEDIATE_PATH` from disk, recompute each feature's area in `EQUAL_AREA_CRS` from the simplified geometry, multiply by the matching province's stashed `_density` (matched by `provinceId` — note `_density` currently lives only on the in-memory `prov` dicts from `run()`, not on the serialized feature, so the reload step will need to build a `provinceId -> density` lookup from the in-memory `all_features`/`provinces` data before reloading, or otherwise carry `_density` through to match against reloaded features), and write the result into that feature's `population` property, then re-serialize `INTERMEDIATE_PATH`. Gate is the same py_compile command. No blockers on this task.

---

## 2026-07-15 — Compute final population from simplified geometry after mapshaper pass

Task: `pipeline` — Compute final population from simplified geometry after mapshaper pass.

Change: In `scripts/generate_provinces.py`'s `run()`:
- Added a `density_by_province_id = {}` dict initialized alongside `all_features`, populated (`density_by_province_id[prov["provinceId"]] = prov["_density"]`) in the same loop where `prov["_density"]` is first set — this survives the mapshaper round-trip since it's independent of the serialized feature.
- After the `npx mapshaper -simplify keep-shapes <pct>%` subprocess call succeeds, reload `INTERMEDIATE_PATH` from disk into `simplified_collection`/`simplified_features`.
- Built `geometries = [shape(feature["geometry"]) for feature in simplified_features]` and computed `areas_km2` via `gpd.GeoSeries(geometries, crs=WGS84_CRS).to_crs(EQUAL_AREA_CRS).area / 1_000_000.0` (same technique used elsewhere in the file, e.g. line 415).
- For each `(feature, area_km2)` pair, looked up `density_by_province_id[province_id]` and set `feature["properties"]["population"] = area_km2 * density`.
- Re-serialized `simplified_collection` back to `INTERMEDIATE_PATH`.
- Reassigned `all_features = simplified_features` so the downstream `update_province_locales(all_features)` call and the final summary's `len(all_features)` operate on the same post-mapshaper feature list whose `population` is now populated (previously `all_features` still pointed at the pre-simplify list with `population: None`).

Gate: `.venv/Scripts/python.exe -m py_compile scripts/generate_provinces.py` — compiled cleanly.

Gotcha reconfirmed: `Edit` against `.ralph/prd.md` failed again with "String to replace not found" despite the text visually matching in `Read` output (tabs/CRLF). Used the same inline-Python fallback pattern as the prior iteration (locate the task's description marker, then flip the next `"passes": false` occurrence after it to `true`) — reliable, keep using it for prd.md flag flips.

Notes for next iteration: Next task is "Add GameSettings.PopulationGrowthPercentPerMonth global constant" — add `public double PopulationGrowthPercentPerMonth { get; set; } = 0.075;` to `src/Game.Configs/GameSettings.cs` alongside `StartYear`/`SpeedMultipliers`/`DefaultLocale`/`AutoSaveInterval`, and add `"populationGrowthPercentPerMonth": 0.075` to `Assets/Configs/game_settings.json`. Gate is `dotnet build src/GlobalStrategy.Core.sln -c Release`. No blockers on this task. Note: this script change was not actually re-run against real geometry yet (that happens in the later "Re-run the province generation pipeline with real geometry" task) — only compiled for syntax.
