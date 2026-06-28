"""
Flag and org image downloader for GlobalStrategy.

Usage (run from project root):
    .venv\Scripts\python.exe .claude\download_flags.py [--dry-run] [--force]

Options:
    --dry-run   Print what would be downloaded without saving anything
    --force     Re-download files that already exist on disk

To add a new country:
    1. Add an entry to COUNTRY_FLAGS mapping countryId -> Wikimedia Commons filename
    2. If the filename is uncertain, add a fallback to COUNTRY_FLAGS_FALLBACK
    3. Run this script

To add a new org:
    1. Add an entry to ORG_FLAGS mapping orgId -> Wikimedia Commons filename (e.g. "File:Eye_of_Providence.svg")
    2. Run this script

Output:
    Assets/Textures/Flags/Countries/<countryId>.png
    Assets/Textures/Flags/Orgs/<orgId>.png
"""

import os
import sys
import requests

# ---------------------------------------------------------------------------
# Wikimedia Commons SVG flags — Wikimedia renders SVG to PNG server-side.
# Format: countryId -> "File:<wikimedia_filename>.svg"
# Era: 19th century (roughly 1815-1900). Keep filenames era-accurate.
# ---------------------------------------------------------------------------
COUNTRY_FLAGS = {
    "Argentina":                                    "File:Flag_of_Argentina_(1818).svg",
    "Austria_Hungary":                              "File:Flag_of_Austria-Hungary_(1869-1918).svg",
    "Belgium":                                      "File:Flag_of_Belgium_(civil).svg",
    "Egypt":                                        "File:Flag_of_Egypt_(1882-1922).svg",
    "Ethiopia":                                     "File:Flag_of_Ethiopia_(1897-1914).svg",
    "France":                                       "File:Flag_of_France.svg",
    "Germany":                                      "File:Flag_of_the_German_Empire.svg",
    "Imperial_Japan":                               "File:Flag_of_Japan.svg",
    "Italy":                                        "File:Flag_of_Italy_(1861-1946).svg",
    "Kingdom_of_Brazil":                            "File:Flag_of_Empire_of_Brazil_(1870-1889).svg",
    "Manchu_Empire":                                "File:Flag_of_the_Qing_Dynasty_(1862-1889).svg",
    "Netherlands":                                  "File:Flag_of_the_Netherlands.svg",
    "Ottoman_Empire":                               "File:Flag_of_the_Ottoman_Empire.svg",
    "Persia":                                       "File:Flag_of_Persia_(1910).svg",
    "Portugal":                                     "File:Flag_of_Portugal_(1830).svg",
    "Russian_Empire":                               "File:Flag_of_Russia.svg",
    "Spain":                                        "File:Flag_of_Spain.svg",
    "SwedenNorway":                                 "File:Flag_of_Sweden.svg",
    "United_Kingdom_of_Great_Britain_and_Ireland":  "File:Flag_of_the_United_Kingdom.svg",
    "United_States_of_America":                     "File:US_flag_38_stars.svg",
}

# Fallback filenames tried if the primary fails to resolve or download.
COUNTRY_FLAGS_FALLBACK = {
    "Persia":                   "File:State_flag_of_Persia_(1806-1925).svg",
    "Portugal":                 "File:Bandeira_de_Portugal_(1830).svg",
    "United_States_of_America": "File:Flag_of_the_United_States.svg",
}

# ---------------------------------------------------------------------------
# Org images via Wikimedia Commons.
# Format: orgId -> "File:<wikimedia_filename>.svg"
# ---------------------------------------------------------------------------
ORG_FLAGS = {
    "Illuminati": "File:Eye_of_Providence.svg",
}

# ---------------------------------------------------------------------------
# Config
# ---------------------------------------------------------------------------
WIKIMEDIA_API = "https://commons.wikimedia.org/w/api.php"
COUNTRIES_DIR = "Assets/Textures/Flags/Countries"
ORGS_DIR = "Assets/Textures/Flags/Orgs"
PNG_MAGIC = b"\x89PNG\r\n\x1a\n"
THUMB_WIDTH = 256


def make_session():
    s = requests.Session()
    s.headers["User-Agent"] = "GlobalStrategyAssetDownloader/1.0 (https://github.com/KonH/GlobalStrategy)"
    return s


