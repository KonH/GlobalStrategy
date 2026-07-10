"""
Diagnose flag download failures by querying the Wikimedia MediaWiki API
for a list of filenames and printing their resolved URLs and MIME types.

Usage:
    python scripts/check_flags.py [File:Filename.svg ...]

With no arguments, checks the hardcoded FAILING list below.
"""

import sys
import requests

WIKIMEDIA_API = "https://commons.wikimedia.org/w/api.php"

FAILING = [
    "File:Civil_Ensign_of_Austria-Hungary_(1869-1918).svg",
    "File:Flag_of_Belgium_(civil).svg",
    "File:Flag_of_France.svg",
    "File:Flag_of_the_German_Empire.svg",
    "File:Flag_of_Brazil_(1870-1889).svg",
    "File:Flag_of_the_Netherlands.svg",
    "File:Flag_of_the_Ottoman_Empire_(1844-1922).svg",
    "File:Flag_of_Russia.svg",
    "File:Flag_of_Spain_(1785-1873,_1875-1931).svg",
    "File:Flag_of_Sweden-Norway.svg",
]


def check(session, filename):
    params = {
        "action": "query",
        "titles": filename,
        "prop": "imageinfo",
        "iiprop": "url|mediatype|mime",
        "iiurlwidth": "256",
        "format": "json",
    }
    resp = session.get(WIKIMEDIA_API, params=params, timeout=30)
    data = resp.json()
    pages = data.get("query", {}).get("pages", {})
    for pid, page in pages.items():
        title = page.get("title", "?")
        if pid == "-1":
            print(f"NOT FOUND: {title}")
            return
        ii = page.get("imageinfo", [])
        if not ii:
            print(f"NO IMAGEINFO: {title}")
            return
        info = ii[0]
        print(f"FOUND: {title}")
        print(f"  mime={info.get('mime')}  mediatype={info.get('mediatype')}")
        print(f"  url      = {info.get('url', '')[:100]}")
        print(f"  thumburl = {info.get('thumburl', '')[:100]}")


def main():
    filenames = sys.argv[1:] if len(sys.argv) > 1 else FAILING
    session = requests.Session()
    session.headers["User-Agent"] = "GlobalStrategyAssetDownloader/1.0 (https://github.com/KonH/GlobalStrategy)"
    for fname in filenames:
        check(session, fname)
        print()


if __name__ == "__main__":
    main()
