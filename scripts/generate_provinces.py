"""
Province-geometry generator for GlobalStrategy (Python stage of the province-division pipeline).

Usage (run from project root):
    .venv\\Scripts\\python.exe scripts\\generate_provinces.py [--force-download]

Options:
    --force-download   Re-download Natural Earth datasets even if already cached

Dependencies (install into .venv):
    .venv\\Scripts\\pip.exe install geopandas shapely scipy pyproj requests
    Node.js/npx must be on PATH — this script shells out to `npx mapshaper` for the
    final simplify pass (see .claude/rules/unity/province_config_generator.md).

Pipeline summary:
    1. Reconstruct each 1880 country's unioned polygon from Assets/Configs/country_config.json
       (mainMapFeatureIds/secondaryMapFeatureIds, colonial-parent merges already applied) and the
       raw feature geometries in Assets/Map/world_1880.json. The mapFeatureId for each raw feature
       is recomputed locally using the same NAME-priority + ASCII-normalize + slug algorithm as
       Program.cs's ToMapFeatureId, so no round-trip through geojson_world.json/map_entry_config.json
       is required — the raw basemap is the single source of truth for geometry.
    2. Reproject to EPSG:6933 (equal-area) purely for km^2 math; all emitted geometry stays WGS84.
    3. Download & cache (skip if present) Natural Earth ne_10m_admin_1_states_provinces and
       ne_10m_populated_places into .tmp/naturalearth/ (gitignored).
    4. Per country: Micro-state rule (< 3,000 km^2), else Option A (admin-1 overlay + fit-quality
       gate), else Option C (deterministic Voronoi fallback + nearest-settlement naming).
    5. Assign deterministic provinceId = "{countryId}__{slug(name)}" with numeric collision suffixes.
    6. Serialize combined output to .tmp/provinces_intermediate.geojson (gitignored intermediate,
       consumed by the C# Game.Configs.Loader stage — not committed).
    7. Run `npx mapshaper -i <file> -simplify keep-shapes <pct>% -o <file>` to reduce vertex count.

Output:
    .tmp/provinces_intermediate.geojson — FeatureCollection with properties:
        provinceId, countryId, displayName, generationMethod ("Micro" | "OptionA" | "OptionC")

See Docs/Specs/43_province-division/plan.md for the full design and
.claude/rules/unity/province_config_generator.md for the rule-doc summary.
"""

import hashlib
import json
import math
import os
import re
import subprocess
import sys
import unicodedata
import zipfile

import requests
from shapely.geometry import shape, mapping, Point
from shapely.ops import unary_union
from shapely.validation import make_valid
import geopandas as gpd
import numpy as np
from scipy.spatial import Voronoi

# ---------------------------------------------------------------------------
# Config / constants
# ---------------------------------------------------------------------------
COUNTRY_CONFIG_PATH = "Assets/Configs/country_config.json"
WORLD_GEOJSON_PATH = "Assets/Map/world_1880.json"
CACHE_DIR = ".tmp/naturalearth"
INTERMEDIATE_PATH = ".tmp/provinces_intermediate.geojson"
EN_LOCALE_PATH = "Assets/Localization/en.asset"
RU_LOCALE_PATH = "Assets/Localization/ru.asset"
PROVINCE_LOCALE_KEY_PREFIX = "province_name."

EQUAL_AREA_CRS = "EPSG:6933"
WGS84_CRS = "EPSG:4326"

MICRO_STATE_AREA_KM2 = 3_000.0
OPTION_A_MIN_PIECES = 2
OPTION_A_MAX_PIECES = 300
SLIVER_AREA_RATIO = 0.005      # <0.5% of country area counts as a sliver
SLIVER_MAX_FRACTION = 0.30     # <=30% of pieces may be slivers
OPTION_C_MIN_SEEDS = 3
OPTION_C_MAX_SEEDS = 150
OPTION_C_AREA_PER_SEED_KM2 = 10_000.0

# Per-country density multiplier for Option C's seed count — scales both the
# computed seed count and the global OPTION_C_MAX_SEEDS cap for that country.
# Use for areas that need finer detail than the global density constants give
# (e.g. archipelagos that collapse into one Voronoi cell).
PER_COUNTRY_DENSITY_MULTIPLIER = {
    "United_Kingdom_of_Great_Britain_and_Ireland": 5.0,
}
MAPSHAPER_SIMPLIFY_PCT = 10  # tunable: percentage of vertices kept

