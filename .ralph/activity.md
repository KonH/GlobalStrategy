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