def resolve_wikimedia_png_url(session, filename):
    """Return a PNG render URL for a Wikimedia Commons file via the MediaWiki API.

    For SVG files the API thumburl ends in '.svg'; appending '.png' converts it
    to Wikimedia's server-side PNG render URL without any local SVG processing.
    """
    params = {
        "action": "query",
        "titles": filename,
        "prop": "imageinfo",
        "iiprop": "url|mime",
        "iiurlwidth": str(THUMB_WIDTH),
        "format": "json",
    }
    try:
        resp = session.get(WIKIMEDIA_API, params=params, timeout=30)
        data = resp.json()
        pages = data.get("query", {}).get("pages", {})
        for pid, page in pages.items():
            if pid == "-1":
                return None
            imageinfo = page.get("imageinfo", [])
            if not imageinfo:
                return None
            info = imageinfo[0]
            url = info.get("thumburl") or info.get("url")
            if url and url.endswith(".svg"):
                url += ".png"
            return url
    except Exception as exc:
        print(f"  API error for {filename}: {exc}")
    return None


def download_wikimedia_file(session, filename, dest_path, label, dry_run=False, force=False):
    """Resolve a Wikimedia filename to a PNG URL via the API, then download it."""
    if not force and os.path.exists(dest_path):
        print(f"SKIP: {label} (already exists)")
        return True
    if dry_run:
        print(f"DRY-RUN: would download {label} -> {dest_path}")
        return True
    url = resolve_wikimedia_png_url(session, filename)
    if not url:
        print(f"WARN: {label} — could not resolve URL for {filename}")
        return False
    try:
        resp = session.get(url, allow_redirects=True, timeout=30)
        ct = resp.headers.get("Content-Type", "")
        if not ct.startswith("image/"):
            print(f"WARN: {label} — unexpected Content-Type: {ct} (url: {resp.url})")
            return False
        content = resp.content
        if not content.startswith(PNG_MAGIC):
            print(f"WARN: {label} — response is not a PNG ({len(content)} B, ct={ct})")
            return False
        os.makedirs(os.path.dirname(dest_path), exist_ok=True)
        with open(dest_path, "wb") as f:
            f.write(content)
        print(f"OK: {label}")
        return True
    except Exception as exc:
        print(f"WARN: {label} — {exc}")
        return False


def run(dry_run=False, force=False):
    session = make_session()
    downloaded = 0
    total = len(COUNTRY_FLAGS) + len(ORG_FLAGS)

    # Country flags via Wikimedia MediaWiki API
    for country_id, filename in COUNTRY_FLAGS.items():
        dest = os.path.join(COUNTRIES_DIR, f"{country_id}.png")
        ok = download_wikimedia_file(session, filename, dest, country_id, dry_run=dry_run, force=force)
        if not ok and country_id in COUNTRY_FLAGS_FALLBACK:
            fallback = COUNTRY_FLAGS_FALLBACK[country_id]
            print(f"  -> trying fallback: {fallback}")
            ok = download_wikimedia_file(session, fallback, dest, f"{country_id} (fallback)", dry_run=dry_run, force=force)
        if ok:
            downloaded += 1

    # Org images via Wikimedia MediaWiki API (filename stored in ORG_FLAGS as "File:...")
    for org_id, filename in ORG_FLAGS.items():
        dest = os.path.join(ORGS_DIR, f"{org_id}.png")
        if download_wikimedia_file(session, filename, dest, org_id, dry_run=dry_run, force=force):
            downloaded += 1

    print(f"\nDownloaded {downloaded}/{total} files")

    # Inline verification
    if not dry_run:
        print("\n--- Verification ---")
        all_expected = (
            [os.path.join(COUNTRIES_DIR, f"{cid}.png") for cid in COUNTRY_FLAGS]
            + [os.path.join(ORGS_DIR, f"{oid}.png") for oid in ORG_FLAGS]
        )
        ok_count = 0
        for path in all_expected:
            label = os.path.basename(path)
            if not os.path.exists(path):
                print(f"MISSING: {label}")
                continue
            with open(path, "rb") as f:
                header = f.read(8)
            if header != PNG_MAGIC:
                print(f"NOT PNG: {label} (header: {header!r})")
                continue
            print(f"OK: {label}")
            ok_count += 1
        print(f"\nVerified {ok_count}/{total} files OK")
        return ok_count == total

    return downloaded == total


if __name__ == "__main__":
    dry_run = "--dry-run" in sys.argv
    force = "--force" in sys.argv
    success = run(dry_run=dry_run, force=force)
    sys.exit(0 if success else 1)