# countryId -> region key, used to look up REGION_DENSITY_RANGES for per-province
# population sampling. Approximate 1880-era relative groupings, not researched
# real data. Countries not listed fall back to "Default".
COUNTRY_REGION = {
    # Western / Central Europe — dense
    "Austria_Hungary": "WesternEurope",
    "Belgium": "WesternEurope",
    "Denmark": "WesternEurope",
    "France": "WesternEurope",
    "Germany": "WesternEurope",
    "Italy": "WesternEurope",
    "Luxembourg": "WesternEurope",
    "Malta": "WesternEurope",
    "Netherlands": "WesternEurope",
    "Portugal": "WesternEurope",
    "Spain": "WesternEurope",
    "Switzerland": "WesternEurope",
    "United_Kingdom_of_Great_Britain_and_Ireland": "WesternEurope",
    # Northern Europe — sparser
    "SwedenNorway": "NorthernEurope",
    # Eastern Europe / Balkans
    "Bosnia_Herzegovina": "EasternEurope",
    "Bulgaria": "EasternEurope",
    "Greece": "EasternEurope",
    "Montenegro": "EasternEurope",
    "Romania": "EasternEurope",
    "Serbia": "EasternEurope",
    # Russian Empire — vast and sparse
    "Russian_Empire": "Russia",
    # South Asia — dense
    "Afghanistan": "SouthAsia",
    "Bhutan": "SouthAsia",
    "British_Raj": "SouthAsia",
    "Ceylon": "SouthAsia",
    "Nepal": "SouthAsia",
    # East Asia — dense
    "Imperial_Japan": "EastAsia",
    "Korea": "EastAsia",
    "Manchu_Empire": "EastAsia",
    "Taiwan": "EastAsia",
    # Southeast Asia
    "Annam": "SoutheastAsia",
    "Brunei": "SoutheastAsia",
    "French_Indochina": "SoutheastAsia",
    "Malaya": "SoutheastAsia",
    "Philippines": "SoutheastAsia",
    "Rattanakosin_Kingdom": "SoutheastAsia",
    # Middle East — sparse, arid
    "Arabia": "MiddleEast",
    "Oman": "MiddleEast",
    "Ottoman_Empire": "MiddleEast",
    "Persia": "MiddleEast",
    "Qatar": "MiddleEast",
    "Trucial_Oman": "MiddleEast",
    # North Africa — sparse, arid/desert interior
    "Algeria": "NorthAfrica",
    "Egypt": "NorthAfrica",
    "Kanem_Bornu": "NorthAfrica",
    "Morocco": "NorthAfrica",
    "Sokoto_Caliphate": "NorthAfrica",
    "Sultanate_of_Damagaram": "NorthAfrica",
    "Tukular_Caliphate": "NorthAfrica",
    "Wadai_Empire": "NorthAfrica",
    "Wassoulou_Empire": "NorthAfrica",
    # Sub-Saharan Africa
    "Asante": "SubSaharanAfrica",
    "Ato_trading_confederacy": "SubSaharanAfrica",
    "Barotse": "SubSaharanAfrica",
    "Basutoland": "SubSaharanAfrica",
    "Benin": "SubSaharanAfrica",
    "Borgu_States": "SubSaharanAfrica",
    "Buganda": "SubSaharanAfrica",
    "Bunyoro": "SubSaharanAfrica",
    "Burundi": "SubSaharanAfrica",
    "Calabar": "SubSaharanAfrica",
    "Cape_Colony": "SubSaharanAfrica",
    "Congo": "SubSaharanAfrica",
    "Cotonou": "SubSaharanAfrica",
    "Dahomey": "SubSaharanAfrica",
    "Dendi_Kingdom": "SubSaharanAfrica",
    "Ethiopia": "SubSaharanAfrica",
    "Futa_Jalon": "SubSaharanAfrica",
    "Futa_Toro": "SubSaharanAfrica",
    "Gabon": "SubSaharanAfrica",
    "Gambia": "SubSaharanAfrica",
    "Griqualand_West": "SubSaharanAfrica",
    "Harer_Egypt": "SubSaharanAfrica",
    "Ibadan": "SubSaharanAfrica",
    "Imerina": "SubSaharanAfrica",
    "Ivory_Coast": "SubSaharanAfrica",
    "Kong_Empire": "SubSaharanAfrica",
    "Kuba": "SubSaharanAfrica",
    "Lagos": "SubSaharanAfrica",
    "Liberia": "SubSaharanAfrica",
    "Lozi": "SubSaharanAfrica",
    "Luba": "SubSaharanAfrica",
    "Lunda": "SubSaharanAfrica",
    "Madagascar": "SubSaharanAfrica",
    "Mbailundu": "SubSaharanAfrica",
    "Mirambo_Unyanyembe_Ukimbu": "SubSaharanAfrica",
    "Mossi_States": "SubSaharanAfrica",
    "Mozambique": "SubSaharanAfrica",
    "Natal": "SubSaharanAfrica",
    "Ndebele": "SubSaharanAfrica",
    "Nguni": "SubSaharanAfrica",
    "Ngwato": "SubSaharanAfrica",
    "Opobo": "SubSaharanAfrica",
    "Orange_Free_State": "SubSaharanAfrica",
    "Ovimbundu": "SubSaharanAfrica",
    "Oyo": "SubSaharanAfrica",
    "Portuguese_Guinea": "SubSaharanAfrica",
    "Rabih_az_Zubayr": "SubSaharanAfrica",
    "Rwanda": "SubSaharanAfrica",
    "Shona": "SubSaharanAfrica",
    "Sierra_Leone": "SubSaharanAfrica",
    "Spanish_Guinea": "SubSaharanAfrica",
    "Sultanate_of_Utetera": "SubSaharanAfrica",
    "Sultinate_of_Zanzibar": "SubSaharanAfrica",
    "Swaziland": "SubSaharanAfrica",
    "Teke": "SubSaharanAfrica",
    "Transvaal": "SubSaharanAfrica",
    "Yaka": "SubSaharanAfrica",
    "Yeke": "SubSaharanAfrica",
    "Zululand": "SubSaharanAfrica",
    # Central Asia — sparse, arid interior
    "Bokhara_Khanate": "CentralAsia",
    "central_Asian_khanates": "CentralAsia",
    # North America — sparse frontier
    "Canada": "NorthAmerica",
    "United_States_of_America": "NorthAmerica",
    # Central America
    "Belize": "CentralAmerica",
    "Costa_Rica": "CentralAmerica",
    "El_Salvador": "CentralAmerica",
    "Guatemala": "CentralAmerica",
    "Honduras": "CentralAmerica",
    "Mexico": "CentralAmerica",
    "Nicaragua": "CentralAmerica",
    # Caribbean — dense islands
    "Anguilla": "Caribbean",
    "Antigua_and_Barbuda": "Caribbean",
    "Dominica": "Caribbean",
    "Dominican_Republic": "Caribbean",
    "Guadeloupe": "Caribbean",
    "Haiti": "Caribbean",
    "Montserrat": "Caribbean",
    "Netherlands_Antilles": "Caribbean",
    "Saint_Barthelemy": "Caribbean",
    "Saint_Kitts_and_Nevis": "Caribbean",
    "Saint_Martin": "Caribbean",
    # South America
    "Argentina": "SouthAmerica",
    "Bolivia": "SouthAmerica",
    "British_Guiana": "SouthAmerica",
    "Chile": "SouthAmerica",
    "Colombia": "SouthAmerica",
    "Dutch_Guiana": "SouthAmerica",
    "Ecuador": "SouthAmerica",
    "French_Guiana": "SouthAmerica",
    "Kingdom_of_Brazil": "SouthAmerica",
    "Paraguay": "SouthAmerica",
    "Peru": "SouthAmerica",
    "Uruguay": "SouthAmerica",
    "Venezuela": "SouthAmerica",
    # Oceania / Pacific islands — sparse
    "American_Samoa": "Oceania",
    "Fiji": "Oceania",
    "Kingdom_of_Hawaii": "Oceania",
    "M_ori": "Oceania",
    "Niue": "Oceania",
    "Papua_New_Guinea": "Oceania",
    "Polynesians": "Oceania",
    "Samoa": "Oceania",
    "Tonga": "Oceania",
}

