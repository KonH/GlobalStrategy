"""
Flag and org image downloader for GlobalStrategy.

Usage:
    python3 .claude/download_flags.py [--dry-run] [--force]

Options:
    --dry-run   Print what would be downloaded without saving anything
    --force     Re-download files that already exist on disk

To add a new country:
    1. Add an entry to COUNTRY_FLAGS mapping countryId -> Wikimedia Commons filename
    2. If the filename is uncertain, add a fallback to COUNTRY_FLAGS_FALLBACK
    3. Run this script

To add a new org:
    1. Add an entry to ORG_FLAGS mapping orgId -> direct PNG URL (or Wikimedia FilePath filename)
    2. Run this script

Output:
    Assets/Textures/Flags/Countries/<countryId>.png
    Assets/Textures/Flags/Orgs/<orgId>.png
"""

import os
import sys
import requests

# ---------------------------------------------------------------------------
# Wikimedia Commons: SVG flags rendered to PNG via the Special:FilePath API.
# Format: countryId -> "File:<wikimedia_filename>.svg"
# Era: 19th century (roughly 1815-1900). Keep filenames era-accurate.
# ---------------------------------------------------------------------------
COUNTRY_FLAGS = {
    "Argentina":                                    "File:Flag_of_Argentina_(1818).svg",
    "Austria_Hungary":                              "File:Civil_Ensign_of_Austria-Hungary_(1869-1918).svg",
    "Belgium":                                      "File:Flag_of_Belgium_(civil).svg",
    "Egypt":                                        "File:Flag_of_Egypt_(1882-1922).svg",
    "Ethiopia":                                     "File:Flag_of_Ethiopia_(1897-1914).svg",
    "France":                                       "File:Flag_of_France.svg",
    "Germany":                                      "File:Flag_of_the_German_Empire.svg",
    "Imperial_Japan":                               "File:Flag_of_Japan.svg",
    "Italy":                                        "File:Flag_of_Italy_(1861-1946).svg",
    "Kingdom_of_Brazil":                            "File:Flag_of_Brazil_(1870-1889).svg",
    "Manchu_Empire":                                "File:Flag_of_the_Qing_Dynasty_(1862-1889).svg",
    "Netherlands":                                  "File:Flag_of_the_Netherlands.svg",
    "Ottoman_Empire":                               "File:Flag_of_the_Ottoman_Empire_(1844-1922).svg",
    "Persia":                                       "File:Flag_of_Persia_(1910).svg",
    "Portugal":                                     "File:Flag_of_Portugal_(1830).svg",
    "Russian_Empire":                               "File:Flag_of_Russia.svg",
    "Spain":                                        "File:Flag_of_Spain_(1785-1873,_1875-1931).svg",
    "SwedenNorway":                                 "File:Flag_of_Sweden-Norway.svg",
    "United_Kingdom_of_Great_Britain_and_Ireland":  "File:Flag_of_the_United_Kingdom.svg",
    "United_States_of_America":                     "File:US_flag_38_stars.svg",
}

# Fallback filenames tried if the primary returns a non-image or <1 KB response.
COUNTRY_FLAGS_FALLBACK = {
    "Persia":                   "File:State_flag_of_Persia_(1806-1925).svg",
    "Portugal":                 "File:Bandeira_de_Portugal_(1830).svg",
    "United_States_of_America": "File:Flag_of_the_United_States.svg",
}

# ---------------------------------------------------------------------------
# Org images: direct PNG URLs (Wikimedia thumbnails or other public domain sources).
# Format: orgId -> direct URL to a PNG image.
# ---------------------------------------------------------------------------
ORG_FLAGS = {
    "Illuminati": "https://upload.wikimedia.org/wikipedia/commons/thumb/a/a9/Eye_of_Providence.svg/256px-Eye_of_Providence.svg.png",
}

# ---------------------------------------------------------------------------
# Config
# ---------------------------------------------------------------------------
WIKIMEDIA_FILEPATH = "https://commons.wikimedia.org/w/index.php?title=Special:FilePath/{filename}&width=256"
COUNTRIES_DIR = "Assets/Textures/Flags/Countries"
ORGS_DIR = "Assets/Textures/Flags/Orgs"
PNG_MAGIC = b"\x89PNG\r\n\x1a\n"


def make_session():
    s = requests.Session()
    s.headers["User-Agent"] = "GlobalStrategyAssetDownloader/1.0"
    return s


def download_one(session, url, dest_path, label, dry_run=False, force=False):
    if not force and os.path.exists(dest_path):
        print(f"SKIP: {label} (already exists)")
        return True
    if dry_run:
        print(f"DRY-RUN: would download {label} -> {dest_path}")
        return True
    try:
        resp = session.get(url, allow_redirects=True, timeout=30)
        ct = resp.headers.get("Content-Type", "")
        if not ct.startswith("image/"):
            print(f"WARN: {label} — unexpected Content-Type: {ct} (url: {resp.url})")
            return False
        content = resp.content
        if len(content) < 1024:
            print(f"WARN: {label} — response too small ({len(content)} B), likely an error page")
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

    # Country flags via Wikimedia FilePath API
    for country_id, filename in COUNTRY_FLAGS.items():
        dest = os.path.join(COUNTRIES_DIR, f"{country_id}.png")
        url = WIKIMEDIA_FILEPATH.format(filename=filename)
        ok = download_one(session, url, dest, country_id, dry_run=dry_run, force=force)
        if not ok and country_id in COUNTRY_FLAGS_FALLBACK:
            fallback = COUNTRY_FLAGS_FALLBACK[country_id]
            fallback_url = WIKIMEDIA_FILEPATH.format(filename=fallback)
            print(f"  -> trying fallback: {fallback}")
            ok = download_one(session, fallback_url, dest, f"{country_id} (fallback)", dry_run=dry_run, force=force)
        if ok:
            downloaded += 1

    # Org images via direct URLs
    for org_id, url in ORG_FLAGS.items():
        dest = os.path.join(ORGS_DIR, f"{org_id}.png")
        if download_one(session, url, dest, org_id, dry_run=dry_run, force=force):
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
            if os.path.getsize(path) < 1024:
                print(f"TOO SMALL: {label}")
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