# region -> (min_people_per_km2, max_people_per_km2), used to sample a per-province
# density in try_option_c/run. Approximate 1880-era relative bands, not researched
# real data: denser South/East Asia and Western Europe, sparser Northern Europe /
# Central Asia / interior deserts.
REGION_DENSITY_RANGES = {
    "WesternEurope": (60.0, 120.0),
    "NorthernEurope": (10.0, 30.0),
    "EasternEurope": (35.0, 80.0),
    "Russia": (3.0, 15.0),
    "SouthAsia": (80.0, 180.0),
    "EastAsia": (60.0, 150.0),
    "SoutheastAsia": (30.0, 90.0),
    "MiddleEast": (3.0, 25.0),
    "NorthAfrica": (3.0, 20.0),
    "SubSaharanAfrica": (3.0, 25.0),
    "CentralAsia": (1.0, 8.0),
    "NorthAmerica": (2.0, 12.0),
    "CentralAmerica": (8.0, 35.0),
    "Caribbean": (25.0, 90.0),
    "SouthAmerica": (2.0, 18.0),
    "Oceania": (1.0, 12.0),
    "Default": (5.0, 30.0),
}

NE_ADMIN1_URL = "https://naciscdn.org/naturalearth/10m/cultural/ne_10m_admin_1_states_provinces.zip"
NE_PLACES_URL = "https://naciscdn.org/naturalearth/10m/cultural/ne_10m_populated_places.zip"


# ---------------------------------------------------------------------------
# ID normalization — mirrors Program.cs NormalizeAscii + ToMapFeatureId exactly
# ---------------------------------------------------------------------------
_ASCII_MAP = {
    "é": "e", "è": "e", "ê": "e", "ë": "e",
    "à": "a", "á": "a", "â": "a", "ä": "a",
    "ì": "i", "í": "i", "î": "i", "ï": "i",
    "ò": "o", "ó": "o", "ô": "o", "ö": "o",
    "ù": "u", "ú": "u", "û": "u", "ü": "u",
    "ø": "o", "ñ": "n", "ç": "c", "ß": "ss",
}


def normalize_ascii(s):
    out = []
    for c in s:
        if ord(c) < 128:
            out.append(c)
        elif c in _ASCII_MAP:
            out.append(_ASCII_MAP[c])
        # else: dropped, matching Program.cs's switch-with-no-default behaviour
    return "".join(out)


def to_map_feature_id(normalized):
    sb = []
    last_was_underscore = False
    for c in normalized:
        if c.isalnum() and ord(c) < 128:
            sb.append(c)
            last_was_underscore = False
        elif not last_was_underscore and sb:
            sb.append("_")
            last_was_underscore = True
    return "".join(sb).rstrip("_")


def slugify(name):
    """ASCII-normalize + underscore-join, same convention as to_map_feature_id."""
    return to_map_feature_id(normalize_ascii(name))


GENERIC_FEATURE_RE = re.compile(r"^feature_\d+$")


def get_string_prop(props, *keys):
    for key in keys:
        val = props.get(key)
        if val is not None:
            return str(val)
    return None


# ---------------------------------------------------------------------------
# Step 1-2: reconstruct country polygons
# ---------------------------------------------------------------------------
def load_country_polygons():
    """Return dict countryId -> (shapely polygon in WGS84, displayName, area_km2)."""
    with open(COUNTRY_CONFIG_PATH, encoding="utf-8") as f:
        country_config = json.load(f)

    with open(WORLD_GEOJSON_PATH, encoding="utf-8") as f:
        world = json.load(f)

    # mapFeatureId -> list of shapely geometries (handles duplicate-named features)
    feature_geoms = {}
    fallback_index = 0
    for feature in world["features"]:
        if feature is None:
            fallback_index += 1
            continue
        props = feature.get("properties", {}) or {}
        name = get_string_prop(props, "NAME", "name", "ADMIN", "admin", "NAME_LONG", "SOVEREIGNT")
        feature_name = name if name is not None else f"feature_{fallback_index}"
        fallback_index += 1

        if GENERIC_FEATURE_RE.match(feature_name):
            continue

        normalized_name = normalize_ascii(feature_name)
        map_feature_id = to_map_feature_id(normalized_name)

        geom = shape(feature["geometry"])
        if not geom.is_valid:
            geom = make_valid(geom)
        feature_geoms.setdefault(map_feature_id, []).append(geom)

    countries = {}
    for entry in country_config["countries"]:
        country_id = entry["countryId"]
        display_name = entry["displayName"]
        feature_ids = list(entry.get("mainMapFeatureIds", [])) + list(entry.get("secondaryMapFeatureIds", []))

        geoms = []
        for fid in feature_ids:
            geoms.extend(feature_geoms.get(fid, []))

        if not geoms:
            print(f"WARN: {country_id} — no matching geometry found for any of {feature_ids}; skipping")
            continue

        polygon = unary_union(geoms)
        if not polygon.is_valid:
            polygon = make_valid(polygon)

        # Area in equal-area CRS
        gs = gpd.GeoSeries([polygon], crs=WGS84_CRS).to_crs(EQUAL_AREA_CRS)
        area_km2 = gs.area.iloc[0] / 1_000_000.0

        countries[country_id] = {
            "polygon": polygon,
            "displayName": display_name,
            "area_km2": area_km2,
        }

    return countries


# ---------------------------------------------------------------------------
# Step 3: Natural Earth download/cache
# ---------------------------------------------------------------------------
def download_and_extract(url, cache_dir, force=False):
    os.makedirs(cache_dir, exist_ok=True)
    zip_name = os.path.basename(url)
    zip_path = os.path.join(cache_dir, zip_name)
    extract_dir = os.path.join(cache_dir, zip_name.replace(".zip", ""))

    shp_candidates = []
    if os.path.isdir(extract_dir):
        shp_candidates = [f for f in os.listdir(extract_dir) if f.endswith(".shp")]

    if shp_candidates and not force:
        print(f"SKIP: {zip_name} (already extracted)")
        return os.path.join(extract_dir, shp_candidates[0])

    print(f"Downloading {url} ...")
    resp = requests.get(url, timeout=120, headers={"User-Agent": "GlobalStrategyProvinceGen/1.0"})
    resp.raise_for_status()
    with open(zip_path, "wb") as f:
        f.write(resp.content)

    with zipfile.ZipFile(zip_path) as zf:
        zf.extractall(extract_dir)

    shp_candidates = [f for f in os.listdir(extract_dir) if f.endswith(".shp")]
    if not shp_candidates:
        raise RuntimeError(f"No .shp found after extracting {zip_name}")

    print(f"OK: {zip_name} -> {extract_dir}")
    return os.path.join(extract_dir, shp_candidates[0])


def load_natural_earth(force=False):
    admin1_shp = download_and_extract(NE_ADMIN1_URL, CACHE_DIR, force=force)
    places_shp = download_and_extract(NE_PLACES_URL, CACHE_DIR, force=force)

    admin1 = gpd.read_file(admin1_shp)
    if admin1.crs is None:
        admin1 = admin1.set_crs(WGS84_CRS)
    else:
        admin1 = admin1.to_crs(WGS84_CRS)

    places = gpd.read_file(places_shp)
    if places.crs is None:
        places = places.set_crs(WGS84_CRS)
    else:
        places = places.to_crs(WGS84_CRS)

    return admin1, places


# ---------------------------------------------------------------------------
# Step 4a: Option A — admin-1 overlay
# ---------------------------------------------------------------------------
def admin1_name(row):
    for key in ("name", "woe_name", "NAME", "WOE_NAME"):
        if key in row and row[key]:
            return str(row[key])
    return "Province"


def try_option_a(country_polygon, area_km2, admin1_gdf):
    country_gdf = gpd.GeoDataFrame({"geometry": [country_polygon]}, crs=WGS84_CRS)
    try:
        pieces = gpd.overlay(admin1_gdf, country_gdf, how="intersection")
    except Exception as exc:
        return None, f"overlay failed: {exc}"

    pieces = pieces[~pieces.geometry.is_empty & pieces.geometry.notnull()]
    # Explode multi-part results so each row is a single polygon piece
    pieces = pieces.explode(index_parts=False).reset_index(drop=True)
    pieces = pieces[pieces.geometry.area > 0]

    piece_count = len(pieces)
    if piece_count < OPTION_A_MIN_PIECES or piece_count > OPTION_A_MAX_PIECES:
        return None, f"piece count {piece_count} outside [{OPTION_A_MIN_PIECES},{OPTION_A_MAX_PIECES}]"

    pieces_equal_area = pieces.to_crs(EQUAL_AREA_CRS)
    piece_areas_km2 = pieces_equal_area.geometry.area / 1_000_000.0
    sliver_threshold = area_km2 * SLIVER_AREA_RATIO
    sliver_count = int((piece_areas_km2 < sliver_threshold).sum())
    sliver_fraction = sliver_count / piece_count if piece_count > 0 else 1.0

    if sliver_fraction > SLIVER_MAX_FRACTION:
        return None, f"sliver fraction {sliver_fraction:.2f} > {SLIVER_MAX_FRACTION}"

    provinces = []
    for _, row in pieces.iterrows():
        provinces.append({
            "geometry": row.geometry,
            "name": admin1_name(row),
        })
    return provinces, None


# ---------------------------------------------------------------------------
# Step 4b: Option C — deterministic Voronoi fallback
# ---------------------------------------------------------------------------
def deterministic_seed(country_id):
    digest = hashlib.md5(country_id.encode("utf-8")).hexdigest()
    return int(digest[:16], 16)


def seed_points_in_polygon(polygon, n, rng):
    minx, miny, maxx, maxy = polygon.bounds
    points = []
    attempts = 0
    max_attempts = n * 2000 + 2000
    while len(points) < n and attempts < max_attempts:
        attempts += 1
        x = rng.uniform(minx, maxx)
        y = rng.uniform(miny, maxy)
        p = Point(x, y)
        if polygon.contains(p):
            points.append(p)
    return points


def voronoi_cells(seed_points, polygon):
    """Build Voronoi cells from seed points, clipped to polygon, via a bounded box hack."""
    coords = np.array([[p.x, p.y] for p in seed_points])
    minx, miny, maxx, maxy = polygon.bounds
    span_x = max(maxx - minx, 1e-6)
    span_y = max(maxy - miny, 1e-6)
    # Add far-away dummy points so all real regions stay finite (standard bounded-Voronoi trick)
    pad = 10 * max(span_x, span_y) + 10
    dummy = np.array([
        [minx - pad, miny - pad], [minx - pad, maxy + pad],
        [maxx + pad, miny - pad], [maxx + pad, maxy + pad],
    ])
    all_points = np.vstack([coords, dummy])
    vor = Voronoi(all_points)

    cells = []
    for point_idx in range(len(seed_points)):
        region_idx = vor.point_region[point_idx]
        region = vor.regions[region_idx]
        if not region or -1 in region:
            continue
        polygon_pts = [vor.vertices[i] for i in region]
        if len(polygon_pts) < 3:
            continue
        from shapely.geometry import Polygon as ShPolygon
        cell = ShPolygon(polygon_pts)
        if not cell.is_valid:
            cell = make_valid(cell)
        clipped = cell.intersection(polygon)
        if clipped.is_empty or clipped.area <= 0:
            continue
        cells.append(clipped)
    return cells


def nearest_settlement_name(centroid, places_gdf):
    # Uncapped nearest-by-distance search — always names a cell after its closest
    # known settlement, however far, so remote cells still get a real place name
    # instead of falling through to a generic "Province N" label.
    places_gdf = places_gdf.copy()
    places_gdf["_dist"] = places_gdf.geometry.distance(centroid)
    nearest = places_gdf.nsmallest(1, "_dist")
    if nearest.empty:
        return None
    row = nearest.iloc[0]
    for key in ("name", "NAME", "NAMEASCII"):
        if key in row and row[key]:
            return str(row[key])
    return None


_COMPASS_DIRECTIONS = [
    "Eastern", "Northeastern", "Northern", "Northwestern",
    "Western", "Southwestern", "Southern", "Southeastern",
]
_COMPASS_CENTRAL_RATIO = 0.25  # normalized distance from bbox center below which a cell is "Central"

# Proper Russian translations for the compass fallback names (not transliterated —
# these are real words, unlike the settlement/admin-1 proper nouns elsewhere).
# Feminine adjective form, matching existing Russian geographic convention
# (Северная Америка, Южная Африка, Восточная Европа, Центральная Азия, ...).
COMPASS_RU = {
    "Eastern": "Восточная",
    "Northeastern": "Северо-Восточная",
    "Northern": "Северная",
    "Northwestern": "Северо-Западная",
    "Western": "Западная",
    "Southwestern": "Юго-Западная",
    "Southern": "Южная",
    "Southeastern": "Юго-Восточная",
    "Central": "Центральная",
}


def compass_direction_name(centroid, country_polygon, display_name):
    """Fallback name for cells with no settlement data at all: a compass position
    relative to the country's own bounding box (e.g. "Northern Afghanistan").
    Returns (english_name, compass_key) — compass_key indexes COMPASS_RU so the
    locale step can build a properly-translated Russian name instead of
    transliterating "Northern" phonetically."""
    minx, miny, maxx, maxy = country_polygon.bounds
    cx = (minx + maxx) / 2.0
    cy = (miny + maxy) / 2.0
    half_w = max((maxx - minx) / 2.0, 1e-6)
    half_h = max((maxy - miny) / 2.0, 1e-6)
    nx = (centroid.x - cx) / half_w
    ny = (centroid.y - cy) / half_h

    if math.hypot(nx, ny) < _COMPASS_CENTRAL_RATIO:
        return f"Central {display_name}", "Central"

    angle = math.degrees(math.atan2(ny, nx)) % 360.0
    idx = int(((angle + 22.5) % 360.0) // 45.0)
    compass_key = _COMPASS_DIRECTIONS[idx]
    return f"{compass_key} {display_name}", compass_key


def try_option_c(country_id, display_name, country_polygon, area_km2, places_gdf, warnings):
    multiplier = PER_COUNTRY_DENSITY_MULTIPLIER.get(country_id, 1.0)
    n_seeds = int(round(area_km2 / OPTION_C_AREA_PER_SEED_KM2 * multiplier))
    effective_max_seeds = int(round(OPTION_C_MAX_SEEDS * multiplier))
    n_seeds = max(OPTION_C_MIN_SEEDS, min(effective_max_seeds, n_seeds))

    import random
    rng = random.Random(deterministic_seed(country_id))

    seeds = seed_points_in_polygon(country_polygon, n_seeds, rng)
    if len(seeds) < 2:
        warnings.append(f"{country_id}: Option C could not place enough seed points; falling back to Micro")
        return None

    cells = voronoi_cells(seeds, country_polygon)
    if not cells:
        warnings.append(f"{country_id}: Option C produced zero valid cells; falling back to Micro")
        return None

    # Places within/near this country only, to keep nearest-search cheap and relevant
    minx, miny, maxx, maxy = country_polygon.bounds
    margin = 2.0
    local_places = places_gdf.cx[minx - margin:maxx + margin, miny - margin:maxy + margin]

    provinces = []
    for cell in cells:
        centroid = cell.centroid
        name = None
        if not local_places.empty:
            name = nearest_settlement_name(centroid, local_places)
        if name:
            provinces.append({"geometry": cell, "name": name})
        else:
            compass_name, compass_key = compass_direction_name(centroid, country_polygon, display_name)
            provinces.append({"geometry": cell, "name": compass_name, "compassKey": compass_key})

    return provinces


# ---------------------------------------------------------------------------
# Step 5: provinceId assignment with collision handling
# ---------------------------------------------------------------------------
def assign_province_ids(country_id, provinces):
    used = {}
    for prov in provinces:
        base_slug = slugify(prov["name"])
        if not base_slug:
            base_slug = "province"
        count = used.get(base_slug, 0) + 1
        used[base_slug] = count
        suffix = "" if count == 1 else f"_{count}"
        prov["provinceId"] = f"{country_id}__{base_slug}{suffix}"
    return provinces


# ---------------------------------------------------------------------------
# Step 6: province_name.* locale entries (en.asset / ru.asset)
# ---------------------------------------------------------------------------
_TRANSLIT_MULTI = [
    ("shch", "щ"), ("tsch", "ч"), ("sch", "ш"),
    ("kh", "х"), ("ch", "ч"), ("sh", "ш"), ("zh", "ж"), ("ph", "ф"), ("th", "т"),
    ("ya", "я"), ("yo", "ё"), ("yu", "ю"), ("ja", "я"), ("jo", "ё"), ("ju", "ю"),
    ("qu", "кв"), ("ck", "к"),
    ("ee", "и"), ("oo", "у"), ("ou", "у"),
    ("ai", "ай"), ("ei", "ей"), ("au", "ау"), ("eu", "еу"), ("oe", "э"), ("ae", "э"),
]
_TRANSLIT_SINGLE = {
    "a": "а", "b": "б", "c": "к", "d": "д", "e": "е", "f": "ф", "g": "г",
    "h": "х", "i": "и", "j": "й", "k": "к", "l": "л", "m": "м", "n": "н",
    "o": "о", "p": "п", "q": "к", "r": "р", "s": "с", "t": "т", "u": "у",
    "v": "в", "w": "в", "x": "кс", "y": "и", "z": "з",
}


def transliterate_to_cyrillic(name):
    """Deterministic phonetic Latin->Cyrillic transliteration — a placeholder for
    proper nouns (settlement/admin-1 names) that have no established Russian
    exonym on file. Not a real translation; intended to be replaced incrementally."""
    result = []
    i = 0
    lower = name.lower()
    n = len(name)
    while i < n:
        matched = None
        for pat, repl in _TRANSLIT_MULTI:
            if lower.startswith(pat, i):
                matched = (pat, repl)
                break
        if matched:
            pat, repl = matched
            if name[i].isupper():
                repl = repl[0].upper() + repl[1:]
            result.append(repl)
            i += len(pat)
            continue
        c = lower[i]
        if c in _TRANSLIT_SINGLE:
            repl = _TRANSLIT_SINGLE[c]
            if name[i].isupper():
                repl = repl[0].upper() + repl[1:]
            result.append(repl)
        else:
            result.append(name[i])  # digits, spaces, punctuation pass through unchanged
        i += 1
    return "".join(result)


_UNICODE_ESCAPE_RE = re.compile(r"\\u([0-9a-fA-F]{4})")


def _unescape_yaml_double_quoted(s):
    s = _UNICODE_ESCAPE_RE.sub(lambda m: chr(int(m.group(1), 16)), s)
    return s.replace('\\"', '"').replace("\\\\", "\\")


def _parse_yaml_scalar(raw):
    raw = raw.strip()
    if len(raw) >= 2 and raw.startswith('"') and raw.endswith('"'):
        return _unescape_yaml_double_quoted(raw[1:-1])
    return raw


def _yaml_scalar(value):
    """Render a string as a YAML scalar matching Unity's own serializer style:
    plain for simple ASCII, double-quoted with \\uXXXX escapes otherwise."""
    is_simple_ascii = (
        value != ""
        and value.strip() == value
        and all(32 <= ord(c) < 127 for c in value)
        and not value.startswith(('"', "'", "-", "?", "!", "&", "*", "|", ">", "%", "@", "`"))
        and ":" not in value
        and "#" not in value
    )
    if is_simple_ascii:
        return value
    escaped = []
    for c in value:
        if c == '"':
            escaped.append('\\"')
        elif c == "\\":
            escaped.append("\\\\")
        elif ord(c) < 128:
            escaped.append(c)
        else:
            escaped.append(f"\\u{ord(c):04x}")
    return '"' + "".join(escaped) + '"'


def load_locale_map(path):
    """Read an existing LocaleConfig .asset's Entries block into a Key->Value dict."""
    with open(path, "r", encoding="utf-8") as f:
        lines = f.readlines()
    mapping = {}
    i = 0
    while i < len(lines):
        stripped = lines[i].strip()
        if stripped.startswith("- Key:"):
            key = stripped[len("- Key:"):].strip()
            value = ""
            if i + 1 < len(lines) and lines[i + 1].strip().startswith("Value:"):
                value = _parse_yaml_scalar(lines[i + 1].strip()[len("Value:"):])
            mapping[key] = value
            i += 2
        else:
            i += 1
    return mapping


def update_locale_asset(path, entries):
    """Replace all `province_name.*` entries in a LocaleConfig .asset's Entries
    block with `entries` (list of (key, value)), leaving every other key untouched."""
    with open(path, "r", encoding="utf-8") as f:
        lines = f.readlines()

    entries_idx = None
    for i, line in enumerate(lines):
        if line.rstrip("\n") == "  Entries:":
            entries_idx = i
            break
    if entries_idx is None:
        raise RuntimeError(f"Could not find 'Entries:' block in {path}")

    header = lines[:entries_idx + 1]
    body = lines[entries_idx + 1:]

    kept = []
    i = 0
    while i < len(body):
        line = body[i]
        if line.strip().startswith("- Key:"):
            key = line.strip()[len("- Key:"):].strip()
            value_line = body[i + 1] if i + 1 < len(body) else ""
            if not key.startswith(PROVINCE_LOCALE_KEY_PREFIX):
                kept.append(line)
                kept.append(value_line)
            i += 2
        else:
            kept.append(line)
            i += 1

    new_lines = []
    for key, value in entries:
        new_lines.append(f"  - Key: {key}\n")
        new_lines.append(f"    Value: {_yaml_scalar(value)}\n")

    # Guard against a missing trailing newline on the last pre-existing line
    # (e.g. no final newline at EOF), which would otherwise merge into new_lines[0].
    if kept and not kept[-1].endswith("\n"):
        kept[-1] += "\n"
    elif not kept and not header[-1].endswith("\n"):
        header[-1] += "\n"

    with open(path, "w", encoding="utf-8") as f:
        f.writelines(header + kept + new_lines)

    print(f"Updated {path}: {len(entries)} '{PROVINCE_LOCALE_KEY_PREFIX}*' entries")


def update_province_locales(all_features):
    ru_country_names = load_locale_map(RU_LOCALE_PATH)

    en_entries = []
    ru_entries = []
    for feature in sorted(all_features, key=lambda f: f["properties"]["provinceId"]):
        props = feature["properties"]
        province_id = props["provinceId"]
        country_id = props["countryId"]
        display_name = props["displayName"]
        compass_key = props.get("compassKey")
        key = f"{PROVINCE_LOCALE_KEY_PREFIX}{province_id}"

        en_entries.append((key, display_name))

        if compass_key is not None:
            ru_country_name = ru_country_names.get(
                f"country_name.{country_id}",
                transliterate_to_cyrillic(display_name[len(compass_key) + 1:]))
            ru_name = f"{COMPASS_RU[compass_key]} {ru_country_name}"
        else:
            ru_name = transliterate_to_cyrillic(display_name)
        ru_entries.append((key, ru_name))

    update_locale_asset(EN_LOCALE_PATH, en_entries)
    update_locale_asset(RU_LOCALE_PATH, ru_entries)


# ---------------------------------------------------------------------------
# Main pipeline
# ---------------------------------------------------------------------------
def run(force_download=False):
    print("Loading country polygons from country_config.json + world_1880.json ...")
    countries = load_country_polygons()
    print(f"Reconstructed {len(countries)} country polygons")

    print("Loading Natural Earth datasets (admin-1 + populated places) ...")
    admin1_gdf, places_gdf = load_natural_earth(force=force_download)
    print(f"admin-1 features: {len(admin1_gdf)}, populated places: {len(places_gdf)}")

    counts = {}
    warnings = []
    all_features = []

    for country_id, data in sorted(countries.items()):
        polygon = data["polygon"]
        display_name = data["displayName"]
        area_km2 = data["area_km2"]

        provinces = None
        method = None

        if area_km2 < MICRO_STATE_AREA_KM2:
            provinces = [{"geometry": polygon, "name": display_name}]
            method = "Micro"
        else:
            option_a_provinces, reject_reason = try_option_a(polygon, area_km2, admin1_gdf)
            if option_a_provinces is not None:
                provinces = option_a_provinces
                method = "OptionA"
            else:
                warnings.append(f"{country_id}: Option A rejected ({reject_reason}); trying Option C")
                option_c_provinces = try_option_c(country_id, display_name, polygon, area_km2, places_gdf, warnings)
                if option_c_provinces is not None:
                    provinces = option_c_provinces
                    method = "OptionC"
                else:
                    provinces = [{"geometry": polygon, "name": display_name}]
                    method = "Micro"

        provinces = assign_province_ids(country_id, provinces)
        counts[method + "_countries"] = counts.get(method + "_countries", 0) + 1

        for prov in provinces:
            all_features.append({
                "type": "Feature",
                "properties": {
                    "provinceId": prov["provinceId"],
                    "countryId": country_id,
                    "displayName": prov["name"],
                    "generationMethod": method,
                    "compassKey": prov.get("compassKey"),
                },
                "geometry": mapping(prov["geometry"]),
            })

    feature_collection = {"type": "FeatureCollection", "features": all_features}

    os.makedirs(os.path.dirname(INTERMEDIATE_PATH), exist_ok=True)
    with open(INTERMEDIATE_PATH, "w", encoding="utf-8") as f:
        json.dump(feature_collection, f)

    print(f"Wrote {len(all_features)} provinces across {len(countries)} countries to {INTERMEDIATE_PATH}")

    print(f"Running mapshaper simplify ({MAPSHAPER_SIMPLIFY_PCT}%) ...")
    result = subprocess.run(
        ["npx", "mapshaper", "-i", INTERMEDIATE_PATH,
         "-simplify", "keep-shapes", f"{MAPSHAPER_SIMPLIFY_PCT}%",
         "-o", INTERMEDIATE_PATH, "force"],
        shell=(sys.platform == "win32"),
        capture_output=True, text=True,
    )
    print(result.stdout)
    if result.returncode != 0:
        print(result.stderr)
        raise RuntimeError("mapshaper simplify pass failed")

    print("Updating province_name.* locale entries ...")
    update_province_locales(all_features)

    # ---- Summary ----
    print("\n--- Summary ---")
    print(f"Total countries processed: {len(countries)}")
    print(f"Micro-state countries:  {counts.get('Micro_countries', 0)}")
    print(f"Option A countries:     {counts.get('OptionA_countries', 0)}")
    print(f"Option C countries:     {counts.get('OptionC_countries', 0)}")
    print(f"Total provinces:        {len(all_features)}")
    if warnings:
        print(f"\nWarnings ({len(warnings)}):")
        for w in warnings:
            print(f"  - {w}")
    else:
        print("\nNo warnings.")


if __name__ == "__main__":
    force = "--force-download" in sys.argv
    run(force_download=force)
